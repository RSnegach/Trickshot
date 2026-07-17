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
    ///   Recline back .... E (hold, airborne) - tip backward for a bicycle setup
    ///   Reset ........... R
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        InputActionAsset _asset;
        InputActionMap _map;
        InputAction _move, _look, _jump, _reset, _legL, _legR, _recline, _ballCam, _sprint;
        PlayerInput _playerInput;

        public void Init()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _map = _asset.AddActionMap("Play");

            _move = _map.AddAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");

            _look   = _map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            _jump   = _map.AddAction("Jump",   InputActionType.Button, "<Keyboard>/space");
            _reset  = _map.AddAction("Reset",  InputActionType.Button, "<Keyboard>/r");
            _legL   = _map.AddAction("LegL",   InputActionType.Button, "<Mouse>/leftButton");
            _legR   = _map.AddAction("LegR",   InputActionType.Button, "<Mouse>/rightButton");
            _recline = _map.AddAction("Recline", InputActionType.Button, "<Keyboard>/e");
            _ballCam = _map.AddAction("BallCam", InputActionType.Button, "<Keyboard>/v");
            _sprint = _map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _sprint.AddBinding("<Keyboard>/rightShift");

            _map.Enable();

            // Architectural seam for local multiplayer; gameplay still polls actions.
            _playerInput = gameObject.AddComponent<PlayerInput>();
            _playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            _playerInput.actions = _asset;
            _playerInput.defaultActionMap = "Play";

            LockCursor();
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

        // E held: recline backward while airborne (bicycle setup).
        public bool ReclineHeld => _recline != null && _recline.IsPressed();

        // Shift held: sprint.
        public bool SprintHeld => _sprint != null && _sprint.IsPressed();
    }
}
