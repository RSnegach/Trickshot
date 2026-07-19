using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Arcade close-control dribbling for the player striker. A soft magnet: whenever the
    /// ball is near the striker's feet and slow (and he is grounded, not mid-trick), it
    /// sticks to a CARRY POINT just in front of him and travels with his run, arcade-style.
    ///
    ///  - Capture is automatic - no button. Walk near a loose/slow ball and it joins you.
    ///  - The carry point sits a short way ahead at a walk and is pushed FURTHER ahead
    ///    when sprinting (a heavier touch you have to chase), tighter when you slow down.
    ///  - A KICK releases the leash and strikes the ball: either the leg button (LMB/RMB)
    ///    or a genuinely fast leg swing into the ball. The shot flies in the facing/aim
    ///    direction with real power (scaled by ShotPowerMul + Shooting nodes).
    ///  - The Control "trap" stat (First Touch / Cushion) tightens everything: closer
    ///    carry and a wider capture net, so a Control build glues the ball to their feet.
    ///
    /// While carrying, this component tells the BallController to skip its own strike/trap
    /// contact logic (BallController.DribbleHold), so the run-cycle foot taps never fight
    /// the magnet. Physics only: it steers the ball's velocity with a capped spring, never
    /// teleports it, so collisions with keepers/walls still knock it loose naturally.
    /// </summary>
    public class Dribble : MonoBehaviour
    {
        GameInput _input;
        Striker _striker;
        ActiveRagdoll _ragdoll;
        BallController _ball;

        bool _carrying;
        float _cooldown;        // after a shot, don't re-capture for a moment

        // Master switch: dribbling is only ENABLED in modes that want it (a real match).
        // Shooting-on-goal modes (Striker, challenges, keeper) leave this false so the ball
        // never snaps to the feet. Off by default; the mode builder opts in.
        public bool Enabled = false;

        // Set-piece suspension: a free kick / penalty (or any dead-ball setup) turns this on
        // so the ball parked at the spot is NOT auto-captured while the taker walks up. The
        // game mode clears it once the kick is taken / play is live again.
        public bool SetPieceActive = false;

        public bool Carrying => _carrying;

        public void Init(GameInput input, Striker striker, ActiveRagdoll ragdoll, BallController ball)
        {
            _input = input;
            _striker = striker;
            _ragdoll = ragdoll;
            _ball = ball;
        }

        // Tightness 0..1 from the Control trap stat: closer carry + bigger capture net.
        float Tightness => PlayerProfile.DribbleTightness;

        // Where the ball wants to sit: in front of the feet along the facing, further out
        // when sprinting, pulled closer by Control. Height rides at the ball radius.
        Vector3 CarryPoint()
        {
            float sprintT = _input != null && _input.SprintHeld ? 1f : 0f;
            float dist = Mathf.Lerp(SimConfig.DribbleNearDistance, SimConfig.DribbleSprintDistance, sprintT);
            dist *= 1f - SimConfig.DribbleTrapTightenMax * Tightness;   // Control pulls it in

            Vector3 feet = _ragdoll.Pelvis.position;
            feet.y = 0f;
            Vector3 p = feet + _striker.FacingForward * dist;
            p.y = SimConfig.BallRadius;
            return p;
        }

        float CaptureRadius => SimConfig.DribbleCaptureRadius + SimConfig.DribbleTrapCaptureBonus * Tightness;

        void FixedUpdate()
        {
            if (_ball == null || _ragdoll == null || _ragdoll.Pelvis == null) return;

            // Off entirely unless the mode enables dribbling, and never during a set piece
            // (free kick / penalty) - the ball must stay parked at the spot, not snap to the
            // feet. Drop any carry and bail so nothing captures.
            if (!Enabled || SetPieceActive) { StopCarry(); return; }

            if (_cooldown > 0f) _cooldown -= Time.fixedDeltaTime;

            // Can't dribble airborne or mid-trick (dive/bicycle own the body + ball).
            bool canDribble = _ragdoll.IsGrounded && !_striker.IsBusy && _striker.ControlEnabled;
            if (!canDribble) { StopCarry(); return; }

            Vector3 carry = CarryPoint();
            Vector3 ballPos = _ball.Rb.position;
            float distToCarry = Vector3.Distance(ballPos, carry);

            if (_carrying)
            {
                // A kick (leg button, or a genuinely fast leg swing into the ball) releases
                // the leash and strikes the ball as a shot.
                if (WantsKick())
                {
                    ReleaseShot();
                    return;
                }
                // Ball knocked too far away (keeper punch, wall, bad bounce) -> lose it.
                if (distToCarry > SimConfig.DribbleReleaseRadius)
                {
                    StopCarry();
                    return;
                }
                Follow(carry);
            }
            else
            {
                // Auto-capture: near the carry point, slow enough, off cooldown.
                if (_cooldown <= 0f
                    && distToCarry <= CaptureRadius
                    && _ball.Rb.linearVelocity.magnitude <= SimConfig.DribbleCaptureMaxSpeed)
                {
                    StartCarry();
                    Follow(carry);
                }
            }
        }

        void StartCarry()
        {
            _carrying = true;
            _ball.DribbleHold = true;   // BallController skips its strike/trap logic while held
            IgnoreStrikerCollision(true);  // the SPRING owns the ball; feet can't punt it
        }

        void StopCarry()
        {
            if (!_carrying) return;
            _carrying = false;
            _ball.DribbleHold = false;
            IgnoreStrikerCollision(false);
        }

        // Toggle physical collision between the ball and the striker's own body colliders.
        // While carrying, the run/walk gait would otherwise boot the held ball around; with
        // collisions off, only the follow spring moves it, so the carry stays glued.
        void IgnoreStrikerCollision(bool ignore)
        {
            var ballCol = _ball.GetComponent<Collider>();
            if (ballCol == null) return;
            var own = _ragdoll.OwnColliders;
            for (int i = 0; i < own.Count; i++)
                if (own[i] != null) Physics.IgnoreCollision(ballCol, own[i], ignore);
        }

        // Spring the ball toward the carry point with a capped acceleration, plus a little
        // lead velocity so it travels ahead with the run instead of trailing. Rolling spin
        // is set to match the carry speed for looks.
        void Follow(Vector3 carry)
        {
            var rb = _ball.Rb;
            Vector3 toCarry = carry - rb.position;

            // Feed-forward the striker's INTENDED horizontal velocity (MoveInput, the clean
            // target the locomotion steers toward - smoother than the noisy pelvis velocity)
            // so the ball keeps pace with the moving carry point instead of trailing.
            Vector3 strikerVel = _ragdoll.MoveInput; strikerVel.y = 0f;
            Vector3 lead = strikerVel * SimConfig.DribbleLeadSpeedFrac;

            Vector3 relVel = rb.linearVelocity - lead;
            Vector3 accel = toCarry * SimConfig.DribbleFollowAccel - relVel * SimConfig.DribbleFollowDamp;
            // Don't fight gravity on the vertical axis with the spring; let it rest on the
            // ground. Only steer horizontally + gently seat it to carry height.
            accel = Vector3.ClampMagnitude(accel, SimConfig.DribbleMaxAccel);
            rb.AddForce(accel, ForceMode.Acceleration);

            // Rolling spin about the axis perpendicular to travel, for looks.
            Vector3 flatVel = rb.linearVelocity; flatVel.y = 0f;
            if (flatVel.sqrMagnitude > 0.04f)
            {
                Vector3 spinAxis = Vector3.Cross(Vector3.up, flatVel.normalized);
                rb.angularVelocity = spinAxis * (flatVel.magnitude * SimConfig.DribbleSpinScale);
            }
        }

        // A kick request: a leg button (LMB/RMB) pressed this frame. ONLY the button
        // breaks the leash - the run/walk gait swings the feet past any speed threshold,
        // so a fast-swing test would boot the ball just from moving. Button-only means
        // walking and running with the ball is a pure carry; you shoot only on purpose.
        bool WantsKick()
        {
            return _input != null && (_input.LeftClickPressed || _input.RightClickPressed);
        }

        // Release the leash and launch the ball as a shot along the facing/aim direction,
        // scaled by the striker's shot power. Routes through BallController.DribbleShot so
        // it shares the facing-gated goal assist + ball-cam pulse with normal strikes. Then
        // hold off re-capture so the same touch doesn't immediately re-grab the ball.
        void ReleaseShot()
        {
            Vector3 dir = (_striker.FacingForward + Vector3.up * SimConfig.DribbleShotLift).normalized;
            float speed = SimConfig.DribbleShotSpeed * PlayerProfile.ShotPowerMul;

            // Same sight-cone gate as a struck shot: only assist when facing the goal.
            Vector3 toGoal = SimConfig.GoalCenter - _ragdoll.Pelvis.position; toGoal.y = 0f;
            Vector3 face = _striker.FacingForward;
            float dot = toGoal.sqrMagnitude > 0.01f ? Vector3.Dot(face, toGoal.normalized) : -1f;
            bool facingGoal = dot >= SimConfig.AssistFacingDot;        // tight cone: aim assist
            bool camFacingGoal = dot > SimConfig.ShotCamFacingDot;     // wide cone: ball-cam (never facing own goal)

            StopCarry();   // drop the leash BEFORE the shot so DribbleHold doesn't block it
            _ball.DribbleShot(dir, speed, facingGoal, camFacingGoal);
            _cooldown = SimConfig.DribbleRecaptureCooldown;
        }

        // Cut the leash on hard resets.
        public void ForceRelease()
        {
            StopCarry();
            _cooldown = SimConfig.DribbleRecaptureCooldown;
        }
    }
}
