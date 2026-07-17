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
        Vector3[] _vel;
        bool[] _pinned;
        int[] _lineIdx;
        int[][] _neighbors;     // structural-spring links (adjacent grid nodes)
        Mesh _mesh;
        Transform _ball;
        float _ballRadius;

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
            _vel = new Vector3[_rest.Length];
            _pinned = _pins.ToArray();
            _lineIdx = _lines.ToArray();
            BuildNeighbors();

            _mesh = new Mesh { name = "NetMesh" };
            _mesh.MarkDynamic();
            _mesh.vertices = _pos;
            _mesh.SetIndices(_lineIdx, MeshTopology.Lines, 0);
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
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

        // Adjacency from the grid edges: each node's directly-linked neighbors. These
        // are the structural springs that make the net behave as one connected fabric
        // so a hit spreads into a wide pocket instead of denting only local nodes.
        void BuildNeighbors()
        {
            var lists = new List<int>[_rest.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<int>();
            for (int e = 0; e < _lineIdx.Length; e += 2)
            {
                int a = _lineIdx[e], b = _lineIdx[e + 1];
                lists[a].Add(b);
                lists[b].Add(a);
            }
            _neighbors = new int[_rest.Length][];
            for (int i = 0; i < lists.Length; i++) _neighbors[i] = lists[i].ToArray();
        }

        void FixedUpdate()
        {
            if (_rest == null) return;
            float dt = Time.fixedDeltaTime;

            bool hasBall = _ball != null;
            Vector3 ballLocal = hasBall ? transform.InverseTransformPoint(_ball.position) : Vector3.zero;
            float pushR = _ballRadius + 0.45f;

            for (int i = 0; i < _pos.Length; i++)
            {
                if (_pinned[i]) { _pos[i] = _rest[i]; _vel[i] = Vector3.zero; continue; }

                // Weak pull back to rest so the net eventually settles.
                Vector3 accel = (_rest[i] - _pos[i]) * SimConfig.NetStiffness - _vel[i] * SimConfig.NetDamping;

                // Structural springs: pull toward where the neighbors have moved
                // (their displacement from rest), which propagates the pocket outward
                // so the whole surrounding net billows, not just the impact point.
                var nb = _neighbors[i];
                if (nb.Length > 0)
                {
                    Vector3 neighborDisp = Vector3.zero;
                    for (int k = 0; k < nb.Length; k++) neighborDisp += (_pos[nb[k]] - _rest[nb[k]]);
                    neighborDisp /= nb.Length;
                    Vector3 myDisp = _pos[i] - _rest[i];
                    accel += (neighborDisp - myDisp) * SimConfig.NetLinkStiffness;
                }

                if (hasBall)
                {
                    Vector3 away = _pos[i] - ballLocal;
                    float dist = away.magnitude;
                    if (dist < pushR && dist > 0.0001f)
                    {
                        float strength = (1f - dist / pushR) * SimConfig.NetBallPush;
                        accel += (away / dist) * strength * SimConfig.NetStiffness;
                    }
                }

                _vel[i] += accel * dt;
                _pos[i] += _vel[i] * dt;

                Vector3 off = _pos[i] - _rest[i];
                if (off.magnitude > SimConfig.NetMaxStretch)
                {
                    _pos[i] = _rest[i] + off.normalized * SimConfig.NetMaxStretch;
                    _vel[i] *= 0.5f;
                }
            }

            _mesh.vertices = _pos;
            _mesh.RecalculateBounds();
        }
    }
}
