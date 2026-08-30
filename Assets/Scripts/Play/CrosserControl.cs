using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Human control for the crosser role: LOOK where you want the cross to go, hold LMB or RMB
    /// (your footedness) to charge it, release to deliver - the same "aim = where you look, charge =
    /// power" language the shot mechanic uses, so the two deliveries feel like one game rather than
    /// two different control schemes glued together. Replaces the old M-key map-click aim, which was
    /// "no skill based UI... click where you want it" rather than something a player has to time and
    /// aim in the moment.
    ///
    /// AIM. The target is a point along the crosser's own flat look-ray, CHARGE deciding how far it
    /// reaches (a tap stays short, a full hold reaches deep) - then clamped into a legal delivery box
    /// around the goal, so aim and power are the same two inputs a real corner needs: which way
    /// you're looking, and how hard you hit it. Relative to wherever he is actually standing, not a
    /// fixed plane off the goal line - see SimConfig.CrossAimNearReach for why that broke.
    ///
    /// FOOTEDNESS. Which button you held sets the curl's SIGN (a fixed left-vs-right split, not a
    /// claim about which wing produces a real inswinger vs outswinger - that would also need to know
    /// which side of the pitch the crosser is standing on, which this does not attempt). Curl peaks at
    /// a half-charge and tapers at both ends, the same shape BallController.LaunchChargedShot uses for
    /// the shot's own curl.
    ///
    /// Host-authoritative, same as before: only the host's CrosserControl actually launches the ball
    /// (the Crosser it drives is the host's real one). A client's crosser body is a display puppet;
    /// its CrosserControl is not ticked, so it never serves - it only shows the host's result. Input
    /// comes through IStrikerInput, so this works for the local host crosser (GameInput) or a remote
    /// human crosser (NetInputSource fed from the wire), unchanged - and the camera yaw fed in via
    /// SetCameraYaw must be the SAME source (NetStrikerMatch wires both off b.netInput.LookYaw for a
    /// remote crosser), or the aim this solves would disagree with which way his body is actually
    /// facing on every other peer's screen.
    /// </summary>
    public class CrosserControl : MonoBehaviour
    {
        IStrikerInput _input;
        Crosser _crosser;
        System.Func<float> _camYaw;

        float _chargeL, _chargeR;             // hold time per foot (LMB / RMB)
        bool _armedL, _armedR;                // armed on press; a hold that began mid-delivery never arms
        bool _prevLegL, _prevLegR;             // for press/release edges - IStrikerInput has no leg Released

        public void Init(IStrikerInput input, Crosser crosser)
        {
            _input = input;
            _crosser = crosser;
            if (_crosser != null) _crosser.AutoServe = false;   // human decides when to cross
        }

        /// <summary>Camera yaw source for AIM. Local host crosser: () => cam.Yaw. Remote human
        /// crosser: () => netInput.LookYaw - must be the exact source fed to that body's own
        /// Striker.SetCameraYaw, or this solves an aim his body isn't actually facing.</summary>
        public void SetCameraYaw(System.Func<float> camYaw) => _camYaw = camYaw;

        /// <summary>0..1 charge of whichever foot is charging, for a HUD power bar. Mirrors
        /// Striker.ShotCharge01 so Hud.ShotBar can be reused verbatim for the crosser.</summary>
        public float Charge01 => Mathf.Clamp01(Mathf.Max(_chargeL, _chargeR) / SimConfig.CrossMaxCharge);

        /// <summary>Holding exactly one of LMB/RMB, and actually free to deliver right now (not
        /// already mid-serve). Mirrors Striker.WantsChargedShot's shape for the same HUD reuse.</summary>
        public bool Holding => _input != null && _crosser != null && _crosser.ReadyToServe
                               && _input.LeftLegHeld != _input.RightLegHeld;

        // Host ticks this each frame. Charges on hold, serves on release: a bare tap is a driven
        // ball, a held press is a chip (charge scales the float AND how deep the aim reaches).
        public void Tick()
        {
            if (_input == null || _crosser == null) return;

            bool legL = _input.LeftLegHeld, legR = _input.RightLegHeld;
            bool pressL = legL && !_prevLegL, relL = !legL && _prevLegL;
            bool pressR = legR && !_prevLegR, relR = !legR && _prevLegR;
            _prevLegL = legL; _prevLegR = legR;

            bool gesture = legL && legR;   // both together: not a delivery input, don't arm either

            if (pressL && !gesture) { _armedL = true; _chargeL = 0f; }
            if (pressR && !gesture) { _armedR = true; _chargeR = 0f; }
            if (gesture) { _armedL = false; _armedR = false; }

            if (_armedL && legL && !gesture) _chargeL = Mathf.Min(_chargeL + Time.deltaTime, SimConfig.CrossMaxCharge);
            if (_armedR && legR && !gesture) _chargeR = Mathf.Min(_chargeR + Time.deltaTime, SimConfig.CrossMaxCharge);

            if (relL) { if (_armedL) Serve(_chargeL, leftFoot: true); _armedL = false; _chargeL = 0f; }
            if (relR) { if (_armedR) Serve(_chargeR, leftFoot: false); _armedR = false; _chargeR = 0f; }
        }

        void Serve(float held, bool leftFoot)
        {
            if (!_crosser.ReadyToServe) return;

            float charge01 = Mathf.Clamp01(held / SimConfig.CrossMaxCharge);
            bool chipped = held >= SimConfig.CrossTapMaxHold;

            Vector3 origin = _crosser.Origin; origin.y = 0f;
            float yaw = _camYaw != null ? _camYaw() : 0f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // Reach ALONG the look-ray from wherever he actually stands (ServeFromFeet - he walks
            // freely), then clamp into a legal delivery box. Relative-to-self, not an absolute plane:
            // see SimConfig's own comment on CrossAimNearReach for why a fixed Z-plane broke.
            float reach = Mathf.Lerp(SimConfig.CrossAimNearReach, SimConfig.CrossAimFarReach, charge01);
            Vector3 raw = origin + dir * reach;
            float x = Mathf.Clamp(raw.x, SimConfig.GoalCenter.x - SimConfig.CrossAimHalfWidth,
                                        SimConfig.GoalCenter.x + SimConfig.CrossAimHalfWidth);
            float z = Mathf.Clamp(raw.z, SimConfig.GoalCenter.z - SimConfig.CrossAimMaxDepth,
                                        SimConfig.GoalCenter.z - SimConfig.CrossAimMinDepth);
            Vector3 target = new Vector3(x, chipped ? 0.25f : SimConfig.BallRadius, z);

            // Curl: perpendicular to the ACTUAL flight direction (origin -> target), not the raw look
            // ray, so it reads the same way the shot's curl does - a sideways push on the real path,
            // not on where the aim ray happened to point. Same peaks-at-mid-charge shape as the shot.
            Vector3 flat = target - origin; flat.y = 0f;
            Vector3 flatDir = flat.sqrMagnitude > 0.0001f ? flat.normalized : dir;
            float shape = 0.25f + 0.75f * Mathf.Sin(charge01 * Mathf.PI);
            float footSign = leftFoot ? 1f : -1f;
            Vector3 curl = Vector3.Cross(Vector3.up, flatDir) * (footSign * SimConfig.CrossCurlAccMax * shape);

            _crosser.ServeNow(target, chipped, charge01, 0f, curl);
        }
    }
}
