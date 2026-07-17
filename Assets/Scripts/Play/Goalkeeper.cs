using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Deliberately simple keeper for the first playable: a kinematic capsule that
    /// shuffles laterally along the goal line to shadow the ball's x position, with a
    /// capped speed so it is beatable. No diving yet (that is a later trick-matching
    /// feature). Kinematic so it never gets bowled over and destabilises the test.
    /// </summary>
    public class Goalkeeper : MonoBehaviour
    {
        Rigidbody _rb;
        BallController _ball;
        float _homeZ;
        float _y;

        public void Init(BallController ball)
        {
            _ball = ball;
            _rb = GetComponent<Rigidbody>();
            _homeZ = transform.position.z - 0.4f; // stand just off the line
            _y = transform.position.y;
        }

        void FixedUpdate()
        {
            if (_ball == null || _rb == null) return;
            float halfGoal = SimConfig.GoalWidth * 0.5f - 0.4f;
            float targetX = Mathf.Clamp(_ball.transform.position.x, -halfGoal, halfGoal);

            // Only react when the ball is heading in and near.
            float dz = _ball.transform.position.z - transform.position.z;
            float react = (dz < 10f && dz > -1.5f) ? 1f : 0.25f;

            // Ability scales tracking speed: 0 = barely moves, 1 = very sharp.
            float speed = Mathf.Lerp(0.6f, 6.5f, Mathf.Clamp01(SimConfig.KeeperAbility));
            Vector3 pos = _rb.position;
            float nx = Mathf.MoveTowards(pos.x, targetX, speed * react * Time.fixedDeltaTime);
            _rb.MovePosition(new Vector3(nx, _y, _homeZ));
        }

        public void ResetTo(Vector3 basePos)
        {
            _homeZ = basePos.z - 0.4f;
            _y = basePos.y;
            if (_rb != null)
            {
                _rb.position = new Vector3(0f, _y, _homeZ);
                transform.position = _rb.position;
            }
        }
    }
}
