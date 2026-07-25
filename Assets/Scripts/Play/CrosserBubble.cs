using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// An invisible protective bubble around the AI/planted crosser. The BALL passes through freely
    /// (so deliveries are never affected), and so does the crosser's OWN body; any OTHER player's
    /// ragdoll that enters the radius is pushed back out. This keeps opponents/teammates from bumping
    /// the crosser mid-serve, guaranteeing a clean, perfectly-placed delivery every time.
    ///
    /// No physics layers exist in this project, so - like AnatomySim - it filters overlaps by
    /// component: skip anything under a BallController, skip our OWN ActiveRagdoll, and push out any
    /// other ActiveRagdoll's rigidbodies. Host-side only in multiplayer (physics runs on the host).
    /// </summary>
    public class CrosserBubble : MonoBehaviour
    {
        ActiveRagdoll _self;      // the crosser (never pushed by its own bubble)
        Transform _center;        // crosser pelvis: the bubble centre
        float _radius;

        readonly Collider[] _hits = new Collider[16];

        public void Init(ActiveRagdoll self, float radius = 1.2f)
        {
            _self = self;
            _radius = radius;
            _center = self != null && self.Pelvis != null ? self.Pelvis.transform : transform;
        }

        void FixedUpdate()
        {
            if (_self == null || _center == null) return;
            Vector3 c = _center.position; c.y = Mathf.Max(c.y, 0f);

            int n = Physics.OverlapSphereNonAlloc(c, _radius, _hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i];
                if (col == null) continue;
                if (col.GetComponentInParent<BallController>() != null) continue;   // ball passes freely
                var rag = col.GetComponentInParent<ActiveRagdoll>();
                if (rag == null || rag == _self) continue;                          // only OTHER players

                var rb = col.attachedRigidbody;
                if (rb == null) continue;

                // Push this body part out to the bubble surface, horizontally (don't launch it up).
                Vector3 away = rb.position - c; away.y = 0f;
                float d = away.magnitude;
                Vector3 dir = d > 1e-4f ? away / d : Vector3.forward;
                if (d < _radius)
                {
                    Vector3 surface = c + dir * _radius;
                    rb.position = new Vector3(surface.x, rb.position.y, surface.z);
                    // Kill any inward velocity so they don't immediately push back in.
                    Vector3 v = rb.linearVelocity;
                    float inward = Vector3.Dot(v, -dir);
                    if (inward > 0f) rb.linearVelocity = v + dir * inward;
                }
            }
        }
    }
}
