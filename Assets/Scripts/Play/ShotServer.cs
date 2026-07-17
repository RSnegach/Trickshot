using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Keeper-mode shot feeder: fires the ball ON TARGET at a random spot within the
    /// goal mouth every few seconds, from a random-ish spot out on the pitch. No
    /// crosser involved. GameManager-free; the KeeperGame driver pumps Tick().
    /// </summary>
    public class ShotServer : MonoBehaviour
    {
        BallController _ball;
        float _timer;
        int _seed = 20260716;

        // Where shots are taken from (out in front of the goal), and the target window.
        Vector3 _spot;
        Vector3 _pendingTarget;

        public bool JustFired { get; private set; }

        public void Init(BallController ball)
        {
            _ball = ball;
        }

        public void Arm(float firstDelay)
        {
            _timer = firstDelay;
            JustFired = false;
            _ball.ResetTo(RandomTakePoint());
        }

        /// <summary>Self-looping: fires a ball, then re-arms KeeperServeInterval later -
        /// continuously, regardless of the previous ball's outcome. Returns true on the
        /// frame a ball fires.</summary>
        public bool Tick()
        {
            JustFired = false;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Fire();
                _timer = SimConfig.KeeperServeInterval;   // constant 2s cadence
                return true;
            }
            return false;
        }

        Vector3 RandomTakePoint()
        {
            // Somewhere in front of the goal, spread across the box.
            float x = (Rand() * 2f - 1f) * 8f;
            float z = SimConfig.GoalCenter.z - Mathf.Lerp(9f, 16f, Rand());
            _spot = new Vector3(x, 0.2f, z);
            return _spot;
        }

        void Fire()
        {
            _spot = RandomTakePoint();
            _ball.ResetTo(_spot);

            // On-target: aim at a random point inside the goal mouth (with a margin so
            // it's inside the posts/bar). Difficulty pushes the aim toward the corners.
            float diff = Mathf.Clamp01(SimConfig.ShotDifficulty);
            float halfW = SimConfig.GoalWidth * 0.5f - 0.5f;
            float spread = Mathf.Lerp(0.55f, 1f, diff);   // harder -> uses more of the goal
            float tx = (Rand() * 2f - 1f) * halfW * spread;
            float ty = Mathf.Lerp(0.4f, SimConfig.GoalHeight - 0.4f, Mathf.Lerp(0.35f, Rand(), diff));
            _pendingTarget = new Vector3(tx, ty, SimConfig.GoalCenter.z);

            // Harder = faster (shorter flight time).
            float t = Mathf.Lerp(0.9f, 0.42f, diff);
            _ball.LaunchTo(_pendingTarget, t, Vector3.zero, 0f);
            JustFired = true;
        }

        float Rand()
        {
            _seed = (_seed * 1103515245 + 12345) & 0x7fffffff;
            return _seed / (float)0x7fffffff;
        }
    }
}
