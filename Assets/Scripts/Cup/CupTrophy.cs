using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The cup itself (design 8.1): a lathe bowl on a stem and foot, two Torus(arcDeg 200) handles
    /// and a small bevelled plinth, combined into ONE mesh about 0.45 m tall, collider-less, in one
    /// shared gold material. Two ways to place it:
    ///  - <see cref="AttachToHand"/> parents it to the LEFT forearm (Bone.ForearmL, the arm every
    ///    curated podium emote leaves alone) with the hand offset (0, TrophyForearmY, 0) - the
    ///    AddGlove offset - BAKED into the mesh, exactly as cosmetics do it (Cosmetics.Piece puts
    ///    the child at local zero). The forearm's local -Y runs from the elbow to the hand, so the
    ///    mesh is flipped to grow along -Y: the base sits on the palm and the bowl stands up past
    ///    the hand when the arm is raised, which is the lift.
    ///  - <see cref="Standing"/> stands it free (on the dais for the hand-over cut): base at the
    ///    given point, +Y up.
    /// Teardown: the mesh is freed by the GeneratedMeshOwner the piece carries (so a parent body
    /// dying frees it too); the material is freed by <see cref="Destroy"/> when this trophy OWNS
    /// it, and additionally registered with the holder body (RegisterCosmeticMaterial) so a body
    /// torn down first frees it as well - both paths null-guard, so a double free is harmless.
    /// </summary>
    public sealed class CupTrophy
    {
        // ---- geometry (metres; the bowl profile is (radius, y) bottom to top, solid on the LEFT) --
        /// <summary>Plinth height; the bowl's foot stands on it.</summary>
        public const float PlinthHeight = 0.05f;
        public const float PlinthRadius = 0.105f;
        /// <summary>Bowl + stem + foot height above the plinth (total = PlinthHeight + BowlHeight = 0.45).</summary>
        public const float BowlHeight = 0.40f;
        /// <summary>Handles: major / tube radius of the 200-degree torus, and the ring centre on the bowl (bowl frame).</summary>
        public const float HandleMajor = 0.045f;
        public const float HandleTube = 0.009f;
        public const float HandleCentreY = 0.355f;
        /// <summary>The handle's ends are buried this far into the bowl wall (the wall is 0.02 thick there).</summary>
        public const float HandleBury = 0.009f;
        /// <summary>Segments: the bowl lathe, the handle ring / tube, the plinth.</summary>
        public const int BowlSegments = 28;
        public const int HandleRingSegments = 20;
        public const int HandleTubeSegments = 8;

        /// <summary>
        /// The bowl profile (radius, y), bottom to top, then back down the inside to the floor
        /// pole. Traversed upward with the solid on its left, so MeshGen.Lathe's outward normal
        /// (dy, -dr) faces out on the outside and IN (toward the axis) on the inside, and up on the
        /// rim's top face. Foot 0.075 wide, stem 0.022, a knop, then the bowl opening to a 0.117 rim
        /// at y 0.40 with a 0.02 wall - thick enough to bury a 0.009 handle tube inside it.
        /// </summary>
        public static readonly Vector2[] BowlProfile =
        {
            new Vector2(0.000f, 0.000f),   // bottom pole
            new Vector2(0.075f, 0.000f),   // foot edge (bottom face)
            new Vector2(0.075f, 0.012f),
            new Vector2(0.040f, 0.030f),   // foot rising into the stem
            new Vector2(0.024f, 0.060f),
            new Vector2(0.022f, 0.150f),   // stem
            new Vector2(0.045f, 0.185f),   // knop
            new Vector2(0.040f, 0.205f),
            new Vector2(0.060f, 0.225f),   // bowl base
            new Vector2(0.095f, 0.270f),
            new Vector2(0.108f, 0.300f),
            new Vector2(0.112f, 0.320f),
            new Vector2(0.115f, 0.350f),   // the near-vertical band the handles sit on
            new Vector2(0.117f, 0.400f),   // rim, outer
            new Vector2(0.097f, 0.400f),   // rim, inner (top face between the two)
            new Vector2(0.095f, 0.350f),
            new Vector2(0.090f, 0.300f),
            new Vector2(0.070f, 0.260f),
            new Vector2(0.040f, 0.240f),
            new Vector2(0.000f, 0.235f),   // bowl floor pole
        };

        /// <summary>The plinth profile: a short bevelled disc the foot stands on.</summary>
        public static readonly Vector2[] PlinthProfile =
        {
            new Vector2(0.000f, 0.000f),
            new Vector2(PlinthRadius, 0.000f),
            new Vector2(PlinthRadius, 0.035f),
            new Vector2(0.090f, PlinthHeight),
            new Vector2(0.000f, PlinthHeight),
        };

        /// <summary>The piece's GameObject (null once destroyed).</summary>
        public GameObject Go { get; private set; }
        /// <summary>The gold material in use (shared or owned).</summary>
        public Material Material { get; private set; }
        /// <summary>The body holding it (AttachToHand), else null.</summary>
        public ActiveRagdoll Holder { get; private set; }
        /// <summary>This trophy made its material and frees it in Destroy (false = a caller's shared gold).</summary>
        public bool OwnsMaterial { get; private set; }

        public bool Alive => Go != null;

        CupTrophy() { }

        /// <summary>The Cosmetics gold (the one material every podium prop shares): Make.Mat(0.85, 0.70, 0.30, smooth 0.85, metal 0.75).</summary>
        public static Material MakeGold() => Make.Mat(new Color(0.85f, 0.70f, 0.30f), 0.85f, 0.75f);

        /// <summary>
        /// The whole trophy as one mesh: base at y = 0, rim at y = PlinthHeight + BowlHeight (0.45),
        /// axis +Y, handles along +-X. The parts are combined (MeshGen.Combine destroys them). The
        /// caller owns the result.
        /// </summary>
        public static Mesh BuildMesh()
        {
            // Plinth (flat-shaded so the bevel reads as an edge) and the bowl (smooth), the bowl
            // lifted onto the plinth.
            var plinth = MeshGen.Lathe(PlinthProfile, BowlSegments, smooth: false);
            var bowl = MeshGen.Lathe(BowlProfile, BowlSegments, smooth: true);
            MeshGen.Transform(bowl, new Vector3(0f, PlinthHeight, 0f));

            // Handles. MeshGen.Torus lies flat about +Y with phi = 0 at (0, 0, R) sweeping toward +X.
            // A pre-rotation about the ring's own axis by alpha advances phi by alpha (Unity's
            // Euler(0, a, 0) maps (sin p, 0, cos p) to (sin(p + a), 0, cos(p + a))), then a 90-degree
            // tilt about X stands the ring up in the XY plane: (x, y, z) -> (x, -z, y), so a ring
            // point becomes (R sin p', -R cos p', 0) with p' = phi + alpha. A 200-degree arc from
            // p' = -10 to 190 is centred on p' = 90, i.e. +X: the right handle bulges out to +X and
            // its two ends land at x = -R sin 10 (0.0078 inboard of the ring centre), one 0.044
            // below and one 0.044 above the centre. Mirrored with alpha = 170 for the left handle.
            var right = HandleMesh(-10f, +1f);
            var left = HandleMesh(170f, -1f);

            return MeshGen.Combine(plinth, bowl, right, left);
        }

        /// <summary>
        /// The handle ring's centre x on the bowl (bowl frame). Desk-checked against BowlProfile:
        /// the arc's ends sit R sin 10 = 0.0078 inboard of the centre, at x = 0.1065, and
        /// R cos 10 = 0.0443 above and below HandleCentreY, i.e. at y 0.3107 and 0.3993. Outer wall
        /// there: 0.1101 (lower) and 0.1170 (upper); inner wall: 0.0911 and 0.0970. A 0.009 tube
        /// centred at 0.1065 spans 0.0975..0.1155 - inside the inner wall at both ends (margins
        /// 6.4 mm and 0.5 mm) and a 5 mm nub proud of the outer wall at the lower end, which is
        /// the joint. Nothing pokes into the bowl.
        /// </summary>
        public const float HandleCentreX = 0.1143f;

        /// <summary>One handle: the torus arc pre-rotated by alpha, stood upright, and placed on the bowl wall on `side` (+1 right, -1 left).</summary>
        static Mesh HandleMesh(float alphaDeg, float side)
        {
            var m = MeshGen.Torus(HandleMajor, HandleTube, HandleRingSegments, HandleTubeSegments, arcDeg: 200f, capEnds: true);
            var rot = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, alphaDeg, 0f);
            MeshGen.Transform(m, new Vector3(side * HandleCentreX, PlinthHeight + HandleCentreY, 0f), rot);
            return m;
        }

        /// <summary>
        /// Attach a trophy to the body's LEFT forearm, base on the palm (the AddGlove offset), the
        /// bowl growing away from the elbow. `gold` may be a shared material (not freed here); null
        /// makes and owns one. Null when the body has no left forearm.
        /// </summary>
        public static CupTrophy AttachToHand(ActiveRagdoll rag, Material gold = null)
        {
            if (rag == null) return null;
            var forearm = rag.Phys(Bone.ForearmL);
            if (forearm == null) return null;
            var t = new CupTrophy();
            t.Holder = rag;
            t.OwnsMaterial = gold == null;
            t.Material = gold ?? MakeGold();
            var mesh = BuildMesh();
            // Flip +Y to -Y (a half-turn about X: (x, y, z) -> (x, -y, -z); the handles are on +-X and
            // unaffected) and drop the base to the hand point. Rim ends up at y = -0.22 - 0.45.
            MeshGen.Transform(mesh, new Vector3(0f, CupTuning.TrophyForearmY, 0f), Quaternion.Euler(180f, 0f, 0f));
            t.Go = Cosmetics.Piece(forearm, mesh, t.Material);
            t.Go.name = "CupTrophy";
            // A body torn down before this trophy frees the material with its other cosmetics.
            if (t.OwnsMaterial) rag.RegisterCosmeticMaterial(t.Material);
            return t;
        }

        /// <summary>A free-standing trophy: base at `worldPos`, +Y up, under `parent` (null = scene root).</summary>
        public static CupTrophy Standing(Transform parent, Vector3 worldPos, Quaternion rot, Material gold = null)
        {
            var t = new CupTrophy();
            t.OwnsMaterial = gold == null;
            t.Material = gold ?? MakeGold();
            var mesh = BuildMesh();
            // A holder at the spot with the piece (mesh at its own origin) under it: destroying the
            // holder cascades to the piece, whose GeneratedMeshOwner frees the mesh.
            var holder = new GameObject("CupTrophyStand");
            if (parent != null) holder.transform.SetParent(parent, false);
            holder.transform.position = worldPos;
            holder.transform.rotation = rot;
            var piece = Cosmetics.Piece(holder.transform, mesh, t.Material);
            piece.name = "CupTrophy";
            t.Go = holder;
            return t;
        }

        /// <summary>Move a standing trophy (no-op for a hand-held one).</summary>
        public void Place(Vector3 worldPos, Quaternion rot)
        {
            if (Go == null || Holder != null) return;
            Go.transform.position = worldPos;
            Go.transform.rotation = rot;
        }

        /// <summary>Show / hide (renderers only; nothing else changes).</summary>
        public void SetVisible(bool on)
        {
            if (Go == null) return;
            var rs = Go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++) rs[i].enabled = on;
        }

        /// <summary>Destroy the piece (its GeneratedMeshOwner frees the mesh) and the material when owned.</summary>
        public void Destroy()
        {
            if (Go != null) Object.Destroy(Go);
            Go = null;
            if (OwnsMaterial && Material != null) Object.Destroy(Material);
            Material = null;
            Holder = null;
        }
    }
}
