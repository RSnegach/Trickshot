using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Landing telegraph for an airborne ball. The serve/flight-prediction system calls Show() with
    /// the predicted landing point so the player can read where to be; it is a purely visual pulsing
    /// marker on the ground (no mouse aiming any more). Used in every ball mode - match (both SP
    /// and MP), striker, freeplay, time trial - so one shape change here is every mode's reticle.
    ///
    /// SMALL CIRCLE + CROSSHAIR THROUGH IT. The previous shape was a flat 1.4 m ring with a separate
    /// 0.35 m solid dot and no crosshair at all - a "landing zone" disc, not an aim point. This reads
    /// as a scope reticle instead: a small ring, a pinpoint dot at its exact centre, and two bars
    /// that cross through the ring's middle and overshoot its edge on both ends (RingRadius x2.4,
    /// vs the ring's own diameter of RingRadius x2 - so the bars are visibly the reticle's crosshair,
    /// not just decoration sitting inside the circle).
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        Transform _ring;
        float _phase;

        public Vector3 TargetPoint { get; private set; }
        public bool Active { get; private set; }

        // World metres on the turf. RingRadius half of the old ring (was 0.7 m, i.e. SimConfig.
        // ScrimReticleRadius - kept in sync there, see its own comment) so the reticle reads as a
        // point to aim at rather than an area the ball might land anywhere inside.
        const float RingRadius  = 0.35f;
        const float DotRadius   = 0.03f;    // pinpoint at the exact predicted spot
        const float CrossReach  = RingRadius * 1.2f;   // bar HALF-length: overshoots the ring's edge
        const float CrossThick  = 0.035f;

        public void Init(Material mat)
        {
            TargetPoint = SimConfig.ReticleStart;
            transform.position = TargetPoint;

            _ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(_ring.GetComponent<Collider>());
            _ring.name = "ReticleRing";
            _ring.SetParent(transform, false);
            _ring.localScale = new Vector3(RingRadius * 2f, 0.02f, RingRadius * 2f);
            _ring.localPosition = Vector3.zero;
            _ring.GetComponent<Renderer>().sharedMaterial = mat;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "ReticleDot";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = new Vector3(DotRadius * 2f, 0.03f, DotRadius * 2f);
            dot.GetComponent<Renderer>().sharedMaterial = mat;

            // The crosshair: two flat bars through the centre, each longer than the ring's own
            // diameter so they visibly cross its edge (CrossReach*2 = RingRadius*2.4) rather than
            // stopping short inside it.
            MakeCrossBar(mat, new Vector3(CrossReach * 2f, 0.025f, CrossThick));   // east-west
            MakeCrossBar(mat, new Vector3(CrossThick, 0.025f, CrossReach * 2f));   // north-south

            SetVisible(false);
        }

        void MakeCrossBar(Material mat, Vector3 scale)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(bar.GetComponent<Collider>());
            bar.name = "ReticleCrossbar";
            bar.transform.SetParent(transform, false);
            bar.transform.localScale = scale;
            bar.transform.localPosition = new Vector3(0f, 0.001f, 0f);   // a hair above the ring/dot, avoids z-fighting
            bar.GetComponent<Renderer>().sharedMaterial = mat;
        }

        public void Show(Vector3 groundPoint)
        {
            TargetPoint = new Vector3(groundPoint.x, 0f, groundPoint.z);
            transform.position = new Vector3(groundPoint.x, 0.02f, groundPoint.z);
            Active = true;
            SetVisible(true);
        }

        public void Hide()
        {
            Active = false;
            SetVisible(false);
        }

        void SetVisible(bool v)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = v;
        }

        void Update()
        {
            if (!Active) return;
            _phase += Time.deltaTime * 3f;
            float s = 1f + Mathf.Sin(_phase) * 0.1f;
            transform.localScale = new Vector3(s, 1f, s);
        }
    }
}
