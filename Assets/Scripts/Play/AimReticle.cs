using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Ground reticle the crosser aims at. Follows the mouse: a ray from the camera
    /// is intersected with the ground plane (y = 0) and clamped to the penalty box.
    /// Pulses so it is easy to see. Purely visual + a queryable TargetPoint.
    /// </summary>
    public class AimReticle : MonoBehaviour
    {
        Camera _cam;
        Transform _ring;
        float _phase;

        public Vector3 TargetPoint { get; private set; }
        public bool Active = true;

        readonly Plane _ground = new Plane(Vector3.up, Vector3.zero);

        public void Init(Camera cam, Material mat)
        {
            _cam = cam;
            TargetPoint = SimConfig.ReticleStart;
            transform.position = TargetPoint;

            // A flat ring made of a thin torus-ish disc: use a flattened cylinder.
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
        }

        void Update()
        {
            if (_cam == null) return;

            if (Active && MouseValid(out Vector3 hit))
            {
                hit = ClampToBox(hit);
                TargetPoint = hit;
                transform.position = new Vector3(hit.x, 0.02f, hit.z);
            }

            _phase += Time.deltaTime * 3f;
            float s = 1f + Mathf.Sin(_phase) * 0.08f;
            transform.localScale = new Vector3(s, 1f, s);
        }

        bool MouseValid(out Vector3 point)
        {
            point = TargetPoint;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            Vector2 mp = mouse.position.ReadValue();
            Ray ray = _cam.ScreenPointToRay(mp);
            if (_ground.Raycast(ray, out float dist))
            {
                point = ray.GetPoint(dist);
                return true;
            }
            return false;
        }

        Vector3 ClampToBox(Vector3 p)
        {
            float halfW = SimConfig.PenaltyBoxWidth * 0.5f - 1f;
            float goalZ = SimConfig.GoalCenter.z;
            float nearZ = goalZ - SimConfig.PenaltyBoxDepth + 1f;
            float farZ = goalZ - 1.2f; // keep it in front of the line
            p.x = Mathf.Clamp(p.x, -halfW, halfW);
            p.z = Mathf.Clamp(p.z, nearZ, farZ);
            p.y = 0f;
            return p;
        }
    }
}
