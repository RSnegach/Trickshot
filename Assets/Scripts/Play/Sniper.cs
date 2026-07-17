using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Hidden fourth role: a shooter perched high in the stadium that tries to hit the
    /// striker or the ball. This is a DORMANT SCAFFOLD - it is not spawned by default
    /// (see GameBootstrap.EnableSniper) and does nothing to current gameplay until
    /// enabled. It exists so the sniper can be fleshed out later against real
    /// extension points: perch, target selection, aim lead-in, and a hitscan shot.
    ///
    /// Currently AI-driven and harmless: it picks a target, aims for a beat, and fires
    /// a hitscan ray with a visible tracer. On a hit it raises OnHitStriker/OnHitBall
    /// (no gameplay consequence wired yet). Swap the AI for PlayerInput later for the
    /// online multiplayer role.
    /// </summary>
    public class Sniper : MonoBehaviour
    {
        Transform _striker;      // the striker's pelvis (a moving target)
        Transform _ball;
        LineRenderer _tracer;
        float _tracerTimer;

        float _fireTimer;
        float _aimTimer;
        Transform _target;
        bool _aiming;

        public bool Active = false;                 // master switch; off = fully dormant
        public System.Action OnHitStriker;
        public System.Action OnHitBall;

        public void Init(Transform strikerPelvis, Transform ball)
        {
            _striker = strikerPelvis;
            _ball = ball;
            transform.position = SimConfig.SniperPerch;

            // Muzzle tracer (hidden until a shot).
            _tracer = gameObject.AddComponent<LineRenderer>();
            _tracer.material = Make.Glow(new Color(1f, 0.25f, 0.2f));
            _tracer.startWidth = 0.05f;
            _tracer.endWidth = 0.02f;
            _tracer.positionCount = 2;
            _tracer.enabled = false;

            _fireTimer = SimConfig.SniperFireInterval;
        }

        void Update()
        {
            if (_tracerTimer > 0f)
            {
                _tracerTimer -= Time.deltaTime;
                if (_tracerTimer <= 0f) _tracer.enabled = false;
            }

            if (!Active) return;

            if (!_aiming)
            {
                _fireTimer -= Time.deltaTime;
                if (_fireTimer <= 0f)
                {
                    _target = PickTarget();
                    if (_target != null) { _aiming = true; _aimTimer = SimConfig.SniperAimTime; }
                    else _fireTimer = SimConfig.SniperFireInterval;
                }
            }
            else
            {
                // Aim: point at the (led) target for the lead-in, then fire.
                Vector3 aimPoint = LeadPoint(_target);
                transform.rotation = Quaternion.LookRotation((aimPoint - transform.position).normalized, Vector3.up);
                _aimTimer -= Time.deltaTime;
                if (_aimTimer <= 0f) { Fire(aimPoint); _aiming = false; _fireTimer = SimConfig.SniperFireInterval; }
            }
        }

        Transform PickTarget()
        {
            // Prefer whichever is closer to the goal mouth (most dangerous); trivial
            // placeholder heuristic, easy to replace.
            if (_striker == null) return _ball;
            if (_ball == null) return _striker;
            float ds = Mathf.Abs(_striker.position.z - SimConfig.GoalCenter.z);
            float db = Mathf.Abs(_ball.position.z - SimConfig.GoalCenter.z);
            return db < ds ? _ball : _striker;
        }

        Vector3 LeadPoint(Transform t)
        {
            if (t == null) return SimConfig.GoalCenter;
            var rb = t.GetComponentInParent<Rigidbody>();
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
            return t.position + vel * SimConfig.SniperLead;
        }

        void Fire(Vector3 aimPoint)
        {
            Vector3 origin = transform.position;
            Vector3 dir = (aimPoint - origin).normalized;

            ShowTracer(origin, origin + dir * SimConfig.SniperRange);

            if (Physics.Raycast(origin, dir, out RaycastHit hit, SimConfig.SniperRange,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                ShowTracer(origin, hit.point);
                if (hit.collider.GetComponentInParent<BallController>() != null) OnHitBall?.Invoke();
                else if (hit.collider.GetComponentInParent<ActiveRagdoll>() != null) OnHitStriker?.Invoke();
            }
        }

        void ShowTracer(Vector3 a, Vector3 b)
        {
            _tracer.SetPosition(0, a);
            _tracer.SetPosition(1, b);
            _tracer.enabled = true;
            _tracerTimer = 0.08f;
        }
    }
}
