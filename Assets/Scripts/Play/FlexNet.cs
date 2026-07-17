using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A see-through, flexible goal net wrapping the back, both sides and the top of
    /// the goal box. Rendered as a LINE GRID (net strings with gaps), not a filled
    /// plane, so it is genuinely see-through and, with an unlit material, never shades
    /// to black regardless of the light angle.
    ///
    /// Each interior node is a damped spring to its rest position; when the ball is
    /// near a node it is pushed radially away, so the net bulges where the ball hits
    /// from any direction and then settles. A separate backstop collider (built in
    /// Arena) is what actually stops the ball - the mesh is purely visual.
    ///
    /// Node coordinates are goal-local: origin at GoalCenter, +X = width, +Y = height,
    /// +Z = depth into the goal (0 at the line, gd at the back).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class FlexNet : MonoBehaviour
    {
        Vector3[] _rest;
        Vector3[] _pos;
        Vector3[] _prev;        // previous positions (Verlet integration)
        bool[] _pinned;
        int[] _lineIdx;
        Mesh _mesh;
        Transform _ball;
        float _ballRadius;

        // Distance constraints (a, b, restLength) between linked grid nodes. Solving
        // these over several iterations is what makes the whole sheet move as fabric.
        struct Link { public int a, b; public float len; }
        Link[] _links;

        MeshRenderer _renderer;
        Camera _cam;
        GameCamera _gameCam;

        readonly List<Vector3> _nodes = new List<Vector3>();
        readonly List<bool> _pins = new List<bool>();
        readonly List<int> _lines = new List<int>();

        public void SetBall(Transform ball, float ballRadius)
        {
            _ball = ball;
            _ballRadius = ballRadius;
        }

        public void Build(float width, float height, float depth, int cols, int rows, Material mat)
        {
            float hw = width * 0.5f;

            // Back panel (z = depth): grid over width x height.
            AddPanel(cols, rows, (u, v) => new Vector3(Mathf.Lerp(-hw, hw, u), Mathf.Lerp(0f, height, v), depth));
            // Left panel (x = -hw): grid over depth x height.
            AddPanel(Mathf.Max(3, Mathf.RoundToInt(cols * depth / width)), rows,
                     (u, v) => new Vector3(-hw, Mathf.Lerp(0f, height, v), Mathf.Lerp(0f, depth, u)));
            // Right panel (x = +hw).
            AddPanel(Mathf.Max(3, Mathf.RoundToInt(cols * depth / width)), rows,
                     (u, v) => new Vector3(hw, Mathf.Lerp(0f, height, v), Mathf.Lerp(0f, depth, u)));
            // Top panel (y = height): grid over width x depth.
            AddPanel(cols, Mathf.Max(3, Mathf.RoundToInt(rows * depth / height)),
                     (u, v) => new Vector3(Mathf.Lerp(-hw, hw, u), height, Mathf.Lerp(0f, depth, v)));

            _rest = _nodes.ToArray();
            _pos = (Vector3[])_rest.Clone();
            _prev = (Vector3[])_rest.Clone();
            _pinned = _pins.ToArray();
            _lineIdx = _lines.ToArray();
            BuildLinks();

            _mesh = new Mesh { name = "NetMesh" };
            _mesh.MarkDynamic();
            _mesh.vertices = _pos;
            _mesh.SetIndices(_lineIdx, MeshTopology.Lines, 0);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            _renderer = GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = mat;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        // Append one grid panel; boundary nodes are pinned, interior nodes flex.
        void AddPanel(int nu, int nv, System.Func<float, float, Vector3> map)
        {
            nu = Mathf.Max(2, nu);
            nv = Mathf.Max(2, nv);
            int start = _nodes.Count;
            for (int v = 0; v < nv; v++)
            for (int u = 0; u < nu; u++)
            {
                _nodes.Add(map(u / (float)(nu - 1), v / (float)(nv - 1)));
                bool edge = (u == 0 || u == nu - 1 || v == 0 || v == nv - 1);
                _pins.Add(edge);
            }
            // Line segments: horizontal and vertical between adjacent nodes.
            for (int v = 0; v < nv; v++)
            for (int u = 0; u < nu; u++)
            {
                int i = start + v * nu + u;
                if (u < nu - 1) { _lines.Add(i); _lines.Add(i + 1); }
                if (v < nv - 1) { _lines.Add(i); _lines.Add(i + nu); }
            }
        }

        // Distance constraints from the grid edges. Iterating these each frame is what
        // makes the net move as one fabric: a local push is propagated across the whole
        // sheet, producing a wide smooth pocket instead of a few dented nodes.
        void BuildLinks()
        {
            var links = new List<Link>();
            for (int e = 0; e < _lineIdx.Length; e += 2)
            {
                int a = _lineIdx[e], b = _lineIdx[e + 1];
                links.Add(new Link { a = a, b = b, len = Vector3.Distance(_rest[a], _rest[b]) });
            }
            _links = links.ToArray();
        }

        void FixedUpdate()
        {
            if (_rest == null) return;
            float dt = Time.fixedDeltaTime;

            bool hasBall = _ball != null;
            Vector3 ballLocal = hasBall ? transform.InverseTransformPoint(_ball.position) : Vector3.zero;
            // Wide push field so the pocket forms even though the backstop keeps the
            // ball ~a radius short of the net plane.
            float reach = _ballRadius + SimConfig.NetBallReach;

            // 1) Verlet integrate + drift back toward rest.
            for (int i = 0; i < _pos.Length; i++)
            {
                if (_pinned[i]) { _pos[i] = _rest[i]; _prev[i] = _rest[i]; continue; }
                Vector3 vel = (_pos[i] - _prev[i]) * SimConfig.NetDamping;
                _prev[i] = _pos[i];
                _pos[i] += vel;
                // gentle spring back to rest so the pocket relaxes when the ball leaves
                _pos[i] += (_rest[i] - _pos[i]) * (SimConfig.NetReturn * dt);
            }

            // 2) Ball push: nodes within the reach field get shoved out to the field
            // surface, so the ball dents a wide pocket (not just nodes it literally
            // overlaps). The constraint pass then spreads that across the sheet.
            if (hasBall)
            {
                for (int i = 0; i < _pos.Length; i++)
                {
                    if (_pinned[i]) continue;
                    Vector3 d = _pos[i] - ballLocal;
                    float dist = d.magnitude;
                    if (dist < reach && dist > 1e-4f)
                        _pos[i] = ballLocal + (d / dist) * reach;
                }
            }

            // 3) Solve distance constraints several times -> spreads the deformation.
            for (int iter = 0; iter < SimConfig.NetConstraintIters; iter++)
            {
                for (int k = 0; k < _links.Length; k++)
                {
                    int a = _links[k].a, b = _links[k].b;
                    Vector3 delta = _pos[b] - _pos[a];
                    float d = delta.magnitude;
                    if (d < 1e-5f) continue;
                    float diff = (d - _links[k].len) / d;
                    bool pa = _pinned[a], pb = _pinned[b];
                    if (pa && pb) continue;
                    if (pa) { _pos[b] -= delta * diff; }
                    else if (pb) { _pos[a] += delta * diff; }
                    else { _pos[a] += delta * (0.5f * diff); _pos[b] -= delta * (0.5f * diff); }
                }
            }

            // 4) Clamp max stretch from rest.
            for (int i = 0; i < _pos.Length; i++)
            {
                if (_pinned[i]) continue;
                Vector3 off = _pos[i] - _rest[i];
                if (off.magnitude > SimConfig.NetMaxStretch)
                    _pos[i] = _rest[i] + off.normalized * SimConfig.NetMaxStretch;
            }

            _mesh.vertices = _pos;
            _mesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            // Hide the net only when the keeper camera is angled into the LOWEST 25% of
            // its look range (mouse pushed down / low angle), where the mesh clutters the
            // view; render it the other 75% of the time.
            if (_renderer == null) return;
            if (_gameCam == null)
            {
                if (_cam == null) _cam = Camera.main;
                if (_cam != null) _gameCam = _cam.GetComponent<GameCamera>();
            }
            bool show = _gameCam == null || _gameCam.KeeperLookDownFraction < 0.6f;
            if (_renderer.enabled != show) _renderer.enabled = show;
        }
    }
}
