using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A single pop-up accuracy target: a bright emissive disc that sits in the plane of
    /// the goal mouth (z = SimConfig.GoalCenter.z) with a glowing rim halo behind it. The
    /// ball scores by passing through a trigger collider sized to the disc.
    ///
    /// Colour encodes value (white 1, yellow 2, red 3); smaller discs are placed toward
    /// the corners and are worth more. The visual container gently pulses and bobs so the
    /// target reads as "popped up", and pops larger for a beat on a hit before hiding.
    ///
    /// The AccuracyGame driver owns a fixed pool of these, wires OnHit, and re-spawns a
    /// fresh one at a new random spot a short delay after each hit.
    /// </summary>
    public class AccuracyTarget : MonoBehaviour
    {
        public int Points;
        public bool Hit;
        public bool Shown => _shown;
        public float Radius => _radius;
        public Vector3 Center => transform.position;

        /// <summary>Fired the frame the ball passes through this target. Argument is self.</summary>
        public System.Action<AccuracyTarget> OnHit;

        const float PulseRate = 4.5f;
        const float PulseAmount = 0.08f;
        const float BobAmount = 0.05f;
        const float FlashTime = 0.22f;
        const float FlashPop = 0.9f;    // extra scale added at the peak of the hit flash
        const float DiscThickness = 0.09f;
        const float HaloScale = 1.22f;  // halo radius relative to the face disc

        BoxCollider _col;
        Transform _visual;      // holds the discs; pulses/bobs without moving the trigger
        Material _faceMat;
        Material _haloMat;
        bool _built;

        bool _shown;
        float _radius = 0.5f;
        Vector3 _baseScale = Vector3.one;
        float _phase;
        float _flash;

        /// <summary>Place this target at pos (in the goal-mouth plane), size it, colour it
        /// for its value, and show it. Rebuilds nothing after the first call.</summary>
        public void Spawn(Vector3 pos, float radius, Color color, int points)
        {
            if (!_built) Build();

            Points = points;
            Hit = false;
            _radius = Mathf.Max(0.05f, radius);
            _baseScale = Vector3.one * _radius;
            _phase = Random.value * Mathf.PI * 2f;   // desync the pulse between targets
            _flash = 0f;

            transform.position = pos;

            // Colour by value; the halo is a whiter, brighter rim.
            Color halo = Color.Lerp(color, Color.white, 0.55f);
            _faceMat.color = color;
            _faceMat.SetColor("_EmissionColor", color * 1.8f);
            _haloMat.color = halo;
            _haloMat.SetColor("_EmissionColor", halo * 2.2f);

            // Trigger is a flat slab in the goal plane, deep enough along Z that a fast
            // ball can't tunnel through it in one physics step.
            _col.size = new Vector3(_radius * 2f, _radius * 2f, Mathf.Max(_radius * 2f, 1.2f));

            _visual.localScale = _baseScale;
            _visual.localPosition = Vector3.zero;
            Show();
        }

        public void Show()
        {
            _shown = true;
            _col.enabled = true;
            SetRenderers(true);
        }

        public void Hide()
        {
            _shown = false;
            _col.enabled = false;
            SetRenderers(false);
        }

        void Build()
        {
            _built = true;

            _col = gameObject.AddComponent<BoxCollider>();
            _col.isTrigger = true;

            _visual = Make.Empty("Visual", transform.position, transform).transform;

            // Discs are built at unit radius (diameter 2) and sized by the visual
            // container scale, so a re-spawn only changes the container scale.
            _faceMat = Make.Glow(Color.white);
            _haloMat = Make.Glow(Color.white);

            BuildDisc("Halo", HaloScale, DiscThickness * 0.7f, _haloMat, 0.03f);   // rim, slightly behind
            BuildDisc("Face", 1f, DiscThickness, _faceMat, 0f);
        }

        // A thin disc facing the pitch (its face lies in the parent XY plane, i.e. the goal
        // mouth). Built colliderless so it never blocks or bounces the ball.
        void BuildDisc(string discName, float radius, float thickness, Material mat, float zBehind)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var oldCol = go.GetComponent<Collider>();
            if (oldCol != null) Destroy(oldCol);
            go.name = discName;
            go.transform.SetParent(_visual, false);
            // Lay the cylinder's long axis along Z so its round face sits in the XY plane.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
            go.transform.localPosition = new Vector3(0f, 0f, zBehind);
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        void SetRenderers(bool v)
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) rs[i].enabled = v;
        }

        void Update()
        {
            if (!_shown) return;

            if (_flash > 0f)
            {
                // Quick pop-and-vanish on a hit.
                _flash -= Time.deltaTime;
                float k = Mathf.Clamp01(_flash / FlashTime);   // 1 -> 0 over the flash
                float pop = 1f + (1f - k) * FlashPop;
                _visual.localScale = _baseScale * pop;
                _visual.localPosition = Vector3.zero;
                if (_flash <= 0f) Hide();
                return;
            }

            _phase += Time.deltaTime * PulseRate;
            float s = 1f + Mathf.Sin(_phase) * PulseAmount;
            _visual.localScale = _baseScale * s;
            _visual.localPosition = new Vector3(0f, Mathf.Sin(_phase * 0.5f) * BobAmount, 0f);
        }

        void OnTriggerEnter(Collider other)
        {
            if (Hit || !_shown) return;
            if (other.GetComponentInParent<BallController>() == null) return;

            Hit = true;
            _col.enabled = false;        // no double count while the flash plays
            _flash = FlashTime;          // Update pops it, then hides
            OnHit?.Invoke(this);
        }
    }
}
