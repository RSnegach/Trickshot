using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Sits on the kicking foot (and lower leg). Watches for ball contact and, when
    /// the striker is reclining in the air AND the body is sufficiently tipped back,
    /// classifies it as a valid bicycle-kick contact: flags the ball as a trick shot and
    /// adds a bonus toward goal. Contact at the wrong time is just a normal physical knock
    /// (an awkward failure). The ball owns the shot-cam cut and the bonus ceiling.
    /// </summary>
    public class KickDetector : MonoBehaviour
    {
        Striker _striker;
        ActiveRagdoll _ragdoll;
        BallController _ball;

        float _cooldown;

        public void Init(Striker striker, ActiveRagdoll ragdoll, BallController ball)
        {
            _striker = striker;
            _ragdoll = ragdoll;
            _ball = ball;
        }

        void Update()
        {
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;
        }

        void OnCollisionEnter(Collision c)
        {
            if (_cooldown > 0f) return;
            var ball = c.collider.GetComponentInParent<BallController>();
            if (ball == null || ball != _ball) return;

            // The Striker latches a bicycle window when the airborne body commits to a flip
            // (see Striker.TrickActive). Trust that ONE robust signal instead of re-reading
            // the pelvis angle at this exact contact frame - a fast flip sweeps through the
            // reclined cone in ~2 frames, so an instantaneous re-check here used to miss most
            // legitimate bikes (the bonus + trick classification silently didn't fire).
            if (!_striker.TrickActive) return; // not a latched bicycle attempt -> just physics

            // One flip touches the ball with several bones (calf + foot, either leg), each
            // carrying its own detector. Claim the bonus on the SHARED ball so only the first
            // contact applies it - otherwise the impulse stacks 2x-4x into an absurd shot.
            if (!ball.TryClaimTrickBonus()) { _cooldown = 0.5f; return; }

            // Valid bicycle kick. Add a bonus impulse toward goal + upward. The claim above also
            // arms the ball's post-bonus speed clamp, so this cannot lift a shot past the ceiling the
            // strike itself is held to - which is what let a quadruped's bike out-hit a human's.
            // Bonus direction and size scale with Shooting+Control: a flatter, slightly faster
            // bike the more invested the player is. The old fixed 0.55 up component was half the
            // reason a bike left as a high looper (the strike's own vy is the other half, handled
            // in BallController).
            float bike = PlayerProfile.BicycleSkill01;
            float lift = Mathf.Lerp(SimConfig.BicycleBonusLiftRaw, SimConfig.BicycleBonusLiftSkilled, bike);
            float pace = Mathf.Lerp(SimConfig.BicyclePaceRaw, SimConfig.BicyclePaceSkilled, bike);
            Vector3 toGoal = SimConfig.GoalCenter - ball.transform.position;
            toGoal.y = 0f;
            Vector3 dir = (toGoal.normalized + Vector3.up * lift).normalized;
            ball.Rb.AddForce(dir * (SimConfig.ValidHitBonus * pace), ForceMode.VelocityChange);
            ball.LastShotWasTrick = true;
            ball.LastShotType = ShotType.Bicycle;

            _cooldown = 0.5f;
        }
    }
}
