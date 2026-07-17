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
    /// Controls:
    ///   Move ............ WASD / Arrows   (striker movement)
    ///   Aim ............. Mouse           (reticle, read in AimReticle)
    ///   Charge cross .... Hold + release Space   (crosser)
    ///   Curl ............ Q / E while charging (inward / outward)
    ///   Jump ............ Space           (striker, after the cross is live)
    ///   Bicycle kick .... Left Mouse / F  (striker)
    ///   Reset ........... R
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        InputActionAsset _asset;
        InputActionMap _map;
        InputAction _move, _charge, _jump, _kick, _reset, _curlL, _curlR, _ballCam;
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

            _charge = _map.AddAction("Charge", InputActionType.Button, "<Keyboard>/space");
            _jump   = _map.AddAction("Jump",   InputActionType.Button, "<Keyboard>/space");
            _kick   = _map.AddAction("Kick",   InputActionType.Button, "<Mouse>/leftButton");
            _kick.AddBinding("<Keyboard>/f");
            _reset  = _map.AddAction("Reset",  InputActionType.Button, "<Keyboard>/r");
            _curlL  = _map.AddAction("CurlL",  InputActionType.Button, "<Keyboard>/q");
            _curlR  = _map.AddAction("CurlR",  InputActionType.Button, "<Keyboard>/e");
            _ballCam = _map.AddAction("BallCam", InputActionType.Button, "<Keyboard>/v");

            _map.Enable();

            // Architectural seam for local multiplayer; gameplay still polls actions.
            _playerInput = gameObject.AddComponent<PlayerInput>();
            _playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            _playerInput.actions = _asset;
            _playerInput.defaultActionMap = "Play";
        }

        void OnDestroy()
        {
            if (_map != null) _map.Disable();
            if (_asset != null) Destroy(_asset);
        }

        // --- polled state ---
        public Vector2 Move => _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;

        public bool ChargeHeld     => _charge != null && _charge.IsPressed();
        public bool ChargePressed  => _charge != null && _charge.WasPressedThisFrame();
        public bool ChargeReleased => _charge != null && _charge.WasReleasedThisFrame();

        public bool JumpPressed => _jump != null && _jump.WasPressedThisFrame();
        public bool KickPressed => _kick != null && _kick.WasPressedThisFrame();
        public bool ResetPressed => _reset != null && _reset.WasPressedThisFrame();
        public bool BallCamPressed => _ballCam != null && _ballCam.WasPressedThisFrame();

        /// <summary>-1 curl one way, +1 the other, while holding Q/E.</summary>
        public float CurlAxis
        {
            get
            {
                float a = 0f;
                if (_curlL != null && _curlL.IsPressed()) a -= 1f;
                if (_curlR != null && _curlR.IsPressed()) a += 1f;
                return a;
            }
        }
    }
}
