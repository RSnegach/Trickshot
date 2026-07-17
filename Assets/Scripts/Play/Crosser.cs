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

        float _timer;
        Vector3 _pendingTarget;
        float _pendingTime;
        Vector3 _pendingCurl;
        float _pendingSpin;
        bool _telegraphed;
        float _swing;        // 0 = neutral, ramps to 1 at contact (drives the leg pose)

        public bool JustServed { get; private set; }

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
            _timer = firstDelay;
            _telegraphed = false;
            _swing = 0f;
            JustServed = false;
            _reticle.Hide();
            _ball.ResetTo(_launchPoint.position);
        }

        /// <summary>Advance the serve timer and self-loop: winds up + swings the leg, fires
        /// a perfect cross at contact, then re-arms ServeInterval later. Returns true on the
        /// frame the ball launches.</summary>
        public bool Tick()
        {
            JustServed = false;

            // ~windup before launch: pick the target, show the telegraph, start the swing.
            if (!_telegraphed && _timer <= SimConfig.CrosserWindupTime)
            {
                PickServe();
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
                _timer = SimConfig.ServeInterval;   // re-arm for the next constant serve
                _telegraphed = false;
                return true;
            }
            return false;
        }

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
            // Same landing spot and flight every serve, no curl (predictable practice).
            _pendingTarget = SimConfig.ServeTarget;
            _pendingTime = SimConfig.ServeTime;
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
        }

        void Launch()
        {
            _ball.ResetTo(_launchPoint.position);
            _ball.LaunchTo(_pendingTarget, _pendingTime, _pendingCurl, _pendingSpin);
            _reticle.Hide();
            _swing = 1f;         // held on the follow-through a moment
            JustServed = true;
        }
    }
}
