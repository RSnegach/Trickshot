using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // Which length bucket a hair style belongs to (for the customize menu grouping).
    public enum HairGroup { Short, Medium, Long }

    // Head cosmetics: hair, facial hair, and accessories, all built as COLLIDER-LESS child
    // visuals of the head bone. Nothing here ever gets a collider or enters the ragdoll's
    // own-collider list, so cosmetics are purely visual and never affect the ball. Shapes are
    // approximated from Unity primitives (spheres/boxes), the same fidelity bar as the flags.
    //
    // Style index 0 is always "none": bald hair, clean-shaven face, no accessory.
    public static partial class Cosmetics
    {
        // Nominal head radius in world metres (the head sphere is ~0.19 radius). Cosmetics are
        // sized/placed relative to this. Local space of Phys(Bone.Head): +Y up, +Z faces the
        // front of the face, +X is to the side. These are facing-independent (local to the head).
        const float HeadR = 0.19f;

        // How far back a HORSE's hair anchor is tilted about X, degrees. A mane is the same hair
        // catalog as a human's, so the only difference is which way "down" points: on a horse the
        // strands have to fall along the CREST of the neck, not off the front of the face. The neck
        // is decor "D_Neck" in BodyLayout, pitched by exactly this euler, so a style whose flow is
        // (0,-1,0) drapes straight down the crest. KEEP THESE TWO NUMBERS EQUAL.
        const float ManeTiltDeg = 38.9f;

        // A horse TAIL's dock radius, per unit of each body scale. HairSim roots every strand on a
        // SPHERE about its anchor's origin, so a tail hung on the PELVIS needs that box expressed as a
        // radius. The horse pelvis is a Box of (0.30g, 0.30h, 0.32g), i.e. half extents
        // (0.15g, 0.15h, 0.16g), and BackCluster's mid root direction is (0, 0.549, -0.836). That
        // direction leaves the TOP face at 0.15/0.549 = 0.273 per unit HEIGHT and the REAR face at
        // 0.16/0.836 = 0.191 per unit GIRTH, so whichever comes first is the real surface and the
        // smaller of the two is the radius. KEEP THESE IN STEP WITH THE PELVIS DIMS IN BodyLayout.Horse.
        //
        // Taking the min rather than just girth-scaling is what holds the dock on the rump at both ends
        // of BOTH sliders. A pure girth radius floats it about 0.07 m clear of a max-weight,
        // min-height rump, because that build is a wide flat slab where the TOP face binds instead.
        const float TailDockPerHeight = 0.273f;
        const float TailDockPerGirth  = 0.191f;
        // Tail length in metres at unit height scale. HairSim never scales def.length itself, so this
        // is multiplied by the body's height scale at build time - which the MANE deliberately is not.
        // 0.55 puts the tip about level with the hocks on a default horse.
        const float TailLen = 0.55f;

        // Head girth scale for the CURRENT AttachAppearance pass. The head's visible radius is
        // HeadR * girth, but cosmetics parent to the head BONE (localScale=1), so their fixed
        // literal offsets/sizes must be multiplied by this to track the head as it grows/shrinks
        // with weight. Set at the top of AttachAppearance; read by Ball/Blk/BeardMesh/CrownPatch.
        // Builds are sequential (no reentrancy), so a static scratch field is safe.
        static float _cosScale = 1f;

        // ---- catalog entry types --------------------------------------------
        // Hair is now a SOFT DYNAMIC style (HairSim), described by data, not a rigid primitive
        // builder. Bald is the one entry with no def (Bald = true). Everything else is simulated
        // strands that fall/swing/collide, built the same way the goal net is.
        public class HairEntry
        {
            public string Name; public HairGroup Group;
            public bool Bald;                 // index 0 only: no hair at all
            public HairSim.HairDef Def;       // the simulated card style: the human style for the
                                              // long drapes, the horse MANE for every entry
            public bool HumanOnlyDef;         // true: on a HUMAN, Def is skipped and Extra builds
                                              // the whole style (shell + geometry + its own sims);
                                              // the horse still builds Def as its mane
            public Action<Transform, HairMats> Extra;  // the human builder (see Cosmetics.Hair.cs)
        }
        public class FacialEntry
        {
            public string Name;
            public Action<Transform, Material> Build;
            // Material factory (facial colour, skin colour) -> the style's main material. Null =
            // the flat lit default. Stubble uses it for a blended decal tinted toward the skin.
            public Func<Color, Color, Material> Mat;
            // Optional second pass with a hair-card TUFT material (a beard's wispy fringe).
            public Action<Transform, Material> Fringe;
        }
        public class AccessoryEntry
        {
            public string Name; public bool Headgear;   // headgear can't combine with non-bald hair
            public Action<Transform, Material> Build;
        }

        // ---- public API -----------------------------------------------------
        public static IReadOnlyList<HairEntry> Hair => _hair;
        public static IReadOnlyList<FacialEntry> Facial => _facial;
        public static IReadOnlyList<AccessoryEntry> Accessories => _accessories;

        public static bool IsBald(int hairStyle) => hairStyle <= 0;
        public static bool AccessoryIsHeadgear(int accessory)
            => accessory > 0 && accessory < _accessories.Count && _accessories[accessory].Headgear;

        // Build all of a player's head cosmetics onto an already-built ragdoll. Reads the head
        // bone transform, makes up to three shared tint materials (hair/facial/accessory) and
        // registers them on the ragdoll so it can free them on teardown. Safe to call with any
        // style indices (0 = none skips that category). Never adds a collider.
        public static void AttachAppearance(ActiveRagdoll rag, PlayerAppearance a)
        {
            if (rag == null) return;

            // ADULT MODE FIRST, above the species guards below. It is not a head cosmetic and it is
            // not human-only: any species whose SpeciesDef.AllowsAdult is set gets it, which now
            // includes both quadrupeds. It used to sit at the BOTTOM of this method, underneath two
            // early returns that dropped out for everything except a human - so the flag, the age
            // gate, the Third Leg tab and the networked dims all worked for a horse and nothing was
            // ever built.
            AttachAdult(rag, a);
            // The HAIR catalog is shared with the horse, whose MANE slot is human hair by design (see
            // SpeciesCosmetics.UsesHumanHair): same styles, same cards, same atlas, combed back along
            // the neck crest instead of standing off the crown. Nothing else here is shared, and the
            // guard lower down says why. Every other species draws from its decor table only.
            bool human = a.SpeciesId == Species.HumanId;
            bool mane  = a.SpeciesId == Species.HorseId;
            if (!human && !mane) return;
            var head = rag.Phys(Bone.Head);
            if (head == null) return;

            // Every cosmetic this pass scales by the head's girth so it tracks the head as weight
            // changes the visible head radius (HeadR * girth). Read once here; Ball/Blk/BeardMesh/
            // CrownPatch multiply their fixed literals by it.
            //
            // A horse's skull is a different SIZE, not just a different girth (0.15 m radius against
            // the human 0.19), so it scales by the ratio of its visible radius to the human nominal.
            // That ratio IS girth on a human, but it is computed only off the human path so the human
            // number stays exactly rag.GirthScale rather than a round trip through a divide.
            _cosScale = human ? rag.GirthScale : rag.HeadVisualRadius / HeadR;

            // Hair (index 0 = bald -> nothing). A HUMAN style builds through its entry: the long
            // drapes are one HairSim of Def; every short style is Extra (a shaped scalp shell plus
            // tapered tuft cards, generated geometry or a decal, see Cosmetics.Hair.cs). A HORSE
            // builds ONLY Def, as tilted cards on the neck crest: shells and geometry are human
            // anatomy and would land on the skull.
            if (a.HairStyle > 0 && a.HairStyle < _hair.Count && !_hair[a.HairStyle].Bald)
            {
                var entry = _hair[a.HairStyle];
                var mats = new HairMats(rag, a.HairColor);
                if (human && entry.HumanOnlyDef)
                {
                    entry.Extra?.Invoke(head, mats);
                }
                else
                {
                    // ROTATION ONLY on the anchor, never a translation: HairSim treats its anchor's
                    // ORIGIN as the head sphere's centre. Tilting turns every style's root scatter and
                    // flow together, which is exactly the knob a mane needs.
                    var tilt = mane ? Quaternion.Euler(ManeTiltDeg, 0f, 0f) : Quaternion.identity;
                    Sim(head, entry.Def, mats, rag.HeadVisualRadius, tilt);
                }
            }

            // ---- HORSE TAIL ---------------------------------------------------------------------
            // The second HairSim on the body, and the only cosmetic here that does NOT hang off the
            // head. Same catalog material, atlas and cards as the mane, on the hips. It replaces the
            // old D_Tail capsule in BodyLayout, which was a rigid cone that could not swing.
            //
            // Outside the hair block above ON PURPOSE, so it is not gated by the mane style: a horse
            // always has a tail, including a bald-maned one. See the BodyLayout comment where D_Tail
            // used to be for why the tail is one fixed style rather than the player's pick.
            if (mane)
            {
                var pelvis = rag.Phys(Bone.Pelvis);
                if (pelvis != null)
                {
                    // Its own material rather than a hoisted share with the mane above. Same colour and
                    // same shader, so identical on screen, and it keeps the human hair path untouched.
                    var tailMat = Make.Hair(a.HairColor);
                    rag.RegisterCosmeticMaterial(tailMat);

                    var go = new GameObject("TailSim");
                    go.transform.SetParent(pelvis, false);
                    // ROTATION-ONLY still applies, and here the rotation is NONE. BackCluster already
                    // roots up-and-back where a dock belongs, so there is nothing to tilt. That makes
                    // this the cleaner of the two anchors: HairSim writes its verts in the space of the
                    // transform it is HANDED while the mesh renders under this child, so an untilted
                    // child renders exactly where the sim put it, whereas the mane's tilted child
                    // renders rotated off its own collision sphere. It is still a child object and not
                    // the pelvis itself only because HairSim RequireComponents a MeshFilter, and the
                    // pelvis transform already carries one.
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;

                    // No crown patch, unlike the mane: the dock sits against the solid coat-coloured
                    // pelvis box, so there is no bare scalp for the gaps between clumps to show.
                    float dockR = Mathf.Min(TailDockPerHeight * rag.HeightScale,
                                            TailDockPerGirth * rag.GirthScale) + 0.01f;

                    var def = new HairSim.HairDef
                    {
                        root         = HairSim.RootMode.BackCluster,
                        strands      = 46,
                        nodes        = 10,
                        length       = TailLen * rag.HeightScale,
                        fan          = 5,
                        staticToHead = false,     // a tail that cannot swing is a stick
                        // Deliberately floppy, so GRAVITY sets the hang and the gait swings it. flow is
                        // the styled REST direction the stiffness holds toward, not the final shape, so
                        // a low stiffness plus a rearward flow reads as a tail set out at the dock and
                        // falling from there. The -0.30 of rear bias is also what keeps the hair clear
                        // of the hind legs, whose rear faces sit about 0.07 ahead of the dock.
                        stiffness    = 0.16f,
                        flow         = new Vector3(0f, -1f, -0.30f),
                        curl         = 0.012f,
                        jitter       = 0.10f,
                        thickness    = 0.07f,     // a tail clump is thicker than a hair clump
                    };
                    go.AddComponent<HairSim>().Build(pelvis, def, tailMat, dockR);
                }
            }

            // Everything below is HUMAN-ONLY, and not by taste. On an animal, StyleB and StyleC are
            // MARKINGS and TACK: those indices address the species' own lists (SpeciesCosmetics) and
            // the species' decor table, NOT _facial and _accessories. Running these builders would
            // draw a beard picked by a marking index. Animal geometry comes from BodyLayout.Decor.
            if (!human) return;
            // Facial hair (index 0 = clean-shaven -> nothing). Every style is a surface shaped to
            // the head (see Cosmetics.Facial.cs); the chest seam is measured first so no outline
            // dips into the jersey, and Sideburns read the bald flag for their top edge.
            if (a.FacialStyle > 0 && a.FacialStyle < _facial.Count)
            {
                var fe = _facial[a.FacialStyle];
                MeasureChest(rag, head);
                _bald = IsBald(a.HairStyle);
                Material mat;
                if (fe.Mat != null) mat = fe.Mat(a.FacialColor, a.Skin);
                else
                {
                    // Same shader as a scalp shell so beard and hair shade alike: mostly Lambert on
                    // the geometric normal, a little anisotropic sheen down the comb direction.
                    mat = Make.HairCap(a.FacialColor);
                    mat.SetFloat("_NormalWeight", 0.7f);
                    mat.SetFloat("_SpecStr", 0.12f);
                }
                rag.RegisterCosmeticMaterial(mat);
                fe.Build(head, mat);
                if (fe.Fringe != null)
                {
                    var tm = Make.HairTuft(a.FacialColor);
                    rag.RegisterCosmeticMaterial(tm);
                    fe.Fringe(head, tm);
                }
            }
            // Accessory (index 0 = none -> nothing). Headgear is only worn when bald; if hair is
            // present, silently skip a headgear accessory (the UI also blocks equipping it).
            if (a.Accessory > 0 && a.Accessory < _accessories.Count)
            {
                var acc = _accessories[a.Accessory];
                if (!(acc.Headgear && !IsBald(a.HairStyle)))
                {
                    var mat = Make.Mat(a.AccessoryColor, 0.25f);
                    rag.RegisterCosmeticMaterial(mat);
                    acc.Build(head, mat);
                }
            }
        }

        // Adult-mode appendage: a Verlet pendulum under the pelvis (skin/coat-tinted, build-scaled).
        // Hanging, it is collider-less and pushes out of player bodies but never the ball; held to
        // attention (the ThirdLeg bind) it goes rigid and carries a hitbox the ball can be struck with.
        //
        // Gated on the SPECIES capability rather than an id list, so it follows AllowsAdult exactly
        // like the toggle, the age prompt, the Third Leg tab and the networked dims already do.
        // AnatomySim measures its own anchors off the built colliders for a quadruped, so a horse and
        // an elephant land correctly under the belly without a per-species table here.
        static void AttachAdult(ActiveRagdoll rag, PlayerAppearance a)
        {
            if (!a.Adult) return;
            var def = Species.ById(a.SpeciesId);
            if (!def.AllowsAdult) return;
            var pelvis = rag.Phys(Bone.Pelvis);
            if (pelvis == null) return;
            var go = new GameObject("AnatomySim");
            go.transform.SetParent(pelvis, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var sim = go.AddComponent<AnatomySim>();
            sim.Build(rag, pelvis, a.Skin, def.AdultScale, def.AdultGrowth,
                      a.MemberLen, a.MemberGirth, a.BallSize);
            rag.Anatomy = sim;   // the Striker and the ball reach it through the body
        }

        // ---- collider-less piece helpers ------------------------------------
        // A rounded shell (sphere) parented to the head; its primitive collider is destroyed.
        static void Ball(Transform head, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = Make.Sphere("cz", 1f, head.position, mat, head);
            go.transform.localPosition = localPos * _cosScale;      // offset scales so the piece stays on the head
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale * _cosScale;       // size scales with the head
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }

        // A flat/blocky piece (box) parented to the head; Make.Box(collider:false) skips the collider.
        static void Blk(Transform head, Vector3 localPos, Vector3 localScale, Material mat)
            => Blk(head, localPos, localScale, Vector3.zero, mat);

        static void Blk(Transform head, Vector3 localPos, Vector3 localScale, Vector3 euler, Material mat)
        {
            var go = Make.Box("cz", Vector3.one, head.position, mat, head, collider: false);
            go.transform.localPosition = localPos * _cosScale;      // offset + size scale with the head girth
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale * _cosScale;
        }

        // ---- "drawn on" head pieces -----------------------------------------
        // The next three build the same way as BeardMesh below: a small triangle grid pushed a
        // few mm proud of the head sphere, so they read as regions painted ON the actual head
        // (an eyepatch, a strap, a headband) rather than boxes floating around it. Same
        // double-sided emit, same GeneratedMeshOwner teardown, same _cosScale tracking.

        // An elliptical patch flush to the head, centred on `dir` (any direction; normalised
        // here), halfW/halfH angular half-sizes in RADIANS. The eyepatch: the eye is simply
        // covered on the head itself.
        static void HeadPatch(Transform head, Material mat, Vector3 dir, float halfW, float halfH)
        {
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize();
            Vector3 up2 = Vector3.Cross(side, dir).normalized;

            const int cols = 12, rows = 8;
            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= rows; j++)
            for (int i = 0; i <= cols; i++)
            {
                float u = (i / (float)cols) * 2f - 1f;       // -1..1 across
                float v = (j / (float)rows) * 2f - 1f;        // -1..1 down
                // Swing the centre direction across (yaw about up2) then down (pitch about
                // side): a small-angle cap that stays an ellipse-ish region on the sphere.
                Vector3 d = Quaternion.AngleAxis(u * halfW * Mathf.Rad2Deg, up2) * dir;
                d = Quaternion.AngleAxis(v * halfH * Mathf.Rad2Deg, side) * d;
                d.Normalize();
                int idx = j * (cols + 1) + i;
                verts[idx] = d * (HeadR + 0.004f) * _cosScale;
                norms[idx] = d;
                uvs[idx] = new Vector2(i / (float)cols, 1f - j / (float)rows);
            }
            EmitHeadGrid(head, mat, verts, norms, uvs, cols, rows);
        }

        // A thin strap lying ON the head sphere from `from` to `to` (unit directions), bowing
        // down mid-path like a cord under its own weight (`bow` = how far it droops toward the
        // neck, 0 = taut great-circle). The eyepatch strap over the back of the head.
        static void HeadLine(Transform head, Material mat, Vector3 from, Vector3 to, float width, float bow)
        {
            from.Normalize(); to.Normalize();
            Vector3 DirAt(float t)
            {
                Vector3 d = Vector3.Slerp(from, to, t);
                return Vector3.Slerp(d, Vector3.down, bow * Mathf.Sin(t * Mathf.PI)).normalized;
            }
            const int seg = 24;
            var verts = new Vector3[(seg + 1) * 2];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                Vector3 d = DirAt(t);
                Vector3 tan = (DirAt(Mathf.Min(t + 0.02f, 1f)) - DirAt(Mathf.Max(t - 0.02f, 0f))).normalized;
                if (tan.sqrMagnitude < 1e-8f) tan = Vector3.Cross(d, Vector3.up);
                // Edge = tangent x radial, so the ribbon lies flat along the sphere surface.
                Vector3 edge = Vector3.Cross(tan, d);
                if (edge.sqrMagnitude < 1e-8f) edge = Vector3.Cross(tan, Vector3.up);
                edge.Normalize();
                float rr = (HeadR + 0.004f) * _cosScale;
                float hw = width * 0.5f * _cosScale;
                verts[i * 2]     = d * rr - edge * hw;
                verts[i * 2 + 1] = d * rr + edge * hw;
                norms[i * 2] = d; norms[i * 2 + 1] = d;
                uvs[i * 2] = new Vector2(0f, t); uvs[i * 2 + 1] = new Vector2(1f, t);
            }
            EmitHeadStrip(head, mat, verts, norms, uvs, seg);
        }

        // A band ringing the whole head at latitude `phi` (radians down from the crown),
        // lying flush on the sphere. The headband: drawn on, not a box around the head.
        static void HeadRing(Transform head, Material mat, float phi, float width)
        {
            const int seg = 32;
            var verts = new Vector3[(seg + 1) * 2];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            float cphi = Mathf.Cos(phi), sphi = Mathf.Sin(phi);
            for (int i = 0; i <= seg; i++)
            {
                float theta = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 d = new Vector3(sphi * Mathf.Sin(theta), cphi, sphi * Mathf.Cos(theta)).normalized;
                // Edge along the meridian so the band lies flat and follows the latitude.
                Vector3 edge = new Vector3(-cphi * Mathf.Sin(theta), sphi, -cphi * Mathf.Cos(theta)).normalized;
                float rr = (HeadR + 0.004f) * _cosScale;
                float hw = width * 0.5f * _cosScale;
                verts[i * 2]     = d * rr - edge * hw;
                verts[i * 2 + 1] = d * rr + edge * hw;
                norms[i * 2] = d; norms[i * 2 + 1] = d;
                uvs[i * 2] = new Vector2(0f, i / (float)seg); uvs[i * 2 + 1] = new Vector2(1f, i / (float)seg);
            }
            EmitHeadStrip(head, mat, verts, norms, uvs, seg);
        }

        // Shared tail for the drawn-on GRID pieces: mesh + double-sided triangles + the
        // GeneratedMeshOwner so teardown frees the runtime mesh (same as BeardMesh).
        static void EmitHeadGrid(Transform head, Material mat,
                                 Vector3[] verts, Vector3[] norms, Vector2[] uvs, int cols, int rows)
        {
            var tris = new int[cols * rows * 12];
            int t = 0;
            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = j * (cols + 1) + i, b = a + 1, c = a + (cols + 1), d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            EmitHeadMesh(head, mat, verts, norms, uvs, tris);
        }

        // Shared tail for the drawn-on STRIP pieces (2 verts per sample down the path).
        static void EmitHeadStrip(Transform head, Material mat,
                                   Vector3[] verts, Vector3[] norms, Vector2[] uvs, int seg)
        {
            var tris = new int[seg * 12];
            int t = 0;
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            EmitHeadMesh(head, mat, verts, norms, uvs, tris);
        }

        static void EmitHeadMesh(Transform head, Material mat,
                                 Vector3[] verts, Vector3[] norms, Vector2[] uvs, int[] tris)
        {
            var mesh = new Mesh();
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject("cz");
            go.transform.SetParent(head, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
        }

        // ---- accessory catalog (index 0 = None) -----------------------------
        static readonly List<AccessoryEntry> _accessories = new List<AccessoryEntry>
        {
            new AccessoryEntry { Name = "None", Headgear = false, Build = (h,m) => { } },

            // EYEWEAR / MASKS ---------------------------------------------
            new AccessoryEntry { Name = "Glasses", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.08f, 0.02f, 0.185f), new Vector3(0.09f, 0.09f, 0.025f), m);     // left rim
                Ball(h, new Vector3(0.08f, 0.02f, 0.185f), new Vector3(0.09f, 0.09f, 0.025f), m);      // right rim
                Blk(h, new Vector3(-0.08f, 0.02f, 0.19f), new Vector3(0.065f, 0.065f, 0.015f), Glass()); // left lens
                Blk(h, new Vector3(0.08f, 0.02f, 0.19f), new Vector3(0.065f, 0.065f, 0.015f), Glass());  // right lens
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.05f, 0.015f, 0.02f), m);           // bridge
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);        // left arm
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);         // right arm
            } },
            new AccessoryEntry { Name = "Square Glasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.08f, 0.02f, 0.185f), new Vector3(0.11f, 0.09f, 0.02f), m);        // left rectangular frame
                Blk(h, new Vector3(-0.08f, 0.02f, 0.193f), new Vector3(0.09f, 0.07f, 0.012f), Glass());
                Blk(h, new Vector3(0.08f, 0.02f, 0.185f), new Vector3(0.11f, 0.09f, 0.02f), m);         // right rectangular frame
                Blk(h, new Vector3(0.08f, 0.02f, 0.193f), new Vector3(0.09f, 0.07f, 0.012f), Glass());
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.05f, 0.02f, 0.02f), m);            // bridge
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);        // left arm
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);         // right arm
            } },
            new AccessoryEntry { Name = "Sunglasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.05f, 0.185f), new Vector3(0.32f, 0.02f, 0.02f), m);            // top brow bar
                Blk(h, new Vector3(-0.08f, 0.02f, 0.19f), new Vector3(0.12f, 0.09f, 0.015f), Dark());   // left dark lens (wide)
                Blk(h, new Vector3(0.08f, 0.02f, 0.19f), new Vector3(0.12f, 0.09f, 0.015f), Dark());    // right dark lens (wide)
                Blk(h, new Vector3(0f, 0.03f, 0.185f), new Vector3(0.06f, 0.02f, 0.02f), m);            // bridge
                Blk(h, new Vector3(-0.15f, 0.04f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);         // left arm
                Blk(h, new Vector3(0.15f, 0.04f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);          // right arm
            } },
            new AccessoryEntry { Name = "Aviators", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.085f, 0.02f, 0.185f), new Vector3(0.12f, 0.10f, 0.03f), m);       // thick left frame (trapezoid feel)
                Blk(h, new Vector3(-0.085f, 0.02f, 0.196f), new Vector3(0.09f, 0.07f, 0.014f), Dark());
                Blk(h, new Vector3(0.085f, 0.02f, 0.185f), new Vector3(0.12f, 0.10f, 0.03f), m);        // thick right frame
                Blk(h, new Vector3(0.085f, 0.02f, 0.196f), new Vector3(0.09f, 0.07f, 0.014f), Dark());
                Blk(h, new Vector3(0f, 0.06f, 0.185f), new Vector3(0.30f, 0.025f, 0.025f), m);          // heavy brow bar
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.05f, 0.025f, 0.025f), m);          // thick bridge
                Blk(h, new Vector3(-0.16f, 0.03f, 0.09f), new Vector3(0.035f, 0.025f, 0.18f), m);       // chunky left arm
                Blk(h, new Vector3(0.16f, 0.03f, 0.09f), new Vector3(0.035f, 0.025f, 0.18f), m);        // chunky right arm
            } },
            new AccessoryEntry { Name = "Visor Shades", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.02f, 0.19f), new Vector3(0.30f, 0.07f, 0.02f), Dark());        // main wraparound band
                Blk(h, new Vector3(-0.17f, 0.02f, 0.12f), new Vector3(0.05f, 0.06f, 0.08f), new Vector3(0f, 35f, 0f), Dark()); // left wrap edge
                Blk(h, new Vector3(0.17f, 0.02f, 0.12f), new Vector3(0.05f, 0.06f, 0.08f), new Vector3(0f, -35f, 0f), Dark()); // right wrap edge
                Blk(h, new Vector3(0f, 0.06f, 0.185f), new Vector3(0.32f, 0.015f, 0.02f), m);           // frame trim above band
                Blk(h, new Vector3(-0.16f, 0.03f, 0.06f), new Vector3(0.025f, 0.02f, 0.14f), m);        // left arm
                Blk(h, new Vector3(0.16f, 0.03f, 0.06f), new Vector3(0.025f, 0.02f, 0.14f), m);         // right arm
            } },
            new AccessoryEntry { Name = "Monocle", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0.08f, 0.02f, 0.19f), new Vector3(0.11f, 0.11f, 0.025f), m);        // rim
                Blk(h, new Vector3(0.08f, 0.02f, 0.196f), new Vector3(0.075f, 0.075f, 0.014f), Glass()); // lens
                Blk(h, new Vector3(0.08f, 0.02f, 0.175f), new Vector3(0.02f, 0.02f, 0.015f), m);        // clip/stud
                Blk(h, new Vector3(0.13f, -0.02f, 0.16f), new Vector3(0.012f, 0.06f, 0.012f), new Vector3(0f, 0f, 25f), m);  // chain link 1
                Blk(h, new Vector3(0.16f, -0.09f, 0.13f), new Vector3(0.012f, 0.08f, 0.012f), new Vector3(0f, 0f, 45f), m);  // chain link 2, hangs down
            } },
            new AccessoryEntry { Name = "Eyepatch", Headgear = false, Build = (h,m) => {
                // No added shapes: the patch is a region DRAWN ON the head where the right eye
                // is (a flush dark cap, same trick as the beard bib), and the strap is a line
                // drawn from the patch edge over the crown to the nape that sags like a cord.
                HeadPatch(h, Dark(), new Vector3(0.08f, 0.02f, 0.19f), 0.30f, 0.34f);
                HeadLine(h, m, new Vector3(0.08f, 0.10f, 0.18f), new Vector3(0f, -0.06f, -0.19f), 0.018f, 0.15f);
            } },
            new AccessoryEntry { Name = "Ski Goggles", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.02f, 0.19f), new Vector3(0.32f, 0.11f, 0.02f), Glass());       // big lens band
                Blk(h, new Vector3(0f, 0.075f, 0.185f), new Vector3(0.34f, 0.03f, 0.025f), m);          // thick top frame
                Blk(h, new Vector3(0f, -0.035f, 0.185f), new Vector3(0.34f, 0.03f, 0.025f), m);         // thick bottom frame
                Blk(h, new Vector3(-0.16f, 0.02f, 0.17f), new Vector3(0.03f, 0.10f, 0.04f), m);         // left side frame
                Blk(h, new Vector3(0.16f, 0.02f, 0.17f), new Vector3(0.03f, 0.10f, 0.04f), m);          // right side frame
                Blk(h, new Vector3(-0.19f, 0.05f, 0.02f), new Vector3(0.03f, 0.04f, 0.20f), new Vector3(0f, 20f, 0f), m);   // left strap
                Blk(h, new Vector3(0.19f, 0.05f, 0.02f), new Vector3(0.03f, 0.04f, 0.20f), new Vector3(0f, -20f, 0f), m);   // right strap
            } },
            new AccessoryEntry { Name = "Reading Glasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.07f, -0.01f, 0.19f), new Vector3(0.09f, 0.045f, 0.018f), m);      // half-height left frame, low on nose
                Blk(h, new Vector3(-0.07f, -0.01f, 0.196f), new Vector3(0.07f, 0.035f, 0.012f), Glass());
                Blk(h, new Vector3(0.07f, -0.01f, 0.19f), new Vector3(0.09f, 0.045f, 0.018f), m);       // half-height right frame
                Blk(h, new Vector3(0.07f, -0.01f, 0.196f), new Vector3(0.07f, 0.035f, 0.012f), Glass());
                Blk(h, new Vector3(0f, -0.01f, 0.19f), new Vector3(0.04f, 0.015f, 0.015f), m);          // low bridge
                Blk(h, new Vector3(-0.14f, 0f, 0.11f), new Vector3(0.02f, 0.018f, 0.15f), new Vector3(6f, 0f, 0f), m);   // arm angling up to ear
                Blk(h, new Vector3(0.14f, 0f, 0.11f), new Vector3(0.02f, 0.018f, 0.15f), new Vector3(-6f, 0f, 0f), m);
            } },
            new AccessoryEntry { Name = "Batman Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.06f, 0.155f), new Vector3(0.36f, 0.30f, 0.10f), m);            // cowl face plate (upper)
                Blk(h, new Vector3(-0.10f, 0.24f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(0f, 0f, -12f), m);  // ear
                Blk(h, new Vector3(0.10f, 0.24f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(0f, 0f, 12f), m);    // ear
                Blk(h, new Vector3(0f, 0.14f, 0.17f), new Vector3(0.34f, 0.04f, 0.06f), m);             // brow ridge above eyes
                Blk(h, new Vector3(-0.09f, 0.09f, 0.215f), new Vector3(0.15f, 0.05f, 0.02f), Glass());  // left eye slit (wide)
                Blk(h, new Vector3(0.09f, 0.09f, 0.215f), new Vector3(0.15f, 0.05f, 0.02f), Glass());   // right eye slit (wide)
                Blk(h, new Vector3(0f, -0.10f, 0.16f), new Vector3(0.30f, 0.14f, 0.09f), m);            // jaw/cheek plate
                Blk(h, new Vector3(0f, -0.06f, 0.215f), new Vector3(0.22f, 0.05f, 0.02f), Dark());      // mouth (wide)
            } },
            new AccessoryEntry { Name = "Hockey Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.01f, 0.17f), new Vector3(0.34f, 0.40f, 0.07f), m);            // face plate
                Blk(h, new Vector3(-0.07f, 0.04f, 0.205f), new Vector3(0.04f, 0.04f, 0.02f), Dark());   // left eye hole
                Blk(h, new Vector3(0.07f, 0.04f, 0.205f), new Vector3(0.04f, 0.04f, 0.02f), Dark());    // right eye hole
                Blk(h, new Vector3(0f, -0.10f, 0.205f), new Vector3(0.03f, 0.03f, 0.02f), Dark());      // mouth hole
                Blk(h, new Vector3(0f, -0.02f, 0.207f), new Vector3(0.018f, 0.018f, 0.015f), Dark());   // nose vent
                Blk(h, new Vector3(-0.05f, 0.14f, 0.205f), new Vector3(0.015f, 0.015f, 0.012f), Dark()); // forehead vent left
                Blk(h, new Vector3(0.05f, 0.14f, 0.205f), new Vector3(0.015f, 0.015f, 0.012f), Dark());  // forehead vent right
                Blk(h, new Vector3(0f, -0.16f, 0.16f), new Vector3(0.14f, 0.06f, 0.06f), m);            // chin guard ridge
            } },
            new AccessoryEntry { Name = "Venetian Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.05f, 0.175f), new Vector3(0.34f, 0.26f, 0.06f), m);            // ornate upper-face plate
                Blk(h, new Vector3(0f, 0.15f, 0.19f), new Vector3(0.30f, 0.04f, 0.03f), new Vector3(4f, 0f, 0f), m);  // brow curve accent
                Blk(h, new Vector3(-0.08f, 0.04f, 0.205f), new Vector3(0.05f, 0.045f, 0.02f), Dark());  // left eye hole
                Blk(h, new Vector3(0.08f, 0.04f, 0.205f), new Vector3(0.05f, 0.045f, 0.02f), Dark());   // right eye hole
                Ball(h, new Vector3(-0.15f, -0.02f, 0.16f), new Vector3(0.04f, 0.05f, 0.03f), m);       // cheek flourish left
                Ball(h, new Vector3(0.15f, -0.02f, 0.16f), new Vector3(0.04f, 0.05f, 0.03f), m);        // cheek flourish right
                Blk(h, new Vector3(0f, 0.22f, 0.14f), new Vector3(0.06f, 0.10f, 0.03f), new Vector3(-15f, 0f, 0f), m); // top flourish/feather
            } },
            // ENVELOPS the whole head: a full-head hood replaces the old front-plate + straps,
            // so this is now headgear (hair cards would clip straight through the shell). The
            // canister and the eye frames sit proud of the hood's surface.
            new AccessoryEntry { Name = "Gas Mask", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.02f, 0f), new Vector3(0.50f, 0.44f, 0.50f), m);             // full-head hood
                Ball(h, new Vector3(0f, -0.07f, 0.24f), new Vector3(0.11f, 0.11f, 0.11f), m);         // round front canister at mouth
                Blk(h, new Vector3(0f, -0.07f, 0.30f), new Vector3(0.05f, 0.05f, 0.04f), m);          // filter nub
                Blk(h, new Vector3(-0.09f, 0.03f, 0.245f), new Vector3(0.11f, 0.10f, 0.025f), m);      // left eye frame
                Blk(h, new Vector3(-0.09f, 0.03f, 0.255f), new Vector3(0.085f, 0.075f, 0.014f), Glass());
                Blk(h, new Vector3(0.09f, 0.03f, 0.245f), new Vector3(0.11f, 0.10f, 0.025f), m);        // right eye frame
                Blk(h, new Vector3(0.09f, 0.03f, 0.255f), new Vector3(0.085f, 0.075f, 0.014f), Glass());
            } },
            new AccessoryEntry { Name = "Welding Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.02f, 0.16f), new Vector3(0.38f, 0.42f, 0.06f), m);             // large flat front plate
                Blk(h, new Vector3(0f, 0.04f, 0.195f), new Vector3(0.22f, 0.035f, 0.02f), Dark());      // horizontal view slit
                Blk(h, new Vector3(0f, 0.20f, 0.08f), new Vector3(0.30f, 0.03f, 0.05f), m);             // top hinge bar
                Blk(h, new Vector3(-0.18f, 0.10f, 0.05f), new Vector3(0.03f, 0.10f, 0.05f), m);         // left side hinge
                Blk(h, new Vector3(0.18f, 0.10f, 0.05f), new Vector3(0.03f, 0.10f, 0.05f), m);          // right side hinge
                Blk(h, new Vector3(0f, -0.18f, 0.10f), new Vector3(0.20f, 0.06f, 0.06f), m);            // chin guard
            } },

            // JEWELRY / FACE PROPS ----------------------------------------
            new AccessoryEntry { Name = "Pipe", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.06f, -0.07f, 0.24f), new Vector3(0.05f, 0.03f, 0.14f), m);        // stem forward from mouth
                Blk(h, new Vector3(0.06f, -0.07f, 0.185f), new Vector3(0.055f, 0.035f, 0.02f), m);     // mouthpiece flare at the lips
                Blk(h, new Vector3(0.06f, -0.02f, 0.315f), new Vector3(0.05f, 0.09f, 0.055f), m);      // bowl standing up at the end
                Blk(h, new Vector3(0.06f, -0.065f, 0.315f), new Vector3(0.035f, 0.015f, 0.045f), m);   // bowl foot/base
                Blk(h, new Vector3(0.06f, 0.02f, 0.315f), new Vector3(0.03f, 0.01f, 0.035f), Dark());  // charred rim at the bowl opening
            } },
            new AccessoryEntry { Name = "Stud Earrings", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.19f, 0.0f, 0.0f), new Vector3(0.025f, 0.025f, 0.025f), m);      // left ear stud
                Ball(h, new Vector3(0.19f, 0.0f, 0.0f), new Vector3(0.025f, 0.025f, 0.025f), m);       // right ear stud
            } },
            new AccessoryEntry { Name = "Hoop Earrings", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.19f, -0.02f, 0.0f), new Vector3(0.008f, 0.014f, 0.03f), m);      // left hoop, top arc
                Blk(h, new Vector3(-0.19f, -0.08f, 0.0f), new Vector3(0.008f, 0.014f, 0.03f), m);      // left hoop, bottom arc
                Blk(h, new Vector3(-0.19f, -0.05f, 0.03f), new Vector3(0.008f, 0.03f, 0.014f), m);     // left hoop, front arc
                Blk(h, new Vector3(-0.19f, -0.05f, -0.03f), new Vector3(0.008f, 0.03f, 0.014f), m);    // left hoop, back arc
                Blk(h, new Vector3(0.19f, -0.02f, 0.0f), new Vector3(0.008f, 0.014f, 0.03f), m);       // right hoop, top arc
                Blk(h, new Vector3(0.19f, -0.08f, 0.0f), new Vector3(0.008f, 0.014f, 0.03f), m);       // right hoop, bottom arc
                Blk(h, new Vector3(0.19f, -0.05f, 0.03f), new Vector3(0.008f, 0.03f, 0.014f), m);      // right hoop, front arc
                Blk(h, new Vector3(0.19f, -0.05f, -0.03f), new Vector3(0.008f, 0.03f, 0.014f), m);     // right hoop, back arc
            } },
            new AccessoryEntry { Name = "Dangle Earrings", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.19f, 0.0f, 0.0f), new Vector3(0.016f, 0.016f, 0.016f), m);      // left stud
                Blk(h, new Vector3(-0.19f, -0.045f, 0.0f), new Vector3(0.006f, 0.03f, 0.006f), m);     // left link
                Ball(h, new Vector3(-0.19f, -0.09f, 0.0f), new Vector3(0.022f, 0.028f, 0.022f), m);    // left dangling drop
                Ball(h, new Vector3(0.19f, 0.0f, 0.0f), new Vector3(0.016f, 0.016f, 0.016f), m);       // right stud
                Blk(h, new Vector3(0.19f, -0.045f, 0.0f), new Vector3(0.006f, 0.03f, 0.006f), m);      // right link
                Ball(h, new Vector3(0.19f, -0.09f, 0.0f), new Vector3(0.022f, 0.028f, 0.022f), m);     // right dangling drop
            } },
            new AccessoryEntry { Name = "Nose Stud", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0.035f, -0.02f, 0.205f), new Vector3(0.01f, 0.01f, 0.01f), m);     // stud base on the nostril
                Ball(h, new Vector3(0.035f, -0.02f, 0.212f), new Vector3(0.006f, 0.006f, 0.006f), Glass()); // tiny gem sparkle
            } },
            new AccessoryEntry { Name = "Septum Ring", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.0f, -0.036f, 0.19f), new Vector3(0.014f, 0.006f, 0.006f), m);     // hoop top
                Blk(h, new Vector3(0.0f, -0.064f, 0.19f), new Vector3(0.014f, 0.006f, 0.006f), m);     // hoop bottom
                Blk(h, new Vector3(-0.014f, -0.05f, 0.19f), new Vector3(0.006f, 0.014f, 0.006f), m);   // hoop left
                Blk(h, new Vector3(0.014f, -0.05f, 0.19f), new Vector3(0.006f, 0.014f, 0.006f), m);    // hoop right
            } },
            new AccessoryEntry { Name = "Eyebrow Piercing", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.05f, 0.06f, 0.19f), new Vector3(0.03f, 0.006f, 0.006f), m);       // barbell bar over the brow
                Ball(h, new Vector3(0.02f, 0.06f, 0.19f), new Vector3(0.008f, 0.008f, 0.008f), m);     // inner ball end
                Ball(h, new Vector3(0.08f, 0.06f, 0.19f), new Vector3(0.008f, 0.008f, 0.008f), m);     // outer ball end
            } },
            new AccessoryEntry { Name = "Nipple Piercings", Headgear = false, Build = (h,m) => {
                // The eyebrow piercing's barbell (same bar + ball ends), worn twice on the chest
                // and spaced apart like nipples. Head-bone offsets like the Chain Necklace: the
                // pieces sit LOW and FORWARD of the head bone so they ride the front of the torso.
                Blk(h, new Vector3(-0.06f, -0.36f, 0.20f), new Vector3(0.03f, 0.006f, 0.006f), m);     // left bar
                Ball(h, new Vector3(-0.09f, -0.36f, 0.20f), new Vector3(0.008f, 0.008f, 0.008f), m);  // left outer ball end
                Ball(h, new Vector3(-0.03f, -0.36f, 0.20f), new Vector3(0.008f, 0.008f, 0.008f), m);  // left inner ball end
                Blk(h, new Vector3(0.06f, -0.36f, 0.20f), new Vector3(0.03f, 0.006f, 0.006f), m);     // right bar
                Ball(h, new Vector3(0.03f, -0.36f, 0.20f), new Vector3(0.008f, 0.008f, 0.008f), m);   // right inner ball end
                Ball(h, new Vector3(0.09f, -0.36f, 0.20f), new Vector3(0.008f, 0.008f, 0.008f), m);   // right outer ball end
            } },
            new AccessoryEntry { Name = "Cigar", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.05f, -0.07f, 0.26f), new Vector3(0.03f, 0.03f, 0.15f), m);       // thick body forward from the mouth
                Blk(h, new Vector3(-0.05f, -0.07f, 0.185f), new Vector3(0.032f, 0.032f, 0.012f), m);   // paper band near the mouth
                Ball(h, new Vector3(-0.05f, -0.07f, 0.35f), new Vector3(0.022f, 0.022f, 0.018f), Dark()); // burning ember tip
            } },
            new AccessoryEntry { Name = "Toothpick", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.06f, -0.075f, 0.24f), new Vector3(0.004f, 0.004f, 0.09f), m);     // thin pick from the mouth corner
                Blk(h, new Vector3(0.06f, -0.075f, 0.285f), new Vector3(0.002f, 0.002f, 0.02f), m);    // tapered tip
            } },
            new AccessoryEntry { Name = "Lollipop", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.0f, -0.07f, 0.25f), new Vector3(0.006f, 0.006f, 0.11f), m);       // thin stick from the mouth
                Ball(h, new Vector3(0.0f, -0.07f, 0.335f), new Vector3(0.035f, 0.035f, 0.035f), m);    // round candy on the end
                Ball(h, new Vector3(0.008f, -0.062f, 0.345f), new Vector3(0.01f, 0.01f, 0.01f), Glass()); // glossy candy shine
            } },
            new AccessoryEntry { Name = "Bindi", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0.0f, 0.06f, 0.188f), new Vector3(0.014f, 0.014f, 0.008f), m);     // backing dot centered on the brow
                Ball(h, new Vector3(0.0f, 0.06f, 0.194f), new Vector3(0.008f, 0.008f, 0.006f), Glass()); // small jewel center
            } },
            new AccessoryEntry { Name = "Vampire Fangs", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.035f, -0.10f, 0.185f), new Vector3(0.008f, 0.022f, 0.008f), new Vector3(8f, 0f, 0f), m);   // left fang pointing down
                Blk(h, new Vector3(0.035f, -0.10f, 0.185f), new Vector3(0.008f, 0.022f, 0.008f), new Vector3(-8f, 0f, 0f), m);   // right fang pointing down
            } },
            new AccessoryEntry { Name = "Chain Necklace", Headgear = false, Build = (h,m) => {
                // A chain resting ON the chest, NOT ringing the neck: the links sit LOWER (y -0.30,
                // below the head bone so they're at collar/upper-chest height) and the ring is
                // pushed FORWARD (+z biased) so it drapes over the front of the torso instead of
                // clipping through it. Torso box is ~0.18 half-width / 0.11 half-depth; the front
                // links sit proud of that, the back links hug the nape above the shoulders.
                Ball(h, new Vector3(0.0f, -0.34f, 0.20f), new Vector3(0.016f, 0.016f, 0.016f), m);     // front centre, on the sternum
                Ball(h, new Vector3(0.12f, -0.32f, 0.16f), new Vector3(0.016f, 0.016f, 0.016f), m);    // front-right
                Ball(h, new Vector3(0.20f, -0.27f, 0.04f), new Vector3(0.016f, 0.016f, 0.016f), m);    // right shoulder
                Ball(h, new Vector3(0.16f, -0.20f, -0.10f), new Vector3(0.014f, 0.014f, 0.014f), m);   // right, rising to nape
                Blk(h, new Vector3(0.0f, -0.17f, -0.19f), new Vector3(0.02f, 0.014f, 0.01f), Dark());  // clasp at the nape (above shoulders)
                Ball(h, new Vector3(-0.16f, -0.20f, -0.10f), new Vector3(0.014f, 0.014f, 0.014f), m);  // left, rising to nape
                Ball(h, new Vector3(-0.20f, -0.27f, 0.04f), new Vector3(0.016f, 0.016f, 0.016f), m);   // left shoulder
                Ball(h, new Vector3(-0.12f, -0.32f, 0.16f), new Vector3(0.016f, 0.016f, 0.016f), m);   // front-left
            } },

            // HEADWEAR (only wearable when bald) --------------------------
            new AccessoryEntry { Name = "Cap", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.15f, -0.01f), new Vector3(0.46f, 0.30f, 0.46f), m);     // crown
                Blk(h, new Vector3(0f, 0.10f, 0.22f), new Vector3(0.34f, 0.04f, 0.20f), m);       // brim forward
                Blk(h, new Vector3(0f, 0.083f, 0.22f), new Vector3(0.32f, 0.012f, 0.19f), Dark()); // darker brim underside
                Blk(h, new Vector3(0f, 0.155f, 0.235f), new Vector3(0.30f, 0.05f, 0.02f), m);     // front panel seam above brim
                Ball(h, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.06f, 0.06f, 0.06f), m);     // top button
                Blk(h, new Vector3(0f, 0.06f, -0.20f), new Vector3(0.10f, 0.05f, 0.03f), Dark()); // rear adjuster
            } },
            new AccessoryEntry { Name = "Bucket Hat", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.44f, 0.24f, 0.44f), m);     // short crown
                Ball(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.66f, 0.06f, 0.66f), m);         // all-around sloped brim
                Blk(h, new Vector3(0.13f, 0.14f, 0.13f), new Vector3(0.02f, 0.02f, 0.02f), Dark()); // eyelet vent
                Blk(h, new Vector3(-0.13f, 0.14f, -0.13f), new Vector3(0.02f, 0.02f, 0.02f), Dark()); // eyelet vent
            } },
            new AccessoryEntry { Name = "Fedora", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.20f, -0.01f), new Vector3(0.38f, 0.20f, 0.38f), m);      // crown
                Blk(h, new Vector3(0f, 0.25f, 0.05f), new Vector3(0.30f, 0.03f, 0.10f), m);       // front pinch crease
                Blk(h, new Vector3(-0.15f, 0.26f, -0.01f), new Vector3(0.05f, 0.04f, 0.30f), new Vector3(0f,0f,-10f), m); // left teardrop dent ridge
                Blk(h, new Vector3(0.15f, 0.26f, -0.01f), new Vector3(0.05f, 0.04f, 0.30f), new Vector3(0f,0f,10f), m);   // right teardrop dent ridge
                Blk(h, new Vector3(0f, 0.12f, 0f), new Vector3(0.66f, 0.03f, 0.66f), m);          // wide flat brim
                Blk(h, new Vector3(0f, 0.105f, 0f), new Vector3(0.64f, 0.012f, 0.64f), Dark());   // brim underside
                Blk(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.40f, 0.04f, 0.40f), Dark());     // band
                Blk(h, new Vector3(-0.14f, 0.10f, 0.14f), new Vector3(0.05f, 0.045f, 0.03f), m);  // band side bow
            } },
            new AccessoryEntry { Name = "Top Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.09f, 0f), new Vector3(0.56f, 0.03f, 0.56f), m);          // flat brim
                Blk(h, new Vector3(0f, 0.075f, 0f), new Vector3(0.54f, 0.012f, 0.54f), Dark());   // brim underside
                Blk(h, new Vector3(0f, 0.28f, -0.01f), new Vector3(0.34f, 0.34f, 0.34f), m);      // tall cylinder crown
                Blk(h, new Vector3(0f, 0.45f, -0.01f), new Vector3(0.36f, 0.02f, 0.36f), m);      // flat top rim
                Blk(h, new Vector3(0f, 0.14f, 0f), new Vector3(0.36f, 0.05f, 0.36f), Dark());     // band
                Blk(h, new Vector3(0f, 0.145f, 0.18f), new Vector3(0.05f, 0.045f, 0.02f), Glass()); // band buckle front
            } },
            new AccessoryEntry { Name = "Cowboy Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.22f, -0.01f), new Vector3(0.34f, 0.22f, 0.34f), m);      // crown
                Blk(h, new Vector3(0f, 0.12f, 0f), new Vector3(0.70f, 0.03f, 0.62f), m);          // wide brim
                Blk(h, new Vector3(-0.30f, 0.16f, 0f), new Vector3(0.24f, 0.03f, 0.30f), new Vector3(0f, 0f, 35f), m);  // left curl
                Blk(h, new Vector3(0.30f, 0.16f, 0f), new Vector3(0.24f, 0.03f, 0.30f), new Vector3(0f, 0f, -35f), m);  // right curl
            } },
            new AccessoryEntry { Name = "Beret", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0.02f, 0.17f, -0.02f), new Vector3(0.40f, 0.09f, 0.36f), m);   // small elliptical disc crown
                Ball(h, new Vector3(0.02f, 0.23f, -0.02f), new Vector3(0.04f, 0.04f, 0.04f), m);   // tiny stalk
            } },
            new AccessoryEntry { Name = "Peaky Cap", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.46f, 0.22f, 0.46f), m);     // low rounded crown
                Ball(h, new Vector3(0f, 0.15f, 0.10f), new Vector3(0.44f, 0.16f, 0.30f), m);      // front peak swept over the brim
                Blk(h, new Vector3(0f, 0.09f, 0.20f), new Vector3(0.30f, 0.03f, 0.12f), m);       // short stubby brim
                Blk(h, new Vector3(0f, 0.078f, 0.20f), new Vector3(0.28f, 0.01f, 0.11f), Dark()); // brim underside
                Ball(h, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.05f, 0.05f, 0.05f), m);     // top button
            } },
            new AccessoryEntry { Name = "Headband", Headgear = true, Build = (h,m) => {
                // DRAWN ON: a thin ribbon lying flush on the head sphere around the brow
                // (HeadRing), not a box ringing the head. Nothing sticks out.
                HeadRing(h, m, 1.07f, 0.045f);
            } },
            new AccessoryEntry { Name = "Trapper Hat", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.15f, -0.01f), new Vector3(0.46f, 0.26f, 0.46f), m);     // draped cloth dome
                Ball(h, new Vector3(0f, 0.12f, -0.20f), new Vector3(0.08f, 0.08f, 0.08f), m);     // back knot
                Blk(h, new Vector3(0f, -0.02f, -0.26f), new Vector3(0.09f, 0.28f, 0.04f), m);      // beaver-tail flap, hangs down the nape
            } },
            new AccessoryEntry { Name = "Sombrero", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.20f, -0.01f), new Vector3(0.32f, 0.18f, 0.32f), m);     // small crown
                Blk(h, new Vector3(0f, 0.14f, 0f), new Vector3(0.34f, 0.03f, 0.34f), Dark());     // crown band
                Ball(h, new Vector3(0f, 0.12f, 0f), new Vector3(0.90f, 0.03f, 0.90f), m);         // very wide flat brim
            } },
            new AccessoryEntry { Name = "Party Hat", Headgear = true, Build = (h,m) => {
                // Just a cone on top of the head: no base band, no tip gem.
                Ball(h, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.32f, 0.16f, 0.32f), m);     // lower cone
                Ball(h, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.16f, 0.16f, 0.16f), m);     // upper cone taper
            } },
            new AccessoryEntry { Name = "Wizard Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.08f, 0f), new Vector3(0.48f, 0.07f, 0.48f), m);          // band
                Ball(h, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.34f, 0.20f, 0.34f), m);     // cone base
                Ball(h, new Vector3(0f, 0.28f, -0.08f), new Vector3(0.22f, 0.18f, 0.22f), m);     // slumping cone
                Ball(h, new Vector3(0f, 0.34f, -0.16f), new Vector3(0.09f, 0.09f, 0.09f), m);     // drooping pom
            } },
        };

        // Small tint materials for mask details (independent of the accessory colour). Created
        // per call; the ragdoll can't track these, so keep them rare (only masks use them). They
        // are tiny and reclaimed on scene change.
        static Material Glass() => Make.Mat(new Color(0.6f, 0.8f, 0.95f, 1f), 0.6f);
        static Material Dark()  => Make.Mat(new Color(0.06f, 0.06f, 0.07f, 1f), 0.1f);
    }

    // Holds a runtime-generated Mesh (see Cosmetics.BeardMesh) and destroys it when its
    // GameObject is torn down. A Mesh is a native object that a plain Destroy(gameObject)
    // leaves dangling, and the customize preview rebuilds the body repeatedly, so without
    // this the generated beard meshes would leak. Mirrors ActiveRagdoll's material tracking.
    public class GeneratedMeshOwner : MonoBehaviour
    {
        public Mesh Mesh;
        void OnDestroy() { if (Mesh != null) Destroy(Mesh); }
    }
}
