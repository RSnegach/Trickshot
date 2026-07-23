using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Soft, dynamic hair: a Verlet strand simulation (same model as the goal net's FlexNet)
    /// rendered as textured HAIR CARDS. Each strand is a chain pinned only at its ROOT on the
    /// scalp; the rest falls under gravity, swings when the head moves, collides off the head
    /// sphere, and (per style) is pulled toward a "styled" rest shape by a stiffness spring. So a
    /// mohawk holds up but wobbles, a ponytail sways, and long hair drapes.
    ///
    /// RENDER: instead of drawing each strand as a line, every strand is a flat QUAD RIBBON (a
    /// strip of quads down the strand, 2 verts per node) UV-mapped to one vertical strip of a
    /// shared grayscale hair atlas. An alpha-cutout shader (Trickshot/HairCard) clips the gaps
    /// between the painted strands, so the wispy strand detail comes from the TEXTURE, not from
    /// thousands of line segments - the standard low-cost "hair cards" approach. The card faces
    /// outward from the head so it reads from outside; its width is def.thickness.
    ///
    /// Purely cosmetic: never gets a collider, never touches the ball. Runs locally on EVERY body
    /// that has it (local player, remote MP puppets, customize preview), so in multiplayer everyone
    /// sees everyone's hair move - style + colour ride the networked PlayerAppearance and each
    /// machine sims the swing itself (no per-node sync).
    ///
    /// The component GameObject is a child of the head bone at identity, so the mesh is authored in
    /// HEAD-LOCAL space (+Y up, +Z front, +X side). Verlet integration runs in WORLD space (gravity
    /// is world-down and must survive the head turning); positions convert back to head-local for
    /// the mesh.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class HairSim : MonoBehaviour
    {
        // How a style's strand roots are scattered over the scalp. Drives the silhouette far more
        // than length does: a Strip reads as a mohawk, a BackCluster as a ponytail/bun, etc.
        public enum RootMode { Crown, SidesBack, BackCluster, Strip, Ring }

        // A hair style as data (no per-primitive authoring). AttachAppearance builds a HairSim from
        // one of these; the catalog in Cosmetics is just a list of these defs.
        public struct HairDef
        {
            public RootMode root;
            public int strands;        // CARD count (each card is a clump; keep low - tens, not hundreds)
            public int nodes;          // nodes per strand (>=2); more = smoother drape, more cost
            public float length;       // total strand length in metres
            public float stiffness;    // 0 = floppy free hang .. 1 = strongly holds the styled shape
            public Vector3 flow;       // styled direction a strand grows toward (local); (0,-1,0)=hang, (0,1,0)=up
            public float curl;         // sideways wobble amplitude down the strand (wavy/curly), metres
            public float jitter;       // per-strand random angular spread so cards don't overlap exactly
            public float thickness;    // CARD WIDTH in metres (a card is a clump, so this is wide)
        }

        // Vertical strips in Resources/Hair/HairAtlas.png (detected from the source art: 4 hair
        // clumps side by side, each a full-height strand strip). U bounds are normalized; a card is
        // UV'd to one of these. The atlas has ROOTS at the BOTTOM (low V) and TIPS at the TOP.
        static readonly Vector2[] AtlasStripsU =
        {
            new Vector2(0.021f, 0.240f),
            new Vector2(0.291f, 0.482f),
            new Vector2(0.536f, 0.724f),
            new Vector2(0.794f, 0.980f),
        };
        const float AtlasVRoot = 0.10f;   // V at the strand root (bottom of the atlas strip)
        const float AtlasVTip  = 0.92f;   // V at the strand tip (top of the atlas strip)

        // Nominal head radius (matches Cosmetics.HeadR) for root placement + collision.
        const float HeadR = 0.19f;

        Transform _head;
        int _perStrand;                 // nodes per strand
        int _strandCount;               // card count

        Vector3[] _restLocal;           // styled rest position of every PHYSICS node, head-local (stiffness target)
        Vector3[] _pos;                 // live physics-node positions, WORLD space
        Vector3[] _prev;                // previous world positions (Verlet)
        bool[] _rootNode;               // true for the pinned root node of each strand
        float _segLen;                  // rest length between consecutive nodes on a strand
        float _cardHalf;                // half the card width (metres)

        Vector3[] _simLocal;            // scratch: physics nodes converted to head-local this tick

        Mesh _mesh;
        Vector3[] _vtx;                 // mesh vertices: 2 (left/right edge) per physics node
        Vector3[] _norm;                // mesh normals: strand TANGENT per vert (Kajiya-Kay shading)
        Vector2[] _uvAtlas;             // static atlas UV per vert (strip U, root->tip V)
        Vector2[] _uvRootTip;           // static uv2.x = root(0)->tip(1) per vert
        int[] _tris;                    // triangle indices (two tris per strand segment)

        float _stiffness;

        // Deterministic per-instance RNG (Random.* is banned in some hosts; tiny LCG seeded from def).
        uint _rng;
        float Rand01() { _rng = _rng * 1664525u + 1013904223u; return (_rng >> 8) * (1f / 16777216f); }
        float RandSym() => Rand01() * 2f - 1f;

        public void Build(Transform head, in HairDef def, Material mat)
        {
            _head = head;
            _strandCount = Mathf.Max(1, def.strands);
            _perStrand = Mathf.Max(2, def.nodes);
            _segLen = def.length / (_perStrand - 1);
            _cardHalf = Mathf.Max(0.001f, def.thickness) * 0.5f;
            _rng = 2166136261u ^ (uint)(def.strands * 73856093) ^ (uint)((int)(def.length * 1000f) * 19349663)
                   ^ (uint)((int)def.root * 83492791);

            // PHYSICS nodes: one Verlet node per (strand, node-along). Cost is strands x nodes.
            int n = _strandCount * _perStrand;
            _restLocal = new Vector3[n];
            _pos = new Vector3[n];
            _prev = new Vector3[n];
            _rootNode = new bool[n];
            _simLocal = new Vector3[n];

            // Mesh: 2 verts (left/right ribbon edge) per physics node.
            int vcount = n * 2;
            _vtx = new Vector3[vcount];
            _norm = new Vector3[vcount];
            _uvAtlas = new Vector2[vcount];
            _uvRootTip = new Vector2[vcount];

            Vector3 flow = def.flow.sqrMagnitude > 1e-4f ? def.flow.normalized : Vector3.down;

            var tris = new System.Collections.Generic.List<int>(_strandCount * (_perStrand - 1) * 6);
            for (int s = 0; s < _strandCount; s++)
            {
                float t = _strandCount > 1 ? s / (float)(_strandCount - 1) : 0.5f;
                Vector3 rootDir = RootDir(def.root, t);              // unit dir from head centre to the scalp root
                Vector3 rootPos = rootDir * (HeadR + 0.01f);         // sit a touch proud of the scalp

                Vector3 growth = Vector3.Slerp(flow, rootDir, 0.25f).normalized;
                Vector3 tilt = new Vector3(RandSym(), RandSym(), RandSym()) * def.jitter;
                growth = (growth + tilt).normalized;

                Vector3 side = Vector3.Cross(growth, Vector3.up);
                if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(growth, Vector3.forward);
                side.Normalize();

                // Assign this card an atlas strip (cycled so all 4 clumps get used across the head).
                Vector2 stripU = AtlasStripsU[s % AtlasStripsU.Length];

                int baseIdx = s * _perStrand;
                for (int k = 0; k < _perStrand; k++)
                {
                    float along = k * _segLen;
                    float u = _perStrand > 1 ? k / (float)(_perStrand - 1) : 0f;   // 0 root .. 1 tip
                    Vector3 wob = side * (Mathf.Sin(u * Mathf.PI * 3f) * def.curl * u);
                    Vector3 local = rootPos + growth * along + wob;
                    int p = baseIdx + k;               // physics node index
                    _restLocal[p] = local;
                    _rootNode[p] = (k == 0);

                    // Two mesh verts per node (left = strip's u0 edge, right = u1 edge). Positions
                    // are filled each tick in WriteVerts; UVs are static and set once here.
                    int vL = p * 2, vR = p * 2 + 1;
                    float vv = Mathf.Lerp(AtlasVRoot, AtlasVTip, u);
                    _uvAtlas[vL] = new Vector2(stripU.x, vv);
                    _uvAtlas[vR] = new Vector2(stripU.y, vv);
                    _uvRootTip[vL] = new Vector2(u, 0f);
                    _uvRootTip[vR] = new Vector2(u, 0f);

                    if (k < _perStrand - 1)
                    {
                        int nL = vL + 2, nR = vR + 2;   // next node's verts
                        // Two triangles for the quad (vL,vR,nL,nR). Cull Off, so winding is moot.
                        tris.Add(vL); tris.Add(nL); tris.Add(vR);
                        tris.Add(vR); tris.Add(nL); tris.Add(nR);
                    }
                }
            }
            _tris = tris.ToArray();

            // Seed physics-node world positions from the rest pose under the head's current transform.
            for (int i = 0; i < n; i++)
            {
                Vector3 w = _head.TransformPoint(_restLocal[i]);
                _pos[i] = w; _prev[i] = w;
            }

            _mesh = new Mesh { name = "HairCardMesh" };
            _mesh.MarkDynamic();
            WriteVerts(_head.worldToLocalMatrix);        // fills vertices + tangent normals
            _mesh.uv = _uvAtlas;                         // static atlas UV (set once)
            _mesh.uv2 = _uvRootTip;                      // static root->tip factor (set once)
            _mesh.triangles = _tris;
            _mesh.RecalculateBounds();

            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            _stiffness = Mathf.Clamp01(def.stiffness);
        }

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
                    // A front-to-back midline strip over the crown (mohawk).
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
            // MultiplyPoint3x4, instead of per-node TransformPoint / InverseTransformPoint native
            // calls. The matrices are constant across this body's nodes this tick, so it's exact.
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

            WriteVerts(w2l);
            _mesh.RecalculateBounds();
        }

        // Build the card ribbon verts + tangent normals from the physics nodes.
        //   1) convert each physics node world->local once (tick-cached matrix, MultiplyPoint3x4);
        //   2) per node compute the STRAND AXIS (tangent) and an OUTWARD direction (away from the
        //      head centre), then place the two ribbon-edge verts offset sideways = axis x outward,
        //      by _cardHalf. So the flat card faces outward from the head (visible from outside) and
        //      its width direction stays perpendicular to the strand as it bends.
        // The axis goes into mesh.normals for both verts: the hair shader lights from this tangent.
        void WriteVerts(Matrix4x4 w2l)
        {
            for (int i = 0; i < _pos.Length; i++)
                _simLocal[i] = w2l.MultiplyPoint3x4(_pos[i]);

            for (int s = 0; s < _strandCount; s++)
            {
                int b0 = s * _perStrand;
                for (int k = 0; k < _perStrand; k++)
                {
                    int p = b0 + k;
                    Vector3 pos = _simLocal[p];
                    // Strand axis (tangent): toward the next node, or from the previous at the tip.
                    Vector3 axis = (k < _perStrand - 1) ? _simLocal[p + 1] - pos : pos - _simLocal[p - 1];
                    if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
                    axis.Normalize();
                    // Outward from the head centre (head-local centre is ~origin at head height; the
                    // component sits at the head, so head-local origin is the head centre).
                    Vector3 outward = pos;
                    if (outward.sqrMagnitude < 1e-6f) outward = Vector3.forward;
                    outward.Normalize();
                    // Card width direction: perpendicular to both the strand and outward, so the flat
                    // of the card faces outward.
                    Vector3 wdir = Vector3.Cross(axis, outward);
                    if (wdir.sqrMagnitude < 1e-6f) wdir = Vector3.Cross(axis, Vector3.up);
                    wdir.Normalize();

                    int vL = p * 2, vR = p * 2 + 1;
                    _vtx[vL] = pos - wdir * _cardHalf;
                    _vtx[vR] = pos + wdir * _cardHalf;
                    _norm[vL] = axis;
                    _norm[vR] = axis;
                }
            }
            _mesh.vertices = _vtx;
            _mesh.normals = _norm;
        }

        // A runtime-generated mesh is not freed by destroying the GameObject, and the customize
        // preview rebuilds the body repeatedly, so free it explicitly (mirrors GeneratedMeshOwner).
        void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
