using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Auto-server. An active-ragdoll character on the wing that plays a right-leg SWING
    /// at the ball and launches a perfectly-solved cross at the moment of contact. The
    /// swing is cosmetic: the ball is always delivered on target regardless of the pose.
    ///
    /// It telegraphs the landing point with the reticle, winds the kicking leg back, then
    /// swings through - the launch fires as the leg passes through the ball. GameManager
    /// pumps Tick() and reads JustServed.
    /// </summary>
    public class Crosser : MonoBehaviour
    {
        AimReticle _reticle;
        BallController _ball;
        Transform _launchPoint;
        ActiveRagdoll _ragdoll;
        public ActiveRagdoll Ragdoll => _ragdoll;   // so the net match can slot/puppet the crosser body

        float _timer;
        Vector3 _pendingTarget;
        float _pendingTime;
        Vector3 _pendingCurl;
        float _pendingSpin;
        bool _telegraphed;
        float _swing;        // 0 = neutral, ramps to 1 at contact (drives the leg pose)

        // Delivery overrides (freeplay). If TargetOverride is set, serves land there
        // instead of SimConfig.ServeTarget. If OriginOverride is set, the ball launches
        // from that world point (a corner flag) instead of the crosser's launch point.
        public Vector3? TargetOverride;
        public Vector3? OriginOverride;

        public bool JustServed { get; private set; }

        // When true (default), the crosser auto-serves on the ServeInterval loop. Set false so
        // it stays idle until ServeNow() is called (a human crosser, or a striker's called
        // pass). The cosmetic swing + perfect launch are shared by both paths.
        public bool AutoServe = true;

        public void Init(AimReticle reticle, BallController ball, Transform launchPoint, ActiveRagdoll ragdoll)
        {
            _reticle = reticle;
            _ball = ball;
            _launchPoint = launchPoint;
            _ragdoll = ragdoll;
            // Stand planted on the wing (upright lock so he doesn't topple).
            if (_ragdoll != null)
            {
                _ragdoll.UprightLock = true;
                _ragdoll.LocomotionEnabled = false;
                _ragdoll.MoveInput = Vector3.zero;
            }
        }

        public void Arm(float firstDelay)
        {
            // Auto mode counts down to the next serve; manual mode stays idle until ServeNow.
            _timer = AutoServe ? firstDelay : float.PositiveInfinity;
            _telegraphed = false;
            _manualPending = false;
            _swing = 0f;
            JustServed = false;
            _reticle.Hide();
            _ball.ResetTo(Origin);
        }

        /// <summary>Park the crosser fully idle: no pending serve, no telegraph, reticle hidden.
        /// Used when the crosser slot is empty and the host disabled AI fill (it never serves
        /// and is never ticked, so it just stands on the wing).</summary>
        public void Idle()
        {
            AutoServe = false;
            _timer = float.PositiveInfinity;
            _telegraphed = false;
            _manualPending = false;
            _swing = 0f;
            JustServed = false;
            if (_reticle != null) _reticle.Hide();
        }

        /// <summary>Advance the serve timer and self-loop: winds up + swings the leg, fires
        /// a perfect cross at contact, then re-arms ServeInterval later. Returns true on the
        /// frame the ball launches.</summary>
        public bool Tick()
        {
            JustServed = false;

            // ~windup before launch: pick the target, show the telegraph, start the swing.
            // AutoServe picks a default serve; a manual serve (ServeNow) has already set the
            // pending target/time and only needs the swing to play out.
            if (!_telegraphed && _timer <= SimConfig.CrosserWindupTime)
            {
                if (AutoServe && !_manualPending) PickServe();
                _reticle.Show(_pendingTarget);
                _telegraphed = true;
            }

            // Drive the leg swing pose: back-lift through the windup, whipping through 0..1.
            if (_telegraphed && _timer > 0f)
                _swing = 1f - Mathf.Clamp01(_timer / SimConfig.CrosserWindupTime);   // 0 -> 1 at contact
            ApplyKickPose();

            _timer -= Time.deltaTime;
            if (_timer <= 0f && _telegraphed)
            {
                Launch();
                // Auto mode re-arms the constant loop; manual mode goes idle until ServeNow.
                _timer = AutoServe ? SimConfig.ServeInterval : float.PositiveInfinity;
                _telegraphed = false;
                _manualPending = false;
                return true;
            }
            return false;
        }

        // Manual serve to a chosen target: a driven (low, flat) or chipped (high, floaty) ball,
        // its flight time scaled by powerMul (a hold-charge 0..1 floats it more), with optional
        // aim scatter (deg) so low-passing/low-crossing players misplace it. Used by the human
        // crosser and by the striker's call-for-pass. Plays the same windup swing.
        public void ServeNow(Vector3 target, bool lofted, float powerMul, float scatterDeg = 0f)
        {
            float baseTime = lofted ? SimConfig.CrossTimeLoft : SimConfig.CrossTimeDrive;
            float floatMul = Mathf.Lerp(SimConfig.CrossChargeFlatMul, SimConfig.CrossChargeFloatMul,
                                        Mathf.Clamp01(powerMul));
            if (scatterDeg > 0.01f)
            {
                float ang = Random.Range(-scatterDeg, scatterDeg);
                Vector3 from = Origin; from.y = 0f;
                Vector3 flat = target; flat.y = 0f;
                Vector3 rel = flat - from;
                rel = Quaternion.AngleAxis(ang, Vector3.up) * rel;
                target = new Vector3(from.x + rel.x, target.y, from.z + rel.z);
            }
            _pendingTarget = target;
            _pendingTime = Mathf.Max(0.2f, baseTime * floatMul);
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
            _manualPending = true;
            _telegraphed = false;
            _swing = 0f;
            _timer = SimConfig.CrosserWindupTime;   // start the windup now; launches after it
        }
        bool _manualPending;

        // True once idle (manual mode, nothing pending) so a driver knows it can ServeNow.
        public bool ReadyToServe => !_telegraphed && !_manualPending;

        // Cosmetic right-leg kick: thigh swings from drawn-back to through, knee extends,
        // slight torso lean. All pose overrides, cleared each frame by the ragdoll driver.
        void ApplyKickPose()
        {
            if (_ragdoll == null) return;
            _ragdoll.ClearPoseOverrides();
            if (_swing <= 0.001f) return;
            // s: -1 (drawn back) early, +1 (followed through) at contact.
            float s = _swing * 2f - 1f;
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(s * SimConfig.CrosserSwingThigh, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR, new Vector3((1f - _swing) * SimConfig.CrosserSwingCalf, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(SimConfig.CrosserPlantLean * _swing, 0f, 0f));
            // Arms swing for balance.
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * 35f, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(s * 25f, 0f, 0f));
        }

        void PickServe()
        {
            // Landing spot: the delivery override (aim spot / corner target) or the
            // default cross target. Flight is fixed, no curl (predictable practice).
            _pendingTarget = TargetOverride ?? SimConfig.ServeTarget;
            _pendingTime = SimConfig.ServeTime;
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
        }

        Vector3 Origin => OriginOverride ?? _launchPoint.position;

        void Launch()
        {
            _ball.ResetTo(Origin);
            _ball.LaunchTo(_pendingTarget, _pendingTime, _pendingCurl, _pendingSpin);
            _reticle.Hide();
            _swing = 1f;         // held on the follow-through a moment
            JustServed = true;
        }
    }
}
