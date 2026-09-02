using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The human crosser's aim: a translucent line from the ball tracing the first part of the flight
    /// he is about to play (the corner-kick read in recent FIFAs) instead of a marker on the landing
    /// spot. It is the INTENDED path - the launch strays from it by his passing scatter - and how much
    /// of it shows is the caller's call (CrosserControl: a fraction of the flight that grows with his
    /// passing, capped in metres). A plain object owning one world-space LineRenderer, built on first
    /// use; nothing else references the object, so Dispose can simply destroy it.
    /// </summary>
    public class CrossPathLine
    {
        const int Samples = 28;
        LineRenderer _line;
        readonly Vector3[] _pts = new Vector3[Samples];

        static Material s_mat;
        static Material Mat
        {
            get
            {
                if (s_mat != null) return s_mat;
                // Sprites/Default: alpha-blended, unlit, vertex-coloured, and on the Always Included
                // Shaders list - the transparent shader a runtime-built project can count on in a
                // build (see Make.Standard's note on stripping). Falls back to an opaque unlit.
                var sh = Shader.Find("Sprites/Default");
                s_mat = sh != null ? new Material(sh) : Make.Unlit(Color.white);
                s_mat.color = Color.white;
                return s_mat;
            }
        }

        void Build()
        {
            var go = new GameObject("CrossPath");
            _line = go.AddComponent<LineRenderer>();
            _line.material = Mat;
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCapVertices = 4;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            // Thick at the ball and tapering, fading to nothing at the end so the line reads as "the
            // first part of the flight", not a ruler to a spot.
            _line.widthCurve = AnimationCurve.Linear(0f, 0.16f, 1f, 0.07f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.55f, 0.5f),
                              new GradientAlphaKey(0f, 1f) });
            _line.colorGradient = g;
            _line.positionCount = 0;
            _line.enabled = false;
        }

        /// <summary>
        /// Trace p(t) = from + v0 t + accel t^2 / 2 for `seconds` (accel includes gravity for an
        /// airborne ball), stopping early once the path is `maxMetres` long. `flatY`: draw every point
        /// at this height instead - a rolled ball's line lies on the turf, not through its centre.
        /// </summary>
        public void Trace(Vector3 from, Vector3 v0, Vector3 accel, float seconds, float maxMetres, float? flatY = null)
        {
            if (_line == null) Build();
            int n = 0;
            float length = 0f;
            Vector3 prev = from;
            for (int i = 0; i < Samples; i++)
            {
                float t = seconds * i / (Samples - 1);
                Vector3 p = from + v0 * t + accel * (0.5f * t * t);
                if (flatY.HasValue) p.y = flatY.Value;
                if (i > 0)
                {
                    float seg = Vector3.Distance(prev, p);
                    if (length + seg > maxMetres)
                    {
                        // Trim the last segment to the cap and stop.
                        float keep = seg > 1e-4f ? (maxMetres - length) / seg : 0f;
                        _pts[n++] = Vector3.Lerp(prev, p, Mathf.Clamp01(keep));
                        break;
                    }
                    length += seg;
                }
                _pts[n++] = p;
                prev = p;
            }
            _line.positionCount = n;
            for (int i = 0; i < n; i++) _line.SetPosition(i, _pts[i]);
            _line.enabled = true;
        }

        public void Hide()
        {
            if (_line != null) _line.enabled = false;
        }

        public void Dispose()
        {
            if (_line != null) { Object.Destroy(_line.gameObject); _line = null; }
        }
    }
}
