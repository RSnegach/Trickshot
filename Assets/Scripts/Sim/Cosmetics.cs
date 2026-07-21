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

        // ---- catalog entry types --------------------------------------------
        public class HairEntry
        {
            public string Name; public HairGroup Group;
            public Action<Transform, Material> Build;   // builds pieces parented to the head
        }
        public class FacialEntry
        {
            public string Name;
            public Action<Transform, Material> Build;
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
            var head = rag.Phys(Bone.Head);
            if (head == null) return;

            // Hair (index 0 = bald -> nothing).
            if (a.HairStyle > 0 && a.HairStyle < _hair.Count)
            {
                var mat = Make.Mat(a.HairColor, 0.2f);
                rag.RegisterCosmeticMaterial(mat);
                _hair[a.HairStyle].Build(head, mat);
            }
            // Facial hair (index 0 = clean-shaven -> nothing).
            if (a.FacialStyle > 0 && a.FacialStyle < _facial.Count)
            {
                var mat = Make.Mat(a.FacialColor, 0.2f);
                rag.RegisterCosmeticMaterial(mat);
                _facial[a.FacialStyle].Build(head, mat);
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

        // ---- collider-less piece helpers ------------------------------------
        // A rounded shell (sphere) parented to the head; its primitive collider is destroyed.
        static void Ball(Transform head, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = Make.Sphere("cz", 1f, head.position, mat, head);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
        }

        // A flat/blocky piece (box) parented to the head; Make.Box(collider:false) skips the collider.
        static void Blk(Transform head, Vector3 localPos, Vector3 localScale, Material mat)
            => Blk(head, localPos, localScale, Vector3.zero, mat);

        static void Blk(Transform head, Vector3 localPos, Vector3 localScale, Vector3 euler, Material mat)
        {
            var go = Make.Box("cz", Vector3.one, head.position, mat, head, collider: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale;
        }

        // ---- hair catalog (index 0 = Bald) ----------------------------------
        // Each style aims for a DISTINCT silhouette, not just a resized sphere: flat-tops and
        // fringes use boxes, spikes use rotated boxes, long styles add side/back drapes, curly
        // uses a lumpy cluster. Head radius ~0.19 in local metres; +Z is the face, +Y up.
        static readonly List<HairEntry> _hair = new List<HairEntry>
        {
            new HairEntry { Name = "Bald",       Group = HairGroup.Short,  Build = (h,m) => { } },

            // SHORT ------------------------------------------------------------
            // Buzz: a very thin, tight cap hugging the scalp.
            new HairEntry { Name = "Buzz",       Group = HairGroup.Short,  Build = (h,m) =>
                Ball(h, new Vector3(0f, 0.05f, -0.02f), new Vector3(0.42f, 0.20f, 0.44f), m) },
            // Flat Top: a squared-off block on top with straight sides (clearly not a sphere).
            new HairEntry { Name = "Flat Top",   Group = HairGroup.Short,  Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.40f, 0.16f, 0.44f), m);   // flat slab on top
                Blk(h, new Vector3(-0.19f, 0.06f, -0.01f), new Vector3(0.05f, 0.16f, 0.42f), m);  // squared left side
                Blk(h, new Vector3(0.19f, 0.06f, -0.01f), new Vector3(0.05f, 0.16f, 0.42f), m); } },// squared right side
            // Side Part: cap with a raised, swept block on one side and a visible parted gap.
            new HairEntry { Name = "Side Part",  Group = HairGroup.Short,  Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.09f, -0.02f), new Vector3(0.44f, 0.30f, 0.44f), m);
                Blk(h, new Vector3(-0.08f, 0.17f, 0.06f), new Vector3(0.12f, 0.07f, 0.30f), new Vector3(0f,0f,10f), m);  // swept-over top
                Blk(h, new Vector3(0.01f, 0.19f, 0.06f), new Vector3(0.015f, 0.06f, 0.28f), m); } },  // the part line gap (thin)

            // MEDIUM -----------------------------------------------------------
            // Bowl: a dome with a straight, blunt fringe across the brow (the classic bowl cut).
            new HairEntry { Name = "Bowl",       Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.01f), new Vector3(0.50f, 0.38f, 0.50f), m);
                Blk(h, new Vector3(0f, 0.02f, 0.15f), new Vector3(0.46f, 0.10f, 0.10f), m); } },   // blunt front fringe
            // Quiff: cap plus a wedge lifted up-and-back at the front.
            new HairEntry { Name = "Quiff",      Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.03f), new Vector3(0.44f, 0.34f, 0.44f), m);
                Blk(h, new Vector3(0f, 0.20f, 0.10f), new Vector3(0.30f, 0.16f, 0.10f), new Vector3(-38f,0f,0f), m); } }, // front pompadour lifted back
            // Curly: a lumpy cluster of small balls all over the crown (bumpy, not smooth).
            new HairEntry { Name = "Curly",      Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.01f), new Vector3(0.46f, 0.36f, 0.46f), m);
                Ball(h, new Vector3(-0.15f, 0.10f, 0.02f), new Vector3(0.20f, 0.20f, 0.22f), m);
                Ball(h, new Vector3(0.15f, 0.10f, 0.02f),  new Vector3(0.20f, 0.20f, 0.22f), m);
                Ball(h, new Vector3(-0.09f, 0.20f, -0.04f), new Vector3(0.18f, 0.18f, 0.20f), m);
                Ball(h, new Vector3(0.09f, 0.20f, -0.04f),  new Vector3(0.18f, 0.18f, 0.20f), m);
                Ball(h, new Vector3(0f, 0.15f, -0.15f),     new Vector3(0.22f, 0.20f, 0.20f), m); } },
            // Spiky: several thin rotated boxes fanning up like gelled spikes.
            new HairEntry { Name = "Spiky",      Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.42f, 0.24f, 0.44f), m);
                Blk(h, new Vector3(0f, 0.22f, 0.02f), new Vector3(0.05f, 0.20f, 0.05f), m);
                Blk(h, new Vector3(-0.12f, 0.20f, 0.02f), new Vector3(0.05f, 0.18f, 0.05f), new Vector3(0f,0f,22f), m);
                Blk(h, new Vector3(0.12f, 0.20f, 0.02f), new Vector3(0.05f, 0.18f, 0.05f), new Vector3(0f,0f,-22f), m);
                Blk(h, new Vector3(0f, 0.20f, 0.14f), new Vector3(0.05f, 0.18f, 0.05f), new Vector3(24f,0f,0f), m);
                Blk(h, new Vector3(0f, 0.20f, -0.14f), new Vector3(0.05f, 0.18f, 0.05f), new Vector3(-24f,0f,0f), m); } },
            // Mohawk: a single tall central fin, shaved sides (no side coverage).
            new HairEntry { Name = "Mohawk",     Group = HairGroup.Medium, Build = (h,m) =>
                Blk(h, new Vector3(0f, 0.24f, 0f), new Vector3(0.07f, 0.30f, 0.46f), m) },

            // LONG -------------------------------------------------------------
            // Long: cap plus long side curtains past the ears and a panel down the back.
            new HairEntry { Name = "Long",       Group = HairGroup.Long,   Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.11f, -0.02f), new Vector3(0.48f, 0.38f, 0.48f), m);
                Blk(h, new Vector3(-0.18f, -0.10f, -0.02f), new Vector3(0.07f, 0.40f, 0.34f), m);   // left curtain
                Blk(h, new Vector3(0.18f, -0.10f, -0.02f), new Vector3(0.07f, 0.40f, 0.34f), m);    // right curtain
                Blk(h, new Vector3(0f, -0.14f, -0.17f), new Vector3(0.40f, 0.46f, 0.10f), m); } },  // back panel
            // Afro: a big rounded crown of hair. Raised so it sits ON the head (covers the
            // cranium) and clears the shoulders + face, instead of swallowing the head.
            new HairEntry { Name = "Afro",       Group = HairGroup.Long,   Build = (h,m) =>
                Ball(h, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.72f, 0.64f, 0.72f), m) },
            // Ponytail: sleek back-swept cap, a tie, and a long tail hanging down the back.
            new HairEntry { Name = "Ponytail",   Group = HairGroup.Long,   Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.03f), new Vector3(0.44f, 0.32f, 0.46f), m);        // pulled-back cap
                Ball(h, new Vector3(0f, 0.06f, -0.20f), new Vector3(0.13f, 0.13f, 0.16f), m);        // tie
                Blk(h, new Vector3(0f, -0.16f, -0.22f), new Vector3(0.09f, 0.42f, 0.09f), m); } },   // long tail down back
            // Man Bun: pulled-back cap with a bun knotted on the top-back.
            new HairEntry { Name = "Man Bun",    Group = HairGroup.Long,   Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.09f, -0.03f), new Vector3(0.44f, 0.30f, 0.46f), m);
                Ball(h, new Vector3(0f, 0.22f, -0.13f), new Vector3(0.17f, 0.17f, 0.17f), m); } },
        };

        // ---- facial hair catalog (index 0 = Clean-Shaven) -------------------
        static readonly List<FacialEntry> _facial = new List<FacialEntry>
        {
            new FacialEntry { Name = "Clean",     Build = (h,m) => { } },
            new FacialEntry { Name = "Mustache",  Build = (h,m) =>
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.14f, 0.03f, 0.05f), m) },
            new FacialEntry { Name = "Handlebar", Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.16f, 0.03f, 0.05f), m);
                Blk(h, new Vector3(-0.09f, -0.07f, 0.17f), new Vector3(0.03f, 0.05f, 0.04f), m);
                Blk(h, new Vector3(0.09f, -0.07f, 0.17f), new Vector3(0.03f, 0.05f, 0.04f), m); } },
            new FacialEntry { Name = "Goatee",    Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.10f, 0.03f, 0.05f), m);
                Ball(h, new Vector3(0f, -0.14f, 0.15f), new Vector3(0.12f, 0.12f, 0.10f), m); } },
            new FacialEntry { Name = "Stubble",   Build = (h,m) =>
                Ball(h, new Vector3(0f, -0.10f, 0.10f), new Vector3(0.36f, 0.20f, 0.30f), m) },
            new FacialEntry { Name = "Short Beard", Build = (h,m) => {
                Ball(h, new Vector3(0f, -0.12f, 0.09f), new Vector3(0.38f, 0.26f, 0.32f), m);
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.14f, 0.03f, 0.05f), m); } },
            new FacialEntry { Name = "Full Beard", Build = (h,m) => {
                Ball(h, new Vector3(0f, -0.14f, 0.07f), new Vector3(0.42f, 0.34f, 0.34f), m);
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.16f, 0.03f, 0.05f), m); } },
            new FacialEntry { Name = "Sideburns", Build = (h,m) => {
                Blk(h, new Vector3(-0.16f, -0.02f, 0.06f), new Vector3(0.04f, 0.16f, 0.10f), m);
                Blk(h, new Vector3(0.16f, -0.02f, 0.06f), new Vector3(0.04f, 0.16f, 0.10f), m); } },
            new FacialEntry { Name = "Chinstrap", Build = (h,m) => {
                Blk(h, new Vector3(-0.15f, -0.04f, 0.05f), new Vector3(0.04f, 0.14f, 0.10f), m);
                Blk(h, new Vector3(0.15f, -0.04f, 0.05f), new Vector3(0.04f, 0.14f, 0.10f), m);
                Ball(h, new Vector3(0f, -0.15f, 0.10f), new Vector3(0.34f, 0.14f, 0.24f), m); } },
        };

        // ---- accessory catalog (index 0 = None) -----------------------------
        static readonly List<AccessoryEntry> _accessories = new List<AccessoryEntry>
        {
            new AccessoryEntry { Name = "None", Headgear = false, Build = (h,m) => { } },

            new AccessoryEntry { Name = "Glasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(-0.08f, 0.02f, 0.185f), new Vector3(0.10f, 0.08f, 0.02f), m);   // left lens
                Blk(h, new Vector3(0.08f, 0.02f, 0.185f), new Vector3(0.10f, 0.08f, 0.02f), m);    // right lens
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.07f, 0.02f, 0.02f), m);       // bridge
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);    // left arm
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m); } },// right arm
            new AccessoryEntry { Name = "Sunglasses", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.02f, 0.185f), new Vector3(0.30f, 0.09f, 0.03f), m);        // single dark band
                Blk(h, new Vector3(-0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m);
                Blk(h, new Vector3(0.15f, 0.03f, 0.10f), new Vector3(0.03f, 0.02f, 0.16f), m); } },
            new AccessoryEntry { Name = "Monocle", Headgear = false, Build = (h,m) => {
                Ball(h, new Vector3(0.08f, 0.02f, 0.19f), new Vector3(0.11f, 0.11f, 0.03f), m);     // rim
                Ball(h, new Vector3(0.08f, 0.02f, 0.195f), new Vector3(0.07f, 0.07f, 0.02f), Glass()); } },
            new AccessoryEntry { Name = "Pipe", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0.06f, -0.07f, 0.24f), new Vector3(0.05f, 0.03f, 0.14f), m);     // stem forward from mouth
                Blk(h, new Vector3(0.06f, -0.02f, 0.31f), new Vector3(0.05f, 0.09f, 0.05f), m); } },// bowl up at the end
            new AccessoryEntry { Name = "Batman Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.06f, 0.155f), new Vector3(0.36f, 0.30f, 0.10f), m);        // cowl face plate (upper)
                Blk(h, new Vector3(-0.10f, 0.24f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(0f,0f,-12f), m); // ear
                Blk(h, new Vector3(0.10f, 0.24f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(0f,0f,12f), m);   // ear
                // Wide white eye slits + a wide dark mouth opening, sitting proud of the plate.
                Blk(h, new Vector3(-0.09f, 0.09f, 0.215f), new Vector3(0.15f, 0.05f, 0.02f), Glass()); // left eye (wide)
                Blk(h, new Vector3(0.09f, 0.09f, 0.215f), new Vector3(0.15f, 0.05f, 0.02f), Glass());  // right eye (wide)
                Blk(h, new Vector3(0f, -0.06f, 0.215f), new Vector3(0.22f, 0.05f, 0.02f), Dark()); } },// mouth (wide)
            new AccessoryEntry { Name = "Hockey Mask", Headgear = false, Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.01f, 0.17f), new Vector3(0.34f, 0.40f, 0.07f), m);        // face plate
                Blk(h, new Vector3(-0.07f, 0.04f, 0.205f), new Vector3(0.04f, 0.04f, 0.02f), Dark());// eye hole
                Blk(h, new Vector3(0.07f, 0.04f, 0.205f), new Vector3(0.04f, 0.04f, 0.02f), Dark());
                Blk(h, new Vector3(0f, -0.10f, 0.205f), new Vector3(0.03f, 0.03f, 0.02f), Dark()); } },// mouth hole

            // Headgear (only wearable when bald):
            new AccessoryEntry { Name = "Cap", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.44f, 0.28f, 0.44f), m);        // crown
                Blk(h, new Vector3(0f, 0.10f, 0.22f), new Vector3(0.34f, 0.04f, 0.20f), m); } },     // brim forward
            new AccessoryEntry { Name = "Top Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.16f, 0f), new Vector3(0.50f, 0.04f, 0.50f), m);             // brim
                Blk(h, new Vector3(0f, 0.30f, 0f), new Vector3(0.34f, 0.28f, 0.34f), m); } },        // tall crown
            new AccessoryEntry { Name = "Beanie", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.01f), new Vector3(0.46f, 0.38f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.02f, 0f), new Vector3(0.46f, 0.06f, 0.46f), m); } },        // fold band
            new AccessoryEntry { Name = "Headband", Headgear = false, Build = (h,m) =>
                Blk(h, new Vector3(0f, 0.12f, 0.02f), new Vector3(0.44f, 0.06f, 0.44f), m) },
        };

        // Small tint materials for mask details (independent of the accessory colour). Created
        // per call; the ragdoll can't track these, so keep them rare (only masks use them). They
        // are tiny and reclaimed on scene change.
        static Material Glass() => Make.Mat(new Color(0.6f, 0.8f, 0.95f, 1f), 0.6f);
        static Material Dark()  => Make.Mat(new Color(0.06f, 0.06f, 0.07f, 1f), 0.1f);
    }
}
