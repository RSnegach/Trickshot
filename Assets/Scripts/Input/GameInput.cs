using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Input for the single-player keyboard + mouse prototype, built on Unity's
    /// Input System. The InputActionAsset is created entirely in code (no .inputactions
    /// asset to wire), and a PlayerInput component is attached so the architecture is
    /// ready for PlayerInputManager / local multiplayer later, as the spec directs.
    ///
    /// Gameplay reads state by polling the actions each frame, which is robust and
    /// independent of PlayerInput's notification behaviour.
    ///
    /// The player controls only the striker. Crosses are served automatically.
    ///
    /// Controls:
    ///   Move ............ WASD / Arrows   (world-relative)
    ///   Camera .......... Mouse move      (orbit); V toggles ball-lock
    ///   Jump ............ Space
    ///   Left leg up ..... Left Mouse (hold)
    ///   Right leg up .... Right Mouse (hold)
    ///   Back-ragdoll .... Space (airborne) - flop onto his back (bicycle setup)
    ///   Reset ........... R
    /// </summary>
    public class GameInput : MonoBehaviour, IStrikerInput
    {
        InputActionAsset _asset;
        InputActionMap _map;
        InputAction _move, _look, _jump, _reset, _legL, _legR, _ballCam, _sprint, _scroll;
        InputAction _passGround, _passLofted, _switchPlayer, _emote, _tackle;   // scrimmage
        InputAction _crossMap;   // striker mode: toggle the cross-targeting map (fixed M)
        PlayerInput _playerInput;

        public void Init()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = _asset.AddActionMap("Play");

            // Movement is a WASD-style 2D vector whose four directions come from the
            // rebindable MoveUp/Down/Left/Right binds, plus a fixed arrow-key fallback.
            _move = _map.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up",    Keybinds.Path("MoveUp"))
                .With("Down",  Keybinds.Path("MoveDown"))
                .With("Left",  Keybinds.Path("MoveLeft"))
                .With("Right", Keybinds.Path("MoveRight"));
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            // Camera + scroll are the mouse itself, never rebindable.
            _look   = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _scroll = _map.AddAction("Scroll", InputActionType.Value, "<Mouse>/scroll/y");

            // The rest are single rebindable button actions built from Keybinds.
            _jump        = Btn("Jump");
            _reset       = Btn("Reset");
            _legL        = Btn("LegL");
            _legR        = Btn("LegR");
            _ballCam     = Btn("BallCam");
            _sprint      = Btn("Sprint");
            _passGround  = Btn("PassGround");
            _passLofted  = Btn("PassLofted");
            _switchPlayer = Btn("Switch");
            _emote       = Btn("Emote");
            _tackle      = Btn("Tackle");
            // Striker-mode cross map: fixed to M (not in the rebind list).
            _crossMap    = _map.AddAction("CrossMap", InputActionType.Button, "<Keyboard>/m");

            _map.Enable();

            // Architectural seam for local multiplayer; gameplay still polls actions.
            _playerInput = gameObject.AddComponent<PlayerInput>();
            _playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            _playerInput.actions = _asset;
            _playerInput.defaultActionMap = "Play";

            LockCursor();
        }

        // Build a single rebindable button action from its Keybinds entry.
        InputAction Btn(string action)
            => _map.AddAction(action, InputActionType.Button, Keybinds.Path(action));

        // ---- runtime rebinding ----
        // Apply a new control path to an action's binding (used by the options menu after a
        // successful rebind). Rebuilds the Move composite when a movement direction changes.
        public void ApplyBinding(string action, string path)
        {
            Keybinds.Set(action, path);
            var a = _map.FindAction(action);
            if (a != null)
            {
                a.Disable();
                a.ApplyBindingOverride(0, path);
                a.Enable();
                return;
            }
            // Movement direction: override the matching part of the Move composite (index
            // 1..4 are Up/Down/Left/Right of the first 2DVector composite).
            int part = action == "MoveUp" ? 1 : action == "MoveDown" ? 2
                     : action == "MoveLeft" ? 3 : action == "MoveRight" ? 4 : -1;
            if (part >= 0)
            {
                _move.Disable();
                _move.ApplyBindingOverride(part, path);
                _move.Enable();
            }
        }

        // Listen for the next key / mouse-button press and report its control path, so the
        // options menu can rebind interactively. Cancels on Escape (reports null).
        public InputActionRebindingExtensions.RebindingOperation StartRebind(string action, System.Action<string> onComplete)
        {
            var a = (action == "MoveUp" || action == "MoveDown" || action == "MoveLeft" || action == "MoveRight")
                    ? _move : _map.FindAction(action);
            if (a == null) { onComplete?.Invoke(null); return null; }

            int part = action == "MoveUp" ? 1 : action == "MoveDown" ? 2
                     : action == "MoveLeft" ? 3 : action == "MoveRight" ? 4 : 0;

            a.Disable();
            var op = a.PerformInteractiveRebinding(part)
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/scroll")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(o =>
                {
                    string path = o.selectedControl != null ? o.selectedControl.path : null;
                    // path comes back like "/Keyboard/w"; normalise to "<Keyboard>/w".
                    string norm = NormalizePath(path);
                    o.Dispose();
                    a.Enable();
                    if (!string.IsNullOrEmpty(norm)) { Keybinds.Set(action, norm); onComplete?.Invoke(norm); }
                    else onComplete?.Invoke(null);
                })
                .OnCancel(o => { o.Dispose(); a.Enable(); onComplete?.Invoke(null); });
            op.Start();
            return op;
        }

        // "/Keyboard/w" -> "<Keyboard>/w".
        static string NormalizePath(string ctrlPath)
        {
            if (string.IsNullOrEmpty(ctrlPath)) return null;
            int firstSlash = ctrlPath.IndexOf('/', 1);
            if (ctrlPath.StartsWith("/") && firstSlash > 0)
            {
                string device = ctrlPath.Substring(1, firstSlash - 1);
                string rest = ctrlPath.Substring(firstSlash + 1);
                return $"<{device}>/{rest}";
            }
            return ctrlPath;
        }

        static void LockCursor()
        {
            // Minecraft-style: pointer stays centred and hidden, only mouse delta is
            // used (the camera reads <Mouse>/delta). In the editor, Esc frees it.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // Re-lock when the game window regains focus (Esc / Alt-Tab releases it).
        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) LockCursor();
        }

        void OnDestroy()
        {
            if (_map != null) _map.Disable();
            if (_asset != null) Destroy(_asset);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // --- polled state ---
        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _look != null ? _look.ReadValue<Vector2>() : Vector2.zero;

        public bool JumpPressed => _jump != null && _jump.WasPressedThisFrame();
        public bool JumpHeld => _jump != null && _jump.IsPressed();
        public bool JumpReleased => _jump != null && _jump.WasReleasedThisFrame();
        // Forward (W / up) held, read from the Move composite's y axis.
        public bool ForwardHeld => Move.y > 0.4f;
        public bool ResetPressed => _reset != null && _reset.WasPressedThisFrame();
        public bool BallCamPressed => _ballCam != null && _ballCam.WasPressedThisFrame();

        // Held leg controls: LMB = left leg up, RMB = right leg up.
        public bool LeftLegHeld  => _legL != null && _legL.IsPressed();
        public bool RightLegHeld => _legR != null && _legR.IsPressed();

        // Click edges (LMB used to skip the replay; both used for keeper save lunges).
        public bool LeftClickPressed => _legL != null && _legL.WasPressedThisFrame();
        public bool RightClickPressed => _legR != null && _legR.WasPressedThisFrame();

        // Shift held: sprint.
        public bool SprintHeld => _sprint != null && _sprint.IsPressed();

        // Mouse wheel Y this frame (raw; ~120 per notch on Windows). Used to pitch the
        // striker about his central axis while airborne.
        public float Scroll => _scroll != null ? _scroll.ReadValue<float>() : 0f;

        // Scrimmage: Q ground pass, E lofted pass. Held + released so the pass can charge
        // (tap = soft, hold = hard). Pressed kept for the call-for-pass (no-ball) case.
        public bool PassGroundPressed => _passGround != null && _passGround.WasPressedThisFrame();
        public bool PassLoftedPressed => _passLofted != null && _passLofted.WasPressedThisFrame();
        public bool PassGroundHeld => _passGround != null && _passGround.IsPressed();
        public bool PassLoftedHeld => _passLofted != null && _passLofted.IsPressed();
        public bool PassGroundReleased => _passGround != null && _passGround.WasReleasedThisFrame();
        public bool PassLoftedReleased => _passLofted != null && _passLofted.WasReleasedThisFrame();
        public bool SwitchPressed => _switchPlayer != null && _switchPlayer.WasPressedThisFrame();
        // Emote wheel: held open while B is down.
        public bool EmoteHeld => _emote != null && _emote.IsPressed();
        public bool EmotePressed => _emote != null && _emote.WasPressedThisFrame();
        // Tackle / slide challenge (C).
        public bool TacklePressed => _tackle != null && _tackle.WasPressedThisFrame();
        // Striker cross-targeting map toggle (M).
        public bool CrossMapPressed => _crossMap != null && _crossMap.WasPressedThisFrame();

        // Sample the current device state into a network InputFrame for sending to the host.
        // Booleans are HELD states (edges are re-derived on the receiving side per tick).
        public Net.InputFrame SampleFrame(uint tick, float lookYaw)
        {
            return new Net.InputFrame
            {
                tick = tick, move = Move, lookYaw = lookYaw,
                jump = JumpHeld, legL = LeftLegHeld, legR = RightLegHeld, sprint = SprintHeld,
                passGround = PassGroundHeld, passLofted = PassLoftedHeld, tackle = TacklePressed,
            };
        }
    }
}
