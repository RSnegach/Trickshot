using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Goal-line trigger. A thin trigger volume sitting in the goal mouth. When the
    /// ball's centre passes through, it raises OnGoal once until re-armed by reset.
    /// </summary>
    public class Goal : MonoBehaviour
    {
        public System.Action<bool> OnGoal; // bool: was it a trick goal
        bool _armed = true;

        void OnTriggerEnter(Collider other)
        {
            if (!_armed) return;
            var ball = other.GetComponentInParent<BallController>();
            if (ball == null) return;
            _armed = false;
            OnGoal?.Invoke(ball.LastShotWasTrick);
        }

        public void Arm() => _armed = true;
    }
}
