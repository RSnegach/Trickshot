using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Landing telegraph for the auto-served cross. The serve system calls Show() with
    /// the predicted landing point so the player can read where to be; it is a purely
    /// visual pulsing ring on the ground (no mouse aiming any more).
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        Transform _ring;
        float _phase;

        public Vector3 TargetPoint { get; private set; }
        public bool Active { get; private set; }

        public void Init(Material mat)
        {
            TargetPoint = SimConfig.ReticleStart;
            transform.position = TargetPoint;

            _ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            Destroy(_ring.GetComponent<Collider>());
            _ring.name = "ReticleRing";
            _ring.SetParent(transform, false);
            _ring.localScale = new Vector3(1.4f, 0.02f, 1.4f);
            _ring.localPosition = Vector3.zero;
            _ring.GetComponent<Renderer>().sharedMaterial = mat;

            var inner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(inner.GetComponent<Collider>());
            inner.name = "ReticleDot";
            inner.transform.SetParent(transform, false);
            inner.transform.localScale = new Vector3(0.35f, 0.03f, 0.35f);
            inner.GetComponent<Renderer>().sharedMaterial = mat;

            SetVisible(false);
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
