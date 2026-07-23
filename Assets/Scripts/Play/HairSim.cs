using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Soft, dynamic hair built the same way the goal net is (FlexNet): a Verlet-integrated
    /// mass-spring system drawn as a LINE mesh, not rigid primitives. Where the net is a pinned
    /// grid that springs back to a flat rest, hair is a set of STRANDS each pinned only at its
    /// ROOT on the scalp; the rest of every strand falls under gravity, swings when the head
    /// moves, collides off the head sphere, and (per style) is pulled toward a "styled" rest
    /// shape by a stiffness spring. So a mohawk holds up but wobbles, a ponytail sways, and long
    /// hair drapes and flows.
    ///
    /// Purely cosmetic: like the other head cosmetics it never gets a collider and never touches
    /// the ball. Runs locally on EVERY body that has it (local player, remote MP puppets, and the
    /// customize preview), so in multiplayer every player sees every other player's hair move -
    /// the hair STYLE + COLOUR already ride the networked PlayerAppearance, and each machine sims
    /// the swing itself (no per-node sync needed, exactly like each peer runs its own net).
    ///
    /// Node coordinates: the component GameObject is a child of the head bone at identity, so the
    /// mesh is authored in HEAD-LOCAL space (+Y up, +Z faces the front of the face, +X to the
    /// side), matching the rest of Cosmetics. The Verlet integration itself runs in WORLD space
    /// (gravity is world-down and must survive the head turning), and positions are converted back
    /// to head-local when writing the mesh.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class HairSim : MonoBehaviour
    {
        // How a style's strand roots are scattered over the scalp. Drives the silhouette far more
        // than length does: a Strip reads as a mohawk, a BackCluster as a ponytail/bun, etc.
        public enum RootMode { Crown, SidesBack, BackCluster, Strip, Ring }

        // A hair style as data (no per-primitive authoring). AttachAppearance builds a HairSim from
        // one of these; the catalog in Cosmetics is now just a list of these defs.
        public struct HairDef
        {
            public RootMode root;
            public int strands;        // strand count (cost knob; keep modest, sims on every body)
            public int nodes;          // nodes per strand (>=2); more = smoother drape, more cost
            public float length;       // total strand length in metres
            public float stiffness;    // 0 = floppy free hang .. 1 = strongly holds the styled shape
            public Vector3 flow;       // styled direction a strand grows toward (local); (0,-1,0)=hang, (0,1,0)=up
            public float curl;         // sideways wobble amplitude down the strand (wavy/curly), metres
            public float jitter;       // per-strand random angular spread so strands don't overlap exactly
            public float thickness;    // outward offset of the root off the scalp (visual volume), metres
        }

        // Nominal head radius (matches Cosmetics.HeadR) for root placement + collision.
        const float HeadR = 0.19f;

        Transform _head;
        int _perStrand;                 // nodes per strand
        int _strandCount;

        Vector3[] _restLocal;           // styled rest position of every PHYSICS node, head-local (stiffness target)
        Vector3[] _pos;                 // live physics-node positions, WORLD space
        Vector3[] _prev;                // previous world positions (Verlet)
        bool[] _rootNode;               // true for the pinned root node of each strand
        float _segLen;                  // rest length between consecutive nodes on a strand
        int[] _lineIdx;                 // line-mesh indices (segments along each sub-line)

        int _lines;                     // sub-lines drawn per physics strand (visual thickness ring)
        float _bundleR;                 // ring radius of the sub-line bundle (metres)
        Vector3[] _simLocal;            // scratch: physics nodes converted to head-local this tick

        Mesh _mesh;
        Vector3[] _localScratch;        // reused mesh-vertex buffer (_lines per physics node)

        // Deterministic per-instance RNG seed so a rebuilt preview looks identical run to run
        // (Random.* is banned in some hosts; use a tiny LCG seeded from the def instead).
        uint _rng;
        float Rand01() { _rng = _rng * 1664525u + 1013904223u; return (_rng >> 8) * (1f / 16777216f); }
        float RandSym() => Rand01() * 2f - 1f;

        public void Build(Transform head, in HairDef def, Material mat)
        {
            _head = head;
            _strandCount = Mathf.Max(1, def.strands);
            _perStrand = Mathf.Max(2, def.nodes);
            _segLen = def.length / (_perStrand - 1);
            _rng = 2166136261u ^ (uint)(def.strands * 73856093) ^ (uint)((int)(def.length * 1000f) * 19349663)
                   ^ (uint)((int)def.root * 83492791);

            // PHYSICS nodes: one Verlet node per (strand, node-along). These are what the sim
            // integrates; their count (strands x nodes) is the whole cost, so keep it modest.
            int n = _strandCount * _perStrand;
            _restLocal = new Vector3[n];
            _pos = new Vector3[n];
            _prev = new Vector3[n];
            _rootNode = new bool[n];

            // RENDER thickness: each physics strand is drawn as _lines parallel sub-lines fanned in
            // a ring around the strand axis, so a strand reads as a lock/rope with visible width
            // rather than a 1px wire. Only the physics nodes are simulated + matrix-transformed; the
            // ring copies are cheap offsets built in WriteLocalVerts, so thickness barely adds cost.
            _lines = Mathf.Max(1, SimConfig.HairStrandLines);
            _bundleR = def.thickness;
            _simLocal = new Vector3[n];                 // scratch: physics nodes in head-local space
            _localScratch = new Vector3[n * _lines];    // mesh verts: _lines per physics node

            Vector3 flow = def.flow.sqrMagnitude > 1e-4f ? def.flow.normalized : Vector3.down;

            // Line segments connect consecutive nodes WITHIN each sub-line of each strand.
            var idx = new System.Collections.Generic.List<int>(n * _lines * 2);
            for (int s = 0; s < _strandCount; s++)
            {
                float t = _strandCount > 1 ? s / (float)(_strandCount - 1) : 0.5f;
                Vector3 rootDir = RootDir(def.root, t);              // unit dir from head centre to the scalp root
                Vector3 rootPos = rootDir * (HeadR + def.thickness); // sit a touch proud of the scalp

                // Per-strand growth direction: the styled flow, blended a little toward "straight out
                // of the scalp" near stiff styles, plus a jittered tilt so strands fan out.
                Vector3 growth = Vector3.Slerp(flow, rootDir, 0.25f).normalized;
                Vector3 tilt = new Vector3(RandSym(), RandSym(), RandSym()) * def.jitter;
                growth = (growth + tilt).normalized;

                // A sideways axis for the curl wobble (perpendicular to growth).
                Vector3 side = Vector3.Cross(growth, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(growth, Vector3.forward);
                side.Normalize();

                int baseIdx = s * _perStrand;
                for (int k = 0; k < _perStrand; k++)
                {
                    float along = k * _segLen;
                    float u = _perStrand > 1 ? k / (float)(_perStrand - 1) : 0f;
                    // Curl grows toward the tip so roots stay tidy and ends wave.
                    Vector3 wob = side * (Mathf.Sin(u * Mathf.PI * 3f) * def.curl * u);
                    Vector3 local = rootPos + growth * along + wob;
                    int p = baseIdx + k;               // physics node index
                    _restLocal[p] = local;
                    _rootNode[p] = (k == 0);
                    if (k < _perStrand - 1)
                    {
                        int pNext = p + 1;
                        for (int L = 0; L < _lines; L++)
                        {
                            idx.Add(p * _lines + L);
                            idx.Add(pNext * _lines + L);
                        }
                    }
                }
            }
            _lineIdx = idx.ToArray();

            // Seed physics-node world positions from the rest pose under the head's current transform.
            for (int i = 0; i < n; i++)
            {
                Vector3 w = _head.TransformPoint(_restLocal[i]);
                _pos[i] = w; _prev[i] = w;
            }

            _mesh = new Mesh { name = "HairMesh" };
            _mesh.MarkDynamic();
            WriteLocalVerts(_head.worldToLocalMatrix);
            _mesh.SetIndices(_lineIdx, MeshTopology.Lines, 0);
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            _stiffness = Mathf.Clamp01(def.stiffness);
        }

        float _stiffness;

        // Unit direction from head centre to a strand's scalp root, for parameter t in [0,1].
        Vector3 RootDir(RootMode mode, float t)
        {
            // phi = angle down from +Y (0 = top of head); theta = azimuth, 0 = +Z (face).
            float phi, theta;
            switch (mode)
            {
                case RootMode.SidesBack:
                    // Cover the sides + back, leave the face clear (curtains, long, flowing).
                    phi = Mathf.Lerp(0.45f, 1.35f, Rand01());
                    theta = Mathf.Lerp(Mathf.PI * 0.35f, Mathf.PI * 1.65f, t);
                    break;
                case RootMode.BackCluster:
                    // Tight patch at the back of the crown (ponytail / bun / braid gather point).
                    phi = Mathf.Lerp(0.7f, 1.15f, Rand01());
                    theta = Mathf.PI + RandSym() * 0.5f;
                    break;
                case RootMode.Strip:
                    // A front-to-back midline strip over the crown (mohawk / faux hawk).
                    float a = Mathf.Lerp(-1.15f, 1.15f, t);   // arc angle over the top in the Z plane
                    return new Vector3(RandSym() * 0.05f, Mathf.Cos(a), Mathf.Sin(a)).normalized;
                case RootMode.Ring:
                    // A band low around the whole head (short caps / fringes).
                    phi = Mathf.Lerp(1.15f, 1.5f, Rand01());
                    theta = t * Mathf.PI * 2f;
                    break;
                default: // Crown: whole upper hemisphere, all around (general medium hair).
                    phi = Mathf.Lerp(0.05f, 1.15f, Rand01());
                    theta = t * Mathf.PI * 2f + RandSym() * 0.2f;
                    break;
            }
            float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
            return new Vector3(sp * Mathf.Sin(theta), cp, sp * Mathf.Cos(theta));
        }

        void FixedUpdate()
        {
            if (_head == null || _pos == null) return;
            float dt = Time.fixedDeltaTime;
            float g = SimConfig.HairGravity;
            float damp = SimConfig.HairDamping;
            float stiffPull = Mathf.Clamp01(_stiffness * SimConfig.HairStiffnessK * dt);
            float gStep = g * dt * dt;

            // Cache the head's transform matrices ONCE per tick and transform points inline with
            // MultiplyPoint3x4, instead of calling Transform.TransformPoint / InverseTransformPoint
            // per node. Those are native-interop calls and, at ~700 strands x up to 5 nodes across
            // ~10 bodies, they were the whole cost of the sim. The matrices are constant across all
            // this body's nodes this tick, so this is exact, not an approximation.
            Matrix4x4 l2w = _head.localToWorldMatrix;
            Matrix4x4 w2l = _head.worldToLocalMatrix;

            // 1) Pin roots to the scalp (they ride the head), Verlet-integrate the rest under
            //    gravity, then spring each free node toward its styled rest so shaped styles hold.
            for (int i = 0; i < _pos.Length; i++)
            {
                if (_rootNode[i])
                {
                    Vector3 w = l2w.MultiplyPoint3x4(_restLocal[i]);
                    _pos[i] = w; _prev[i] = w;
                    continue;
                }
                Vector3 vel = (_pos[i] - _prev[i]) * damp;
                _prev[i] = _pos[i];
                _pos[i] += vel;
                _pos[i] += new Vector3(0f, gStep, 0f);            // world gravity
                if (stiffPull > 0f)
                    _pos[i] += (l2w.MultiplyPoint3x4(_restLocal[i]) - _pos[i]) * stiffPull;
            }

            // 2) Solve segment length constraints along each strand (spreads motion, keeps length).
            int iters = SimConfig.HairConstraintIters;
            for (int it = 0; it < iters; it++)
            {
                for (int s = 0; s < _strandCount; s++)
                {
                    int b0 = s * _perStrand;
                    for (int k = 0; k < _perStrand - 1; k++)
                    {
                        int a = b0 + k, b = a + 1;
                        Vector3 delta = _pos[b] - _pos[a];
                        float d = delta.magnitude;
                        if (d < 1e-6f) continue;
                        float diff = (d - _segLen) / d;
                        if (_rootNode[a]) { _pos[b] -= delta * diff; }               // root fixed: move only b
                        else { _pos[a] += delta * (0.5f * diff); _pos[b] -= delta * (0.5f * diff); }
                    }
                }
            }

            // 3) Head-sphere collision: push any node that sank into the skull back to the surface.
            Vector3 centre = _head.position;
            float rad = HeadR + SimConfig.HairHeadPad;
            for (int i = 0; i < _pos.Length; i++)
            {
                if (_rootNode[i]) continue;
                Vector3 d = _pos[i] - centre;
                float m = d.magnitude;
                if (m < rad && m > 1e-4f) _pos[i] = centre + d * (rad / m);
            }

            WriteLocalVerts(w2l);
            _mesh.RecalculateBounds();
        }

        // Build the mesh verts from the physics nodes. Two passes:
        //   1) convert each physics node world->local once (tick-cached matrix, MultiplyPoint3x4);
        //   2) fan each node into a ring of _lines verts, offset perpendicular to the strand axis
        //      by _bundleR, so a strand renders as a lock/rope with width instead of a 1px wire.
        // Pass 2 is pure vector math (no native calls), so thickness is nearly free.
        void WriteLocalVerts(Matrix4x4 w2l)
        {
            for (int i = 0; i < _pos.Length; i++)
                _simLocal[i] = w2l.MultiplyPoint3x4(_pos[i]);

            if (_lines <= 1 || _bundleR <= 1e-5f)
            {
                // Single-wire fast path: mesh vert == physics node.
                for (int i = 0; i < _simLocal.Length; i++) _localScratch[i] = _simLocal[i];
                _mesh.vertices = _localScratch;
                return;
            }

            for (int s = 0; s < _strandCount; s++)
            {
                int b0 = s * _perStrand;
                for (int k = 0; k < _perStrand; k++)
                {
                    int p = b0 + k;
                    Vector3 pos = _simLocal[p];
                    // Strand axis at this node (toward the next node, or from the previous at the tip).
                    Vector3 axis = (k < _perStrand - 1) ? _simLocal[p + 1] - pos : pos - _simLocal[p - 1];
                    if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
                    axis.Normalize();
                    // Two perpendiculars to fan the ring around the axis (so the bundle has width
                    // from every view angle, never collapsing edge-on).
                    Vector3 perp0 = Vector3.Cross(axis, Vector3.up);
                    if (perp0.sqrMagnitude < 1e-6f) perp0 = Vector3.Cross(axis, Vector3.forward);
                    perp0.Normalize();
                    Vector3 perp1 = Vector3.Cross(axis, perp0);
                    int outBase = p * _lines;
                    for (int L = 0; L < _lines; L++)
                    {
                        float ang = (Mathf.PI * 2f) * L / _lines;
                        Vector3 off = (perp0 * Mathf.Cos(ang) + perp1 * Mathf.Sin(ang)) * _bundleR;
                        _localScratch[outBase + L] = pos + off;
                    }
                }
            }
            _mesh.vertices = _localScratch;
        }

        // A runtime-generated mesh is not freed by destroying the GameObject, and the customize
        // preview rebuilds the body repeatedly, so free it explicitly (mirrors GeneratedMeshOwner).
        void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
