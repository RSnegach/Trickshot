using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Adult-mode cosmetic appendage: a small collider-less Verlet pendulum hanging from the
    /// bottom-centre of the pelvis. A pill-shaped "member" spans the chain and swings under gravity
    /// + body motion; two spheres ("berries") sit at the attachment. Modeled on HairSim (root pin,
    /// Verlet integrate, segment-length constraints), but with only a few nodes and rigid primitive
    /// pieces re-posed from the nodes each tick instead of a card mesh.
    ///
    /// Purely visual: its own pieces have NO colliders (never a hitbox on the ball). It DOES push
    /// out of player bodies (own + others') by testing the free nodes against nearby ragdoll
    /// colliders, but explicitly ignores the ball, so the ball's motion is never affected.
    /// Attached + sized by Cosmetics.AttachAppearance when PlayerAppearance.Adult is true.
    /// </summary>
    public class AnatomySim : MonoBehaviour
    {
        Transform _pelvis;
        float _scale;                 // girth scale (matches the body)

        // Chain: node 0 = pinned root at the pelvis attach point; 1..N = free, hanging.
        // Four nodes (root + three free) give a longer, smoother pill so the member reads as an
        // elongated limb rather than a stub.
        const int Nodes = 4;
        readonly Vector3[] _pos = new Vector3[Nodes];    // world
        readonly Vector3[] _prev = new Vector3[Nodes];
        Vector3 _rootLocal;           // member attach: front underside of the pelvis (pelvis-local)
        Vector3 _berryLocal;          // berry seat: tucked up under the torso, centred
        float _segLen;                // rest length between nodes

        Transform _member;            // the pill (a stretched sphere spanning root -> tip)
        Transform _berryL, _berryR;   // the two spheres at the attachment
        float _memberRadius, _berryRadius;

        // Reused buffer for body-collision queries (avoids per-tick allocation).
        readonly Collider[] _hits = new Collider[8];

        // base dimensions in metres (scaled by girth). Small + tasteful-ish.
        // Two separate anchors so the pieces don't overlap:
        //  - member roots on the FRONT UNDERSIDE of the pelvis (where the berries used to sit) and
        //    hangs down as a long, thin, arm-proportioned pendulum,
        //  - berries seat UP UNDER THE TORSO, centred (above + behind the member root).
        const float MemberDrop = 0.12f;   // member root: below pelvis centre, front underside
        const float MemberFwd  = 0.09f;   // member root: forward of centre (front)
        const float BerryRise  = 0.10f;   // berries: ABOVE pelvis centre, up under the torso bottom
        const float BerryFwd   = 0.03f;   // berries: near-centred, just off the front
        // Long + slender to mirror a forearm (~0.60 tall at 0.09 dia ≈ 6.7:1). Three segments at
        // 0.10 span a ~0.30 chain; the pill ends up ~0.36 long at 0.056 dia ≈ 6.4:1.
        const float SegLen   = 0.10f;     // per-segment rest length
        const float MemberR  = 0.028f;    // pill radius (thin, arm-like)
        const float BerryR   = 0.032f;    // berry radius
        const float BerryGap = 0.03f;     // half the spacing between the two berries

        // lenMul/girthMul/ballMul come from the adult "Third Leg" skill nodes (1 = base): they
        // stretch the member length, thicken it, and grow the berries respectively.
        public void Build(Transform pelvis, Color skin, float girth, float lenMul, float girthMul, float ballMul)
        {
            _pelvis = pelvis;
            _scale = Mathf.Max(0.5f, girth);
            // Defensive floors: a stray 0 (e.g. an un-initialised appearance) must not collapse it.
            lenMul   = Mathf.Max(0.25f, lenMul);
            girthMul = Mathf.Max(0.25f, girthMul);
            ballMul  = Mathf.Max(0.25f, ballMul);
            _segLen = SegLen * _scale * lenMul;         // longer segments -> longer member
            _memberRadius = MemberR * _scale * girthMul;
            _berryRadius = BerryR * _scale * ballMul;

            // Two anchors (pelvis-local, -Y = down, +Y = up toward the torso). The member roots on
            // the front underside (where the berries used to sit) and hangs down; the berries move
            // UP under the torso, centred and slightly behind the member root, so the two don't
            // overlap.
            _rootLocal  = new Vector3(0f, -MemberDrop, MemberFwd) * _scale;
            _berryLocal = new Vector3(0f,  BerryRise,  BerryFwd)  * _scale;

            // Seed the chain hanging straight down in world space from the attach point.
            Vector3 root = _pelvis.TransformPoint(_rootLocal);
            for (int i = 0; i < Nodes; i++)
            {
                _pos[i] = root + Vector3.down * (_segLen * i);
                _prev[i] = _pos[i];
            }

            var mat = Make.Mat(skin, 0.15f);
            var rag = _pelvis.GetComponentInParent<ActiveRagdoll>();
            if (rag != null) rag.RegisterCosmeticMaterial(mat);

            _member = MakePiece("member", mat).transform;
            _berryL = MakePiece("berryL", mat).transform;
            _berryR = MakePiece("berryR", mat).transform;

            PoseBerries();
            PoseMember();
        }

        // A collider-less primitive sphere child (the pill is a stretched sphere). Parented under
        // this component's GameObject, which itself hangs under the pelvis.
        GameObject MakePiece(string name, Material mat)
        {
            var go = Make.Sphere(name, 1f, transform.position, mat, transform);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // never a hitbox
            return go;
        }

        void FixedUpdate()
        {
            if (_pelvis == null) return;
            float dt = Time.fixedDeltaTime;
            float g = SimConfig.HairGravity;
            float damp = SimConfig.HairDamping;
            float gStep = g * dt * dt;

            // 1) Pin the root to the pelvis attach point; Verlet-integrate the free nodes under
            //    gravity. The pinned root moving with the body drags the chain, so it swings.
            Vector3 root = _pelvis.TransformPoint(_rootLocal);
            _pos[0] = root; _prev[0] = root;
            for (int i = 1; i < Nodes; i++)
            {
                Vector3 vel = (_pos[i] - _prev[i]) * damp;
                _prev[i] = _pos[i];
                _pos[i] += vel;
                _pos[i] += new Vector3(0f, gStep, 0f);
            }

            // 2) Segment-length constraints (a few iterations), root fixed.
            for (int it = 0; it < SimConfig.HairConstraintIters; it++)
            {
                for (int i = 0; i < Nodes - 1; i++)
                {
                    int a = i, b = i + 1;
                    Vector3 d = _pos[b] - _pos[a];
                    float len = d.magnitude;
                    if (len < 1e-6f) continue;
                    float diff = (len - _segLen) / len;
                    if (a == 0) _pos[b] -= d * diff;                     // root fixed: move only b
                    else { _pos[a] += d * (0.5f * diff); _pos[b] -= d * (0.5f * diff); }
                }
            }

            // 3) Body collision: push each FREE node out of any player-body collider it sinks into,
            //    but NEVER the ball. No physics layers exist in this project, so filter by component:
            //    keep ragdoll colliders, skip anything under a BallController (or our own pieces).
            for (int i = 1; i < Nodes; i++)
            {
                float r = _memberRadius;
                int n = Physics.OverlapSphereNonAlloc(_pos[i], r, _hits, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var col = _hits[h];
                    if (col == null) continue;
                    if (col.GetComponentInParent<BallController>() != null) continue;   // never the ball
                    if (col.GetComponentInParent<ActiveRagdoll>() == null) continue;    // only player bodies
                    // Push the node to just outside the collider surface.
                    Vector3 cp = col.ClosestPoint(_pos[i]);
                    Vector3 away = _pos[i] - cp;
                    float m = away.magnitude;
                    if (m < 1e-4f)
                    {
                        // Node is inside the collider (ClosestPoint returns the point itself):
                        // shove it out along the vector from the collider centre.
                        away = _pos[i] - col.bounds.center; away.y = 0f;
                        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
                        _pos[i] = col.bounds.center + away.normalized * (col.bounds.extents.magnitude + r);
                    }
                    else if (m < r)
                    {
                        _pos[i] = cp + away * (r / m);
                    }
                }
            }

            PoseBerries();
            PoseMember();
        }

        // The two berries sit tucked up under the torso, side by side, centred at their own fixed
        // anchor (above + behind the member root) so they don't overlap the member. They ride the
        // pelvis rigidly (no sway of their own).
        void PoseBerries()
        {
            if (_berryL == null) return;
            Vector3 seat = _pelvis.TransformPoint(_berryLocal);
            Vector3 right = _pelvis.right;
            float d = _berryRadius * 2f;
            _berryL.position = seat - right * (BerryGap * _scale);
            _berryR.position = seat + right * (BerryGap * _scale);
            _berryL.localScale = _berryR.localScale = new Vector3(d, d, d);
            _berryL.rotation = _berryR.rotation = _pelvis.rotation;
        }

        // The member is a sphere stretched into a pill spanning root -> tip node.
        void PoseMember()
        {
            if (_member == null) return;
            Vector3 a = _pos[0];
            Vector3 b = _pos[Nodes - 1];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 axis = b - a;
            float len = axis.magnitude;
            _member.position = mid;
            if (len > 1e-4f) _member.rotation = Quaternion.FromToRotation(Vector3.up, axis / len);
            else _member.rotation = _pelvis.rotation;
            // Stretched sphere: diameter across, (length + a rounded cap) along the axis (local Y).
            float dia = _memberRadius * 2f;
            _member.localScale = new Vector3(dia, len * 0.5f + _memberRadius, dia);
        }
    }
}
