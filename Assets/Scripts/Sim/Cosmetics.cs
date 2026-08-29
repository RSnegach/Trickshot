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
    public static class Cosmetics
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
            public HairSim.HairDef Def;       // the simulated style (ignored when Bald or NoCards)
            public bool NoCards;              // skip the HairSim card build - this style is just
                                              // solid pieces (e.g. the man bun = spheres)
            public Action<Transform, Material> Extra;  // optional solid hair-coloured pieces built
                                              // on the head (a bun, a tie), in the hair material
        }
        public class FacialEntry
        {
            public string Name;
            public Action<Transform, Material> Build;
            // True (default): the beard mesh wears the textured hair-card material (strand look +
            // sheen). False for Stubble (a flush flat shadow) and Clean (nothing) - they read wrong
            // with strand texture. Small Blk accents (mustache/handlebar) always take a flat mat.
            public bool CardTexture = true;
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

            // Hair (index 0 = bald -> nothing). A non-bald style is a SOFT DYNAMIC HairSim: a
            // child of the head carrying the line-mesh + the Verlet strand sim (built like the
            // net). Cosmetic only - no collider, never a hitbox. Runs on every body with hair,
            // so remote MP puppets' hair swings too (style + colour ride PlayerAppearance).
            if (a.HairStyle > 0 && a.HairStyle < _hair.Count && !_hair[a.HairStyle].Bald)
            {
                var entry = _hair[a.HairStyle];
                var mat = Make.Hair(a.HairColor);
                rag.RegisterCosmeticMaterial(mat);

                // Crown patch: a hair-coloured skin over the TOP of the head that hugs the head
                // sphere (built like the beard bib, upper hemisphere), UNDER the cards, so no bare
                // scalp peeks through the gaps between clumps. Because it's placed at the real head
                // radius (HeadR * girth) it scales with head size and follows the head silhouette,
                // instead of the old fixed floating sphere shell. Skipped for the Mohawk, which
                // bares the sides by design - a full crown skin would fill in the shaved scalp.
                if (entry.Name != "Mohawk")
                {
                    // Wear the HAIR shader (opaque variant) so the cap shares the hair's tint +
                    // anisotropic sheen instead of reading as a flat plastic dome.
                    var capMat = Make.HairCap(a.HairColor);
                    rag.RegisterCosmeticMaterial(capMat);
                    CrownPatch(head, capMat);
                }

                // Solid extra pieces for this style (e.g. the man bun's spheres), in hair colour.
                entry.Extra?.Invoke(head, mat);

                // Dynamic card strands, unless this style is solid-only (NoCards).
                if (!entry.NoCards)
                {
                    var go = new GameObject("HairSim");
                    go.transform.SetParent(head, false);
                    // ROTATION ONLY, never a translation. HairSim treats its anchor's ORIGIN as the
                    // head sphere's centre in four places (mesh-local space, the collision centre, the
                    // outward direction in WriteVerts, root placement), so moving the anchor off the
                    // skull breaks all four at once. Tilting it is free: it turns every style's root
                    // scatter and flow direction together, which is exactly the knob a mane needs.
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = mane ? Quaternion.Euler(ManeTiltDeg, 0f, 0f)
                                                      : Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    // Pass the body's real head radius rather than letting HairSim assume the human
                    // 0.19: on a horse that assumption floats every root ~0.04 m off the skull.
                    go.AddComponent<HairSim>().Build(head, entry.Def, mat, rag.HeadVisualRadius);
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
            // Facial hair (index 0 = clean-shaven -> nothing). Beard styles wear the textured
            // hair-card material (strand look + sheen); Stubble/Clean use a flat material.
            if (a.FacialStyle > 0 && a.FacialStyle < _facial.Count)
            {
                var fe = _facial[a.FacialStyle];
                var mat = fe.CardTexture ? Make.HairTuft(a.FacialColor) : Make.Mat(a.FacialColor, 0.2f);
                rag.RegisterCosmeticMaterial(mat);
                fe.Build(head, mat);
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

        // Adult-mode appendage: a collider-less Verlet pendulum under the pelvis (skin/coat-tinted,
        // build-scaled). Purely cosmetic; it pushes out of player bodies but never the ball.
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
            go.AddComponent<AnatomySim>().Build(rag, pelvis, a.Skin, def.AdultScale, def.AdultGrowth,
                                                a.MemberLen, a.MemberGirth, a.BallSize);
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

        // A curved polygon "bib" for facial hair, generated as a triangle grid that wraps the
        // lower face and hangs a little under the chin. Coordinates are the head sphere surface
        // pushed a few mm proud, so it reads as hair sitting ON the face rather than a helmet.
        // Style is driven by the wrap/length/thickness params, NOT by a sphere radius:
        //   thetaMax     half-angle (rad) the bib wraps around the front (bigger = reaches the
        //                jaw sides), phiTop..phiBot the vertical band (rad; 0 = ear/cheek level,
        //                negative = below, down under the chin),
        //   drop         extra downward length at the bottom (metres) for voluminous styles,
        //   bulge        extra outward thickness at the bottom (metres); ~0 = flush stubble,
        //   widenBottom  how much of the wrap-angle the lowest ring keeps (1 = full width,
        //                <1 tapers to a narrower chin).
        // Collider-less (MeshFilter + MeshRenderer only), parented to the head at unit scale.
        static void BeardMesh(Transform head, Material mat, float thetaMax, float phiTop,
                              float phiBot, float drop, float bulge, float widenBottom = 0.7f)
        {
            const int cols = 10, rows = 5;
            var verts = new Vector3[(cols + 1) * (rows + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];
            // Map the bib into ONE strand strip of the shared hair atlas so the HairTuft material
            // shows real strand texture: u spans the strip's width across the bib, v runs root->tip
            // top->bottom (atlas roots are at LOW v). A few U repeats so strands read fine, not one
            // giant clump. (No-op visually when a flat material is used, e.g. Stubble.)
            Vector2 strip = new Vector2(0.291f, 0.482f);   // one clump strip from HairSim.AtlasStripsU
            const float uRepeats = 3f, vRoot = 0.10f, vTip = 0.92f;
            for (int j = 0; j <= rows; j++)
            {
                float tv = j / (float)rows;                 // 0 top .. 1 bottom
                float phi = Mathf.Lerp(phiTop, phiBot, tv);
                float widen = Mathf.Lerp(1f, widenBottom, tv);
                float cphi = Mathf.Cos(phi), sphi = Mathf.Sin(phi);
                // Radius (and the downward hang) scale with head girth so beards track head size.
                float rr = (HeadR + Mathf.Lerp(0.008f, 0.008f + bulge, tv)) * _cosScale;
                for (int i = 0; i <= cols; i++)
                {
                    float tu = i / (float)cols;             // 0 left .. 1 right
                    float theta = Mathf.Lerp(-thetaMax, thetaMax, tu) * widen;
                    float x = rr * cphi * Mathf.Sin(theta);
                    float y = rr * sphi - drop * _cosScale * tv * tv;   // quadratic hang so the top stays flush
                    float z = rr * cphi * Mathf.Cos(theta);
                    int idx = j * (cols + 1) + i;
                    verts[idx] = new Vector3(x, y, z);
                    norms[idx] = new Vector3(cphi * Mathf.Sin(theta), sphi, cphi * Mathf.Cos(theta)).normalized;
                    float uu = Mathf.Repeat(tu * uRepeats, 1f);
                    uvs[idx] = new Vector2(Mathf.Lerp(strip.x, strip.y, uu), Mathf.Lerp(vRoot, vTip, 1f - tv));
                }
            }
            // Double-sided: emit each quad with both winding orders over the same verts. Whichever
            // order Unity treats as front-facing renders (with the outward vertex normals we set);
            // the reverse is back-face culled. Avoids depending on the cull convention (can't be
            // playtested here) and never z-fights, since only one order survives the cull per view.
            var tris = new int[cols * rows * 12];
            int t = 0;
            for (int j = 0; j < rows; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = j * (cols + 1) + i;
                int b = a + 1;
                int c = a + (cols + 1);
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            var go = new GameObject("cz");
            go.transform.SetParent(head, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            // Destroying the GameObject does NOT free a runtime-generated mesh (same as materials),
            // and the customize preview rebuilds the body repeatedly, so track it for teardown.
            go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
        }

        // A hair-coloured skin over the TOP of the head - the scalp base under the hair cards, so
        // no bare head shows through the gaps. A dome of triangles wrapping the upper hemisphere at
        // the real head radius (HeadR * girth) pushed a hair's breadth proud, so it hugs the head
        // and scales with head size (unlike the old fixed floating sphere). Rings of latitude from
        // the crown (phi = pi/2, the +Y pole) down to a lower band edge, sweeping the full circle.
        static void CrownPatch(Transform head, Material mat)
        {
            const int rings = 5, seg = 16;
            float rr = (HeadR + 0.006f) * _cosScale;    // just proud of the head surface, girth-scaled
            const float phiTop = Mathf.PI * 0.5f;       // +Y pole (top of head)
            const float phiBot = Mathf.PI * 0.5f - 1.05f; // ~down to just above ear level
            // Symmetric front-to-back: the cap comes down the same amount over the brow as at the
            // back (previously nudged back on z, which receded the front hairline).

            int cols = seg;
            var verts = new Vector3[(cols + 1) * (rings + 1)];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];   // crown UVs (only sampled if it ever wears a textured mat)
            for (int j = 0; j <= rings; j++)
            {
                float tv = j / (float)rings;                 // 0 top .. 1 bottom band
                float phi = Mathf.Lerp(phiTop, phiBot, tv);
                float cphi = Mathf.Cos(phi), sphi = Mathf.Sin(phi);
                for (int i = 0; i <= cols; i++)
                {
                    float theta = Mathf.Lerp(-Mathf.PI, Mathf.PI, i / (float)cols);
                    float x = rr * cphi * Mathf.Sin(theta);
                    float y = rr * sphi;
                    float z = rr * cphi * Mathf.Cos(theta);
                    int idx = j * (cols + 1) + i;
                    verts[idx] = new Vector3(x, y, z);
                    // The hair shader reads the vertex normal as the strand TANGENT for its
                    // anisotropic sheen, so point it along the MERIDIAN (crown -> down the head,
                    // d(pos)/d(phi)) - the way hair flows - rather than radially out. Gives the cap
                    // a hair-like highlight that runs down the head instead of a plastic dome.
                    norms[idx] = new Vector3(-sphi * Mathf.Sin(theta), cphi, -sphi * Mathf.Cos(theta)).normalized;
                    uvs[idx] = new Vector2(i / (float)cols, 1f - tv);
                }
            }
            // Double-sided (emit both windings; the culled one is free) - same trick as BeardMesh,
            // so we don't depend on the cull convention (can't be playtested here).
            var tris = new int[cols * rings * 12];
            int t = 0;
            for (int j = 0; j < rings; j++)
            for (int i = 0; i < cols; i++)
            {
                int a = j * (cols + 1) + i;
                int b = a + 1;
                int c = a + (cols + 1);
                int d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
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

        // ---- hair catalog (index 0 = Bald) ----------------------------------
        // Every non-bald style is a SOFT DYNAMIC HairSim (see HairSim.cs): a Verlet strand sim
        // rendered as textured HAIR CARDS - flat quad ribbons UV-mapped to a shared grayscale hair
        // atlas (Resources/Hair/HairAtlas.png), alpha-cutout so the strand detail comes from the
        // texture. A style is pure DATA. Stiffness holds a shaped style up (mohawk) while low
        // stiffness lets it fall and drape. Runs on every body that wears it.
        //
        // COST vs LOOK are decoupled: `strands` is the SIMULATED chain count (the only real cost),
        // `fan` multiplies RENDERED cards per strand for free (verts only), `staticToHead` skips the
        // sim for scalp caps. Coverage/fullness here comes mostly from high `fan`, not strand count.
        // def.thickness is CARD WIDTH. Long styles use TopSidesBack (crown + sides + back, face
        // clear) so hair sits ON TOP; a ponytail uses BackCluster (a tight gathered tie) + a
        // straight-down flow so it reads as one tail hanging down.
        static readonly List<HairEntry> _hair = new List<HairEntry>
        {
            new HairEntry { Name = "Bald", Group = HairGroup.Short, Bald = true },

            // SHORT (STATIC scalp caps - no sim; heavy fan for dense coverage, free) ------
            // Buzz: shortest possible flat stubble-cap, dense + very stiff, no wobble.
            new HairEntry { Name = "Buzz", Group = HairGroup.Short, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 130, nodes = 2, length = 0.035f, fan = 4, staticToHead = true,
                stiffness = 0.98f, flow = new Vector3(0f, 1f, 0f), curl = 0f, jitter = 0.35f, thickness = 0.05f } },
            // Crew Cut: a touch longer than buzz, slightly forward flat-top feel.
            new HairEntry { Name = "Crew Cut", Group = HairGroup.Short, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 130, nodes = 3, length = 0.075f, fan = 4, staticToHead = true,
                stiffness = 0.92f, flow = new Vector3(0f, 1f, 0.15f), curl = 0f, jitter = 0.2f, thickness = 0.05f } },
            // Spiky: gelled spikes shooting straight UP, very stiff, thin points, high scatter.
            new HairEntry { Name = "Spiky", Group = HairGroup.Short, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 95, nodes = 3, length = 0.17f, fan = 3, staticToHead = false,
                stiffness = 0.92f, flow = new Vector3(0f, 1f, 0f), curl = 0f, jitter = 0.55f, thickness = 0.035f } },
            // Fringe: a soft bang swept DOWN over the brow from a front hairline (FrontSweep root),
            // low stiffness so it hangs on the forehead. Distinct forward drape, not a ring cap.
            new HairEntry { Name = "Fringe", Group = HairGroup.Short, Def = new HairSim.HairDef {
                root = HairSim.RootMode.FrontSweep, strands = 70, nodes = 5, length = 0.19f, fan = 5, staticToHead = false,
                stiffness = 0.3f, flow = new Vector3(0f, -0.75f, 0.6f), curl = 0.012f, jitter = 0.15f, thickness = 0.055f } },
            // Mohawk: tall stiff midline crest (Strip), thin dense blades standing up.
            new HairEntry { Name = "Mohawk", Group = HairGroup.Medium, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Strip, strands = 34, nodes = 5, length = 0.26f, fan = 5, staticToHead = false,
                stiffness = 0.88f, flow = new Vector3(0f, 1f, 0f), curl = 0.008f, jitter = 0.08f, thickness = 0.05f } },

            // MEDIUM (fuller caps + some sway) -----------------------------------
            // Messy: short, low stiffness, MAX scatter - tufts poke every direction, little curl.
            new HairEntry { Name = "Messy", Group = HairGroup.Medium, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 95, nodes = 4, length = 0.16f, fan = 4, staticToHead = false,
                stiffness = 0.32f, flow = new Vector3(0f, 0.5f, 0f), curl = 0.02f, jitter = 0.7f, thickness = 0.05f } },
            // Wavy: long, LOOSE big waves (low-frequency curl over a long strand), soft drape,
            // low jitter so the waves read as coherent sheets, not frizz. Wider cards.
            new HairEntry { Name = "Wavy", Group = HairGroup.Medium, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 84, nodes = 6, length = 0.28f, fan = 5, staticToHead = false,
                stiffness = 0.22f, flow = new Vector3(0f, -0.4f, 0.1f), curl = 0.05f, jitter = 0.18f, thickness = 0.07f } },
            // Curly: TIGHT high-frequency curl on shorter strands, springy (mid stiffness so it
            // holds a round bounce instead of draping), high jitter for a packed coily look.
            new HairEntry { Name = "Curly", Group = HairGroup.Medium, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 110, nodes = 6, length = 0.17f, fan = 4, staticToHead = false,
                stiffness = 0.42f, flow = new Vector3(0f, 0.3f, 0f), curl = 0.085f, jitter = 0.5f, thickness = 0.05f } },
            // Afro: big round halo - up-and-out flow, max jitter, medium curl, thick cards, high
            // stiffness so it keeps the round dome rather than collapsing.
            new HairEntry { Name = "Afro", Group = HairGroup.Medium, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 130, nodes = 4, length = 0.19f, fan = 5, staticToHead = false,
                stiffness = 0.6f, flow = new Vector3(0f, 1f, 0f), curl = 0.055f, jitter = 0.7f, thickness = 0.08f } },

            // LONG (cover the TOP + sides + back, face clear; drape) -------------
            // Ponytail: one tight gathered tail hanging straight down the back, sleek (low curl/jitter).
            new HairEntry { Name = "Ponytail", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.BackCluster, strands = 42, nodes = 11, length = 0.52f, fan = 4, staticToHead = false,
                stiffness = 0.15f, flow = new Vector3(0f, -1f, -0.15f), curl = 0.01f, jitter = 0.05f, thickness = 0.05f } },
            // Man Bun: no card strands - just a solid sphere bun high on the back of the crown
            // (plus a thin tie), over the shared hair crown cap. Simplest and reads cleanly.
            new HairEntry { Name = "Man Bun", Group = HairGroup.Long, NoCards = true, Extra = (h,m) => {
                Ball(h, new Vector3(0f, 0.24f, -0.13f), new Vector3(0.20f, 0.20f, 0.20f), m);   // the bun
                Ball(h, new Vector3(0f, 0.21f, -0.19f), new Vector3(0.07f, 0.07f, 0.07f), m);   // tie wrap
            } },
            // Dreads: CHUNKY heavy ropes - few thick cards (big thickness, low fan so each reads as
            // one rope), barely any curl, high jitter so the ropes separate, semi-stiff so they hang
            // like weighted cords with minimal sway. The rope look, opposite of fine Long hair.
            new HairEntry { Name = "Dreads", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.TopSidesBack, strands = 40, nodes = 9, length = 0.48f, fan = 2, staticToHead = false,
                stiffness = 0.34f, flow = new Vector3(0f, -1f, -0.05f), curl = 0.004f, jitter = 0.55f, thickness = 0.09f } },
            // Shoulder Length: mid-length with body - between Long and Wavy, a bit of wave, slightly
            // stiffer than Long so it keeps some shape at the shoulders.
            new HairEntry { Name = "Shoulder Length", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.TopSidesBack, strands = 66, nodes = 10, length = 0.36f, fan = 5, staticToHead = false,
                stiffness = 0.18f, flow = new Vector3(0f, -1f, -0.05f), curl = 0.03f, jitter = 0.14f, thickness = 0.065f } },
            // Long: MANY fine floppy strands, thin cards, very low stiffness (drapes + sways freely),
            // low jitter for a sleek curtain. The fine/soft opposite of chunky Dreads.
            new HairEntry { Name = "Long", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.TopSidesBack, strands = 80, nodes = 12, length = 0.54f, fan = 6, staticToHead = false,
                stiffness = 0.07f, flow = new Vector3(0f, -1f, -0.06f), curl = 0.018f, jitter = 0.1f, thickness = 0.055f } },
        };

        // ---- facial hair catalog (index 0 = Clean-Shaven) -------------------
        static readonly List<FacialEntry> _facial = new List<FacialEntry>
        {
            new FacialEntry { Name = "Clean",     CardTexture = false, Build = (h,m) => { } },
            new FacialEntry { Name = "Mustache",  Build = (h,m) =>
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.14f, 0.03f, 0.05f), m) },
            new FacialEntry { Name = "Handlebar", Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.16f, 0.03f, 0.05f), m);
                Blk(h, new Vector3(-0.09f, -0.07f, 0.17f), new Vector3(0.03f, 0.05f, 0.04f), m);
                Blk(h, new Vector3(0.09f, -0.07f, 0.17f), new Vector3(0.03f, 0.05f, 0.04f), m); } },
            // Goatee: narrow chin tuft (flush polygon wedge) under a small mustache. No sphere.
            new FacialEntry { Name = "Goatee",    Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.10f, 0.025f, 0.045f), m);
                BeardMesh(h, m, thetaMax: 0.45f, phiTop: -0.55f, phiBot: -1.30f,
                          drop: 0.025f, bulge: 0.012f, widenBottom: 0.55f); } },
            // Stubble: dead-flush shell on chin + jaw (bulge/drop 0), no protrusion at all. Flat
            // material (CardTexture=false) - it's a shadow, not strands.
            new FacialEntry { Name = "Stubble",   CardTexture = false, Build = (h,m) =>
                BeardMesh(h, m, thetaMax: 1.25f, phiTop: -0.30f, phiBot: -1.20f,
                          drop: 0f, bulge: 0f, widenBottom: 0.80f) },
            // Short Beard: modest-volume polygon bib + mustache. Small hang, slight thickness.
            new FacialEntry { Name = "Short Beard", Build = (h,m) => {
                BeardMesh(h, m, thetaMax: 1.20f, phiTop: -0.28f, phiBot: -1.25f,
                          drop: 0.035f, bulge: 0.022f, widenBottom: 0.75f);
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.13f, 0.025f, 0.045f), m); } },
            // Full Beard: fuller polygon bib (more hang + thickness, reaches higher on the cheeks).
            new FacialEntry { Name = "Full Beard", Build = (h,m) => {
                BeardMesh(h, m, thetaMax: 1.35f, phiTop: -0.20f, phiBot: -1.30f,
                          drop: 0.075f, bulge: 0.042f, widenBottom: 0.85f);
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.15f, 0.03f, 0.045f), m); } },
            new FacialEntry { Name = "Sideburns", Build = (h,m) => {
                Blk(h, new Vector3(-0.16f, -0.02f, 0.06f), new Vector3(0.035f, 0.14f, 0.08f), m);
                Blk(h, new Vector3(0.16f, -0.02f, 0.06f), new Vector3(0.035f, 0.14f, 0.08f), m); } },
            // Chinstrap: thin flush strap along the jaw (polygon band, no volume) + side connectors.
            new FacialEntry { Name = "Chinstrap", Build = (h,m) => {
                Blk(h, new Vector3(-0.15f, -0.04f, 0.05f), new Vector3(0.035f, 0.13f, 0.08f), m);
                Blk(h, new Vector3(0.15f, -0.04f, 0.05f), new Vector3(0.035f, 0.13f, 0.08f), m);
                BeardMesh(h, m, thetaMax: 1.30f, phiTop: -0.72f, phiBot: -1.22f,
                          drop: 0f, bulge: 0f, widenBottom: 0.90f); } },
        };

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
            new AccessoryEntry { Name = "Round Glasses", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.085f, 0.02f, 0.185f), new Vector3(0.095f, 0.095f, 0.02f), m);    // left rim (very round)
                Ball(h, new Vector3(0.085f, 0.02f, 0.185f), new Vector3(0.095f, 0.095f, 0.02f), m);     // right rim
                Blk(h, new Vector3(-0.085f, 0.02f, 0.192f), new Vector3(0.07f, 0.07f, 0.012f), Glass());
                Blk(h, new Vector3(0.085f, 0.02f, 0.192f), new Vector3(0.07f, 0.07f, 0.012f), Glass());
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.03f, 0.012f, 0.018f), m);          // small round bridge
                Blk(h, new Vector3(-0.155f, 0.03f, 0.09f), new Vector3(0.02f, 0.018f, 0.18f), m);       // thin left arm
                Blk(h, new Vector3(0.155f, 0.03f, 0.09f), new Vector3(0.02f, 0.018f, 0.18f), m);        // thin right arm
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
                Ball(h, new Vector3(-0.09f, 0.035f, 0.185f), new Vector3(0.10f, 0.075f, 0.02f), m);     // left teardrop frame
                Blk(h, new Vector3(-0.09f, 0.02f, 0.192f), new Vector3(0.09f, 0.11f, 0.014f), Glass());  // taller teardrop lens
                Ball(h, new Vector3(0.09f, 0.035f, 0.185f), new Vector3(0.10f, 0.075f, 0.02f), m);      // right teardrop frame
                Blk(h, new Vector3(0.09f, 0.02f, 0.192f), new Vector3(0.09f, 0.11f, 0.014f), Glass());
                Blk(h, new Vector3(0f, 0.05f, 0.185f), new Vector3(0.06f, 0.015f, 0.015f), m);          // double-bridge top bar
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.04f, 0.012f, 0.015f), m);          // double-bridge lower bar
                Blk(h, new Vector3(-0.16f, 0.045f, 0.10f), new Vector3(0.02f, 0.015f, 0.16f), m);       // thin left arm
                Blk(h, new Vector3(0.16f, 0.045f, 0.10f), new Vector3(0.02f, 0.015f, 0.16f), m);        // thin right arm
            } },
            new AccessoryEntry { Name = "Wayfarers", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.085f, 0.02f, 0.185f), new Vector3(0.12f, 0.10f, 0.03f), m);       // thick left frame (trapezoid feel)
                Blk(h, new Vector3(-0.085f, 0.02f, 0.196f), new Vector3(0.09f, 0.07f, 0.014f), Dark());
                Blk(h, new Vector3(0.085f, 0.02f, 0.185f), new Vector3(0.12f, 0.10f, 0.03f), m);        // thick right frame
                Blk(h, new Vector3(0.085f, 0.02f, 0.196f), new Vector3(0.09f, 0.07f, 0.014f), Dark());
                Blk(h, new Vector3(0f, 0.06f, 0.185f), new Vector3(0.30f, 0.025f, 0.025f), m);          // heavy brow bar
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.05f, 0.025f, 0.025f), m);          // thick bridge
                Blk(h, new Vector3(-0.16f, 0.03f, 0.09f), new Vector3(0.035f, 0.025f, 0.18f), m);       // chunky left arm
                Blk(h, new Vector3(0.16f, 0.03f, 0.09f), new Vector3(0.035f, 0.025f, 0.18f), m);        // chunky right arm
            } },
            new AccessoryEntry { Name = "Rimless Glasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.08f, 0.02f, 0.188f), new Vector3(0.09f, 0.07f, 0.014f), Glass()); // left lens, no frame ring
                Blk(h, new Vector3(0.08f, 0.02f, 0.188f), new Vector3(0.09f, 0.07f, 0.014f), Glass());  // right lens, no frame ring
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.025f, 0.01f, 0.015f), m);          // tiny bridge stud
                Blk(h, new Vector3(-0.13f, 0.025f, 0.14f), new Vector3(0.015f, 0.012f, 0.12f), m);      // hairline left arm
                Blk(h, new Vector3(0.13f, 0.025f, 0.14f), new Vector3(0.015f, 0.012f, 0.12f), m);       // hairline right arm
            } },
            new AccessoryEntry { Name = "Sport Visor Shades", Headgear = false, Build = (h,m) => {
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
            new AccessoryEntry { Name = "3D Glasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.08f, 0.02f, 0.185f), new Vector3(0.11f, 0.085f, 0.025f), m);      // thick left frame
                Blk(h, new Vector3(0.08f, 0.02f, 0.185f), new Vector3(0.11f, 0.085f, 0.025f), m);       // thick right frame
                Blk(h, new Vector3(-0.08f, 0.02f, 0.195f), new Vector3(0.085f, 0.065f, 0.012f), m);     // red-ish lens (tinted)
                Blk(h, new Vector3(0.08f, 0.02f, 0.195f), new Vector3(0.085f, 0.065f, 0.012f), Dark()); // dark lens
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.05f, 0.02f, 0.02f), m);            // bridge
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);         // left arm
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);          // right arm
            } },
            new AccessoryEntry { Name = "Eyepatch", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.08f, 0.02f, 0.183f), new Vector3(0.115f, 0.135f, 0.012f), m);      // patch rim
                Blk(h, new Vector3(0.08f, 0.02f, 0.193f), new Vector3(0.10f, 0.12f, 0.018f), Dark());   // dark oval patch, right eye
                Blk(h, new Vector3(0f, 0.15f, 0.05f), new Vector3(0.02f, 0.02f, 0.42f), new Vector3(0f, 0f, 8f), m);   // strap over crown to left ear
                Blk(h, new Vector3(-0.17f, 0.05f, 0.02f), new Vector3(0.02f, 0.10f, 0.02f), m);         // strap drop to left ear
                Ball(h, new Vector3(-0.19f, 0.02f, 0f), new Vector3(0.03f, 0.03f, 0.03f), m);           // knot at ear
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
            new AccessoryEntry { Name = "Nerd Glasses", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.08f, 0.02f, 0.185f), new Vector3(0.10f, 0.10f, 0.025f), m);      // thick left round rim
                Ball(h, new Vector3(0.08f, 0.02f, 0.185f), new Vector3(0.10f, 0.10f, 0.025f), m);       // thick right round rim
                Blk(h, new Vector3(-0.08f, 0.02f, 0.193f), new Vector3(0.075f, 0.075f, 0.014f), Glass());
                Blk(h, new Vector3(0.08f, 0.02f, 0.193f), new Vector3(0.075f, 0.075f, 0.014f), Glass());
                Blk(h, new Vector3(0f, 0.02f, 0.19f), new Vector3(0.06f, 0.03f, 0.02f), m);             // bulky taped bridge
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);        // left arm
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.025f, 0.02f, 0.16f), m);         // right arm
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
            new AccessoryEntry { Name = "Domino Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.03f, 0.19f), new Vector3(0.28f, 0.09f, 0.02f), m);             // eye-region plate
                Blk(h, new Vector3(-0.08f, 0.03f, 0.197f), new Vector3(0.06f, 0.045f, 0.012f), Dark()); // left eye hole
                Blk(h, new Vector3(0.08f, 0.03f, 0.197f), new Vector3(0.06f, 0.045f, 0.012f), Dark());  // right eye hole
                Blk(h, new Vector3(-0.16f, 0.04f, 0.10f), new Vector3(0.015f, 0.02f, 0.16f), new Vector3(0f, 10f, 0f), m); // thin left strap
                Blk(h, new Vector3(0.16f, 0.04f, 0.10f), new Vector3(0.015f, 0.02f, 0.16f), new Vector3(0f, -10f, 0f), m); // thin right strap
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
            new AccessoryEntry { Name = "Gas Mask", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0f, -0.07f, 0.22f), new Vector3(0.10f, 0.10f, 0.10f), m);           // round front canister at mouth
                Blk(h, new Vector3(0f, -0.07f, 0.27f), new Vector3(0.05f, 0.05f, 0.04f), m);            // filter nub
                Blk(h, new Vector3(-0.08f, 0.03f, 0.185f), new Vector3(0.11f, 0.10f, 0.025f), m);       // left eye frame
                Blk(h, new Vector3(-0.08f, 0.03f, 0.193f), new Vector3(0.085f, 0.075f, 0.014f), Glass());
                Blk(h, new Vector3(0.08f, 0.03f, 0.185f), new Vector3(0.11f, 0.10f, 0.025f), m);        // right eye frame
                Blk(h, new Vector3(0.08f, 0.03f, 0.193f), new Vector3(0.085f, 0.075f, 0.014f), Glass());
                Blk(h, new Vector3(-0.17f, 0.03f, 0.05f), new Vector3(0.025f, 0.025f, 0.20f), new Vector3(0f, 20f, 0f), m);  // left strap
                Blk(h, new Vector3(0.17f, 0.03f, 0.05f), new Vector3(0.025f, 0.025f, 0.20f), new Vector3(0f, -20f, 0f), m);  // right strap
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
            new AccessoryEntry { Name = "Gauges", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.19f, 0.0f, 0.0f), new Vector3(0.03f, 0.03f, 0.018f), m);        // left lobe stretched rim
                Ball(h, new Vector3(-0.19f, 0.0f, 0.0f), new Vector3(0.018f, 0.018f, 0.02f), Dark());  // left gauge hollow
                Ball(h, new Vector3(0.19f, 0.0f, 0.0f), new Vector3(0.03f, 0.03f, 0.018f), m);         // right lobe stretched rim
                Ball(h, new Vector3(0.19f, 0.0f, 0.0f), new Vector3(0.018f, 0.018f, 0.02f), Dark());   // right gauge hollow
            } },
            new AccessoryEntry { Name = "Nose Ring", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.03f, -0.018f, 0.20f), new Vector3(0.005f, 0.005f, 0.012f), m);    // hoop top
                Blk(h, new Vector3(0.03f, -0.042f, 0.20f), new Vector3(0.005f, 0.005f, 0.012f), m);    // hoop bottom
                Blk(h, new Vector3(0.018f, -0.03f, 0.20f), new Vector3(0.012f, 0.005f, 0.005f), m);    // hoop left
                Blk(h, new Vector3(0.042f, -0.03f, 0.20f), new Vector3(0.012f, 0.005f, 0.005f), m);    // hoop right
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
            new AccessoryEntry { Name = "Lip Ring", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.03f, -0.078f, 0.185f), new Vector3(0.005f, 0.005f, 0.014f), m);   // hoop top
                Blk(h, new Vector3(0.03f, -0.102f, 0.185f), new Vector3(0.005f, 0.005f, 0.014f), m);   // hoop bottom
                Blk(h, new Vector3(0.018f, -0.09f, 0.185f), new Vector3(0.014f, 0.005f, 0.005f), m);   // hoop left
                Blk(h, new Vector3(0.042f, -0.09f, 0.185f), new Vector3(0.014f, 0.005f, 0.005f), m);   // hoop right
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
            new AccessoryEntry { Name = "Face Gem", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0.11f, -0.04f, 0.168f), new Vector3(0.012f, 0.012f, 0.008f), m);   // metal setting on the cheek
                Ball(h, new Vector3(0.11f, -0.04f, 0.174f), new Vector3(0.008f, 0.008f, 0.006f), Glass()); // faceted gem
            } },
            new AccessoryEntry { Name = "Beauty Mark", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(-0.11f, -0.03f, 0.172f), new Vector3(0.006f, 0.006f, 0.004f), Dark()); // tiny dot on the cheek
            } },
            new AccessoryEntry { Name = "Grill", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.0f, -0.07f, 0.185f), new Vector3(0.05f, 0.022f, 0.01f), m);       // base band across the front teeth
                Blk(h, new Vector3(-0.03f, -0.07f, 0.188f), new Vector3(0.012f, 0.02f, 0.01f), m);     // tooth
                Blk(h, new Vector3(-0.01f, -0.07f, 0.188f), new Vector3(0.012f, 0.02f, 0.01f), m);     // tooth
                Blk(h, new Vector3(0.01f, -0.07f, 0.188f), new Vector3(0.012f, 0.02f, 0.01f), m);      // tooth
                Blk(h, new Vector3(0.03f, -0.07f, 0.188f), new Vector3(0.012f, 0.02f, 0.01f), m);      // tooth
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
            new AccessoryEntry { Name = "Face Tattoo", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.10f, 0.00f, 0.178f), new Vector3(0.05f, 0.005f, 0.003f), new Vector3(0f, 0f, 20f), m);    // line marking, upper cheek
                Blk(h, new Vector3(-0.10f, -0.02f, 0.175f), new Vector3(0.045f, 0.005f, 0.003f), new Vector3(0f, 0f, -15f), m); // line marking, mid cheek
                Blk(h, new Vector3(-0.10f, -0.045f, 0.17f), new Vector3(0.035f, 0.005f, 0.003f), new Vector3(0f, 0f, 20f), m);  // line marking, lower cheek
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
            new AccessoryEntry { Name = "Snapback", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.46f, 0.22f, 0.46f), m);      // boxy flat crown
                Blk(h, new Vector3(0f, 0.11f, 0.24f), new Vector3(0.40f, 0.03f, 0.22f), m);       // wide flat bill
                Ball(h, new Vector3(0f, 0.28f, -0.01f), new Vector3(0.06f, 0.06f, 0.06f), m);     // top button
                Blk(h, new Vector3(0f, 0.06f, -0.19f), new Vector3(0.14f, 0.05f, 0.05f), Dark()); // back strap accent
            } },
            new AccessoryEntry { Name = "Beanie", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.46f, 0.32f, 0.46f), m);     // knit dome
                Blk(h, new Vector3(0f, 0.05f, 0f), new Vector3(0.48f, 0.07f, 0.48f), m);          // fold band
                Blk(h, new Vector3(0f, 0.03f, 0f), new Vector3(0.485f, 0.02f, 0.485f), Dark());   // ribbed cuff seam
                Blk(h, new Vector3(0f, 0.20f, 0.235f), new Vector3(0.06f, 0.10f, 0.02f), Dark());  // knit-brand tab
                Ball(h, new Vector3(0f, 0.34f, -0.01f), new Vector3(0.10f, 0.10f, 0.10f), m);     // pom
            } },
            new AccessoryEntry { Name = "Bucket Hat", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.44f, 0.24f, 0.44f), m);     // short crown
                Ball(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.66f, 0.06f, 0.66f), m);         // all-around sloped brim
                Blk(h, new Vector3(0f, 0.20f, 0f), new Vector3(0.45f, 0.02f, 0.45f), Dark());     // stitch line round the crown
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
                Blk(h, new Vector3(0.02f, 0.17f, -0.02f), new Vector3(0.52f, 0.10f, 0.50f), new Vector3(0f, 0f, 14f), m); // tilted disc crown
                Ball(h, new Vector3(0.02f, 0.24f, -0.02f), new Vector3(0.05f, 0.05f, 0.05f), m);  // tiny stalk
            } },
            new AccessoryEntry { Name = "Flat Cap", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.46f, 0.22f, 0.46f), m);     // low rounded crown
                Ball(h, new Vector3(0f, 0.15f, 0.10f), new Vector3(0.44f, 0.16f, 0.30f), m);      // front peak swept over the brim
                Blk(h, new Vector3(0f, 0.09f, 0.20f), new Vector3(0.30f, 0.03f, 0.12f), m);       // short stubby brim
                Blk(h, new Vector3(0f, 0.078f, 0.20f), new Vector3(0.28f, 0.01f, 0.11f), Dark()); // brim underside
                Ball(h, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.05f, 0.05f, 0.05f), m);     // top button
            } },
            new AccessoryEntry { Name = "Visor", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.08f, 0f), new Vector3(0.48f, 0.06f, 0.48f), m);          // headband ring
                Blk(h, new Vector3(0f, 0.09f, 0.22f), new Vector3(0.34f, 0.03f, 0.20f), m);       // forward brim
                Blk(h, new Vector3(0f, 0.076f, 0.22f), new Vector3(0.32f, 0.012f, 0.19f), Dark()); // shaded brim underside
                Blk(h, new Vector3(0f, 0.05f, -0.22f), new Vector3(0.10f, 0.04f, 0.03f), Dark());  // rear hook-and-loop closure
            } },
            new AccessoryEntry { Name = "Headband", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.46f, 0.05f, 0.46f), m);          // ring band
                Ball(h, new Vector3(0f, 0.10f, -0.22f), new Vector3(0.06f, 0.05f, 0.04f), m);     // back knot bump
            } },
            new AccessoryEntry { Name = "Sweatband", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.05f, 0f), new Vector3(0.50f, 0.10f, 0.50f), m);          // thick terry band
                Blk(h, new Vector3(0f, 0.05f, 0f), new Vector3(0.51f, 0.02f, 0.51f), Dark());     // stripe trim
            } },
            new AccessoryEntry { Name = "Bandana", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.15f, -0.01f), new Vector3(0.46f, 0.26f, 0.46f), m);     // draped cloth dome
                Ball(h, new Vector3(0f, 0.12f, -0.20f), new Vector3(0.08f, 0.08f, 0.08f), m);     // back knot
                Blk(h, new Vector3(0f, 0.06f, -0.24f), new Vector3(0.05f, 0.10f, 0.03f), m);      // hanging tail
            } },
            new AccessoryEntry { Name = "Durag", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.15f, -0.01f), new Vector3(0.44f, 0.26f, 0.44f), m);     // smooth crown cover
                Blk(h, new Vector3(-0.06f, 0.02f, -0.22f), new Vector3(0.05f, 0.14f, 0.03f), m);  // left tie tail
                Blk(h, new Vector3(0.06f, 0.02f, -0.22f), new Vector3(0.05f, 0.14f, 0.03f), m);   // right tie tail
            } },
            new AccessoryEntry { Name = "Hard Hat", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.17f, -0.01f), new Vector3(0.50f, 0.34f, 0.50f), m);     // domed shell
                Blk(h, new Vector3(0f, 0.09f, 0.20f), new Vector3(0.40f, 0.03f, 0.14f), m);       // forward brim ridge
                Blk(h, new Vector3(0f, 0.32f, 0f), new Vector3(0.06f, 0.04f, 0.50f), Dark());     // top ridge line
            } },
            new AccessoryEntry { Name = "Sombrero", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.20f, -0.01f), new Vector3(0.32f, 0.18f, 0.32f), m);     // small crown
                Blk(h, new Vector3(0f, 0.14f, 0f), new Vector3(0.34f, 0.03f, 0.34f), Dark());     // crown band
                Ball(h, new Vector3(0f, 0.12f, 0f), new Vector3(0.90f, 0.03f, 0.90f), m);         // very wide flat brim
            } },
            new AccessoryEntry { Name = "Party Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.09f, 0f), new Vector3(0.42f, 0.04f, 0.42f), m);          // base band
                Ball(h, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.32f, 0.16f, 0.32f), m);     // lower cone
                Ball(h, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.16f, 0.16f, 0.16f), m);     // upper cone taper
                Ball(h, new Vector3(0f, 0.40f, -0.01f), new Vector3(0.07f, 0.07f, 0.07f), Glass()); // tip gem
            } },
            new AccessoryEntry { Name = "Crown", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.46f, 0.08f, 0.46f), m);          // band ring
                Blk(h, new Vector3(0f, 0.22f, 0.17f), new Vector3(0.08f, 0.14f, 0.08f), m);       // center front point
                Blk(h, new Vector3(-0.13f, 0.20f, 0.13f), new Vector3(0.07f, 0.11f, 0.07f), m);   // left-front point
                Blk(h, new Vector3(0.13f, 0.20f, 0.13f), new Vector3(0.07f, 0.11f, 0.07f), m);    // right-front point
                Ball(h, new Vector3(0f, 0.14f, 0.19f), new Vector3(0.05f, 0.05f, 0.05f), Glass()); // front gem
            } },
            new AccessoryEntry { Name = "Santa Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.08f, 0f), new Vector3(0.48f, 0.07f, 0.48f), m);          // band
                Ball(h, new Vector3(0f, 0.18f, -0.01f), new Vector3(0.34f, 0.20f, 0.34f), m);     // cone base
                Ball(h, new Vector3(0f, 0.28f, -0.08f), new Vector3(0.22f, 0.18f, 0.22f), m);     // slumping cone
                Ball(h, new Vector3(0f, 0.34f, -0.16f), new Vector3(0.09f, 0.09f, 0.09f), m);     // drooping pom
            } },
            new AccessoryEntry { Name = "Chef Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.10f, -0.01f), new Vector3(0.40f, 0.10f, 0.40f), m);      // short stiff band
                Ball(h, new Vector3(0f, 0.26f, -0.01f), new Vector3(0.50f, 0.28f, 0.50f), m);     // puffy top
                Ball(h, new Vector3(0.10f, 0.30f, 0.05f), new Vector3(0.20f, 0.20f, 0.20f), m);   // pleat bump
                Ball(h, new Vector3(-0.10f, 0.30f, -0.05f), new Vector3(0.20f, 0.20f, 0.20f), m); // pleat bump
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
