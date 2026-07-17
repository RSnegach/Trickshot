using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Sits on the kicking foot (and lower leg). Watches for ball contact and, when
    /// the striker is in the bicycle window AND the body is sufficiently inverted,
    /// classifies it as a valid bicycle-kick contact: flags the ball as a trick shot,
    /// adds a bonus toward goal, and reports so the camera can go slow-mo. Contact at
    /// the wrong time is just a normal physical knock (an awkward failure).
    /// </summary>
    public class KickDetector : MonoBehaviour
    {
        Striker _striker;
        ActiveRagdoll _ragdoll;
        BallController _ball;

        public System.Action OnValidTrick;
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

            if (!_striker.BicycleActive) return; // wrong timing -> just physics

            // Body must be leaning back / inverting: pelvis up-vector should point
            // away from world up (dot below threshold means tilted past ~80 deg).
            float upness = Vector3.Dot(_ragdoll.Pelvis.transform.up, Vector3.up);
            if (upness > SimConfig.BicycleMinInvert) return; // not inverted enough

            // Valid bicycle kick. Add a bonus impulse toward goal + upward.
            Vector3 toGoal = SimConfig.GoalCenter - ball.transform.position;
            toGoal.y = 0f;
            Vector3 dir = (toGoal.normalized + Vector3.up * 0.55f).normalized;
            ball.Rb.AddForce(dir * SimConfig.ValidHitBonus, ForceMode.VelocityChange);
            ball.LastShotWasTrick = true;

            _cooldown = 0.5f;
            OnValidTrick?.Invoke();
        }
    }
}
