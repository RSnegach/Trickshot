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
            for (int j = 0; j <= rows; j++)
            {
                float tv = j / (float)rows;                 // 0 top .. 1 bottom
                float phi = Mathf.Lerp(phiTop, phiBot, tv);
                float widen = Mathf.Lerp(1f, widenBottom, tv);
                float cphi = Mathf.Cos(phi), sphi = Mathf.Sin(phi);
                float rr = HeadR + Mathf.Lerp(0.008f, 0.008f + bulge, tv);
                for (int i = 0; i <= cols; i++)
                {
                    float tu = i / (float)cols;             // 0 left .. 1 right
                    float theta = Mathf.Lerp(-thetaMax, thetaMax, tu) * widen;
                    float x = rr * cphi * Mathf.Sin(theta);
                    float y = rr * sphi - drop * tv * tv;   // quadratic hang so the top stays flush
                    float z = rr * cphi * Mathf.Cos(theta);
                    int idx = j * (cols + 1) + i;
                    verts[idx] = new Vector3(x, y, z);
                    norms[idx] = new Vector3(cphi * Mathf.Sin(theta), sphi, cphi * Mathf.Cos(theta)).normalized;
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

        // ---- hair catalog (index 0 = Bald) ----------------------------------
        // Each style aims for a DISTINCT silhouette, not just a resized sphere: flat-tops and
        // fringes use boxes, spikes use rotated boxes, long styles add side/back drapes, curly
        // uses a lumpy cluster. Head radius ~0.19 in local metres; +Z is the face, +Y up.
        static readonly List<HairEntry> _hair = new List<HairEntry>
        {
            new HairEntry { Name = "Bald",       Group = HairGroup.Short,  Build = (h,m) => { } },

            // SHORT -------------------------------------------------------------
            new HairEntry { Name = "Buzz", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.20f, 0.46f), m);
                Blk(h, new Vector3(0f, -0.02f, -0.16f), new Vector3(0.30f, 0.06f, 0.05f), m);
                Blk(h, new Vector3(0.19f, 0.01f, -0.02f), new Vector3(0.04f, 0.14f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, 0.01f, -0.02f), new Vector3(0.04f, 0.14f, 0.30f), m);
            } },
            new HairEntry { Name = "Crew Cut", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.24f, 0.46f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.20f, 0.34f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.20f, 0.34f), m);
                Blk(h, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.36f, 0.05f, 0.05f), m);
            } },
            new HairEntry { Name = "Caesar", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.01f, 0.14f), new Vector3(0.42f, 0.05f, 0.06f), m);
                Blk(h, new Vector3(0.19f, 0f, -0.02f), new Vector3(0.04f, 0.16f, 0.32f), m);
                Blk(h, new Vector3(-0.19f, 0f, -0.02f), new Vector3(0.04f, 0.16f, 0.32f), m);
            } },
            new HairEntry { Name = "High Fade", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.10f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.34f, 0.16f, 0.40f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.02f, 0.10f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.02f, 0.10f, 0.30f), m);
            } },
            new HairEntry { Name = "Low Fade", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.10f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.11f, -0.02f), new Vector3(0.36f, 0.10f, 0.42f), m);
                Blk(h, new Vector3(0.19f, -0.03f, -0.02f), new Vector3(0.03f, 0.14f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.03f, -0.02f), new Vector3(0.03f, 0.14f, 0.30f), m);
            } },
            new HairEntry { Name = "Burr", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.08f, 0.46f), m);
                Blk(h, new Vector3(0f, -0.04f, -0.15f), new Vector3(0.20f, 0.03f, 0.04f), m);
            } },
            new HairEntry { Name = "Comb Over", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.20f, 0.46f), m);
                Blk(h, new Vector3(0.05f, 0.14f, 0.02f), new Vector3(0.30f, 0.06f, 0.30f), new Vector3(0f, 0f, 20f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(0.19f, -0.02f, -0.02f), new Vector3(0.03f, 0.12f, 0.28f), m);
            } },
            new HairEntry { Name = "Ivy League", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0.04f, 0.13f, 0.02f), new Vector3(0.26f, 0.08f, 0.28f), new Vector3(0f, 0f, 10f), m);
                Blk(h, new Vector3(-0.06f, 0.13f, 0.04f), new Vector3(0.02f, 0.05f, 0.20f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
            } },
            new HairEntry { Name = "Butch", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.08f, -0.02f), new Vector3(0.44f, 0.24f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.15f, -0.02f), new Vector3(0.38f, 0.08f, 0.40f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.18f, 0.32f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.18f, 0.32f), m);
            } },
            new HairEntry { Name = "Textured Crop", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.20f, 0.46f), m);
                Ball(h, new Vector3(0.08f, 0.17f, 0.02f), new Vector3(0.10f, 0.08f, 0.10f), m);
                Ball(h, new Vector3(-0.06f, 0.18f, -0.03f), new Vector3(0.09f, 0.09f, 0.09f), m);
                Ball(h, new Vector3(0f, 0.19f, -0.08f), new Vector3(0.11f, 0.08f, 0.10f), m);
                Ball(h, new Vector3(0.10f, 0.16f, -0.09f), new Vector3(0.09f, 0.07f, 0.09f), m);
                Ball(h, new Vector3(-0.10f, 0.16f, 0f), new Vector3(0.10f, 0.08f, 0.09f), m);
            } },
            new HairEntry { Name = "French Crop", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.03f, 0.13f), new Vector3(0.22f, 0.05f, 0.06f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
            } },
            new HairEntry { Name = "Flat Top", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.12f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.16f, -0.02f), new Vector3(0.38f, 0.10f, 0.40f), m);
                Blk(h, new Vector3(0.19f, 0f, -0.02f), new Vector3(0.04f, 0.20f, 0.34f), m);
                Blk(h, new Vector3(-0.19f, 0f, -0.02f), new Vector3(0.04f, 0.20f, 0.34f), m);
            } },
            new HairEntry { Name = "Side Part", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.24f, 0.46f), m);
                Blk(h, new Vector3(0.05f, 0.15f, 0.01f), new Vector3(0.30f, 0.10f, 0.32f), new Vector3(0f, 0f, 15f), m);
                Blk(h, new Vector3(-0.05f, 0.15f, 0.03f), new Vector3(0.02f, 0.06f, 0.22f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.18f, 0.32f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.05f, 0.18f, 0.32f), m);
            } },
            new HairEntry { Name = "Hard Part", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0.05f, 0.15f, 0.01f), new Vector3(0.28f, 0.08f, 0.30f), new Vector3(0f, 0f, 8f), m);
                Blk(h, new Vector3(-0.07f, 0.16f, 0.05f), new Vector3(0.015f, 0.04f, 0.16f), m);
                Blk(h, new Vector3(0.19f, -0.02f, -0.02f), new Vector3(0.03f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.02f, -0.02f), new Vector3(0.03f, 0.16f, 0.30f), m);
            } },
            new HairEntry { Name = "Undercut", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.10f, 0.46f), m);
                Ball(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.36f, 0.22f, 0.38f), m);
                Blk(h, new Vector3(0.19f, -0.02f, -0.02f), new Vector3(0.02f, 0.12f, 0.28f), m);
                Blk(h, new Vector3(-0.19f, -0.02f, -0.02f), new Vector3(0.02f, 0.12f, 0.28f), m);
            } },
            new HairEntry { Name = "Slick Back", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.12f, -0.06f), new Vector3(0.34f, 0.08f, 0.36f), new Vector3(-10f, 0f, 0f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
            } },
            new HairEntry { Name = "Spiky", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.42f, 0.14f, 0.44f), m);
                Blk(h, new Vector3(0.10f, 0.16f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(15f, 0f, 20f), m);
                Blk(h, new Vector3(0.05f, 0.18f, -0.03f), new Vector3(0.05f, 0.15f, 0.05f), new Vector3(10f, 0f, 8f), m);
                Blk(h, new Vector3(0f, 0.19f, -0.08f), new Vector3(0.05f, 0.16f, 0.05f), new Vector3(5f, 0f, 0f), m);
                Blk(h, new Vector3(-0.05f, 0.18f, -0.03f), new Vector3(0.05f, 0.15f, 0.05f), new Vector3(10f, 0f, -8f), m);
                Blk(h, new Vector3(-0.10f, 0.16f, 0.02f), new Vector3(0.05f, 0.14f, 0.05f), new Vector3(15f, 0f, -20f), m);
            } },
            new HairEntry { Name = "Faux Buzz Fade", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.06f, -0.02f), new Vector3(0.44f, 0.08f, 0.46f), m);
                Blk(h, new Vector3(0.19f, 0.02f, -0.02f), new Vector3(0.03f, 0.10f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, 0.02f, -0.02f), new Vector3(0.03f, 0.10f, 0.30f), m);
                Blk(h, new Vector3(0.20f, -0.05f, -0.02f), new Vector3(0.015f, 0.08f, 0.24f), m);
                Blk(h, new Vector3(-0.20f, -0.05f, -0.02f), new Vector3(0.015f, 0.08f, 0.24f), m);
            } },
            new HairEntry { Name = "Brush Cut", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.18f, 0.46f), m);
                Blk(h, new Vector3(0.08f, 0.15f, -0.02f), new Vector3(0.06f, 0.10f, 0.30f), new Vector3(5f, 0f, 0f), m);
                Blk(h, new Vector3(0f, 0.16f, -0.02f), new Vector3(0.06f, 0.11f, 0.30f), m);
                Blk(h, new Vector3(-0.08f, 0.15f, -0.02f), new Vector3(0.06f, 0.10f, 0.30f), new Vector3(-5f, 0f, 0f), m);
            } },
            new HairEntry { Name = "Widow's Peak", Group = HairGroup.Short, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.07f, -0.02f), new Vector3(0.44f, 0.22f, 0.46f), m);
                Blk(h, new Vector3(0f, 0.02f, 0.14f), new Vector3(0.08f, 0.04f, 0.06f), new Vector3(20f, 0f, 0f), m);
                Blk(h, new Vector3(0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
                Blk(h, new Vector3(-0.19f, -0.01f, -0.02f), new Vector3(0.04f, 0.16f, 0.30f), m);
            } },

            // MEDIUM ------------------------------------------------------------
            new HairEntry { Name = "Bowl", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.44f, 0.32f, 0.44f), m);
                Blk(h, new Vector3(0f, 0.05f, 0.13f), new Vector3(0.36f, 0.10f, 0.09f), new Vector3(12f,0f,0f), m);
                Blk(h, new Vector3(-0.19f, 0.06f, 0.04f), new Vector3(0.10f, 0.14f, 0.20f), m);
                Blk(h, new Vector3(0.19f, 0.06f, 0.04f), new Vector3(0.10f, 0.14f, 0.20f), m);
                Blk(h, new Vector3(0f, 0.05f, -0.15f), new Vector3(0.34f, 0.10f, 0.10f), m);
            } },
            new HairEntry { Name = "Quiff", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.22f, 0.08f), new Vector3(0.28f, 0.18f, 0.14f), new Vector3(-30f,0f,0f), m);
                Ball(h, new Vector3(-0.15f, 0.10f, 0.00f), new Vector3(0.16f, 0.14f, 0.16f), m);
                Ball(h, new Vector3(0.15f, 0.10f, 0.00f), new Vector3(0.16f, 0.14f, 0.16f), m);
                Blk(h, new Vector3(0f, 0.10f, -0.14f), new Vector3(0.30f, 0.16f, 0.12f), m);
            } },
            new HairEntry { Name = "Curly", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.01f), new Vector3(0.42f, 0.26f, 0.42f), m);
                Ball(h, new Vector3(-0.16f, 0.18f, 0.05f), new Vector3(0.16f, 0.16f, 0.16f), m);
                Ball(h, new Vector3(0.16f, 0.18f, 0.05f), new Vector3(0.16f, 0.16f, 0.16f), m);
                Ball(h, new Vector3(-0.12f, 0.22f, -0.06f), new Vector3(0.15f, 0.15f, 0.15f), m);
                Ball(h, new Vector3(0.12f, 0.22f, -0.06f), new Vector3(0.15f, 0.15f, 0.15f), m);
                Ball(h, new Vector3(0f, 0.24f, 0.02f), new Vector3(0.17f, 0.17f, 0.17f), m);
                Ball(h, new Vector3(0f, 0.14f, -0.15f), new Vector3(0.20f, 0.18f, 0.18f), m);
            } },
            new HairEntry { Name = "Spiky Top", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.24f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.22f, 0.06f), new Vector3(0.09f, 0.20f, 0.09f), new Vector3(-20f,0f,0f), m);
                Blk(h, new Vector3(-0.10f, 0.22f, 0.02f), new Vector3(0.09f, 0.20f, 0.09f), new Vector3(-10f,25f,0f), m);
                Blk(h, new Vector3(0.10f, 0.22f, 0.02f), new Vector3(0.09f, 0.20f, 0.09f), new Vector3(-10f,-25f,0f), m);
                Blk(h, new Vector3(-0.09f, 0.22f, -0.08f), new Vector3(0.09f, 0.20f, 0.09f), new Vector3(10f,20f,0f), m);
                Blk(h, new Vector3(0.09f, 0.22f, -0.08f), new Vector3(0.09f, 0.20f, 0.09f), new Vector3(10f,-20f,0f), m);
            } },
            new HairEntry { Name = "Mohawk", Group = HairGroup.Medium, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.08f, 0.10f, 0.28f), m);
                Blk(h, new Vector3(0f, 0.24f, 0.05f), new Vector3(0.07f, 0.16f, 0.08f), m);
                Blk(h, new Vector3(0f, 0.27f, -0.02f), new Vector3(0.07f, 0.20f, 0.10f), m);
                Blk(h, new Vector3(0f, 0.24f, -0.10f), new Vector3(0.07f, 0.16f, 0.08f), m);
                Blk(h, new Vector3(0f, 0.30f, 0.07f), new Vector3(0.06f, 0.10f, 0.08f), new Vector3(-15f,0f,0f), m);
            } },
            new HairEntry { Name = "Pompadour", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.11f, -0.03f), new Vector3(0.44f, 0.30f, 0.44f), m);
                Blk(h, new Vector3(0f, 0.21f, 0.10f), new Vector3(0.32f, 0.18f, 0.12f), new Vector3(-38f,0f,0f), m);
                Ball(h, new Vector3(0f, 0.26f, 0.04f), new Vector3(0.20f, 0.14f, 0.18f), m);
                Ball(h, new Vector3(-0.16f, 0.10f, 0.02f), new Vector3(0.14f, 0.12f, 0.16f), m);
                Ball(h, new Vector3(0.16f, 0.10f, 0.02f), new Vector3(0.14f, 0.12f, 0.16f), m);
                Blk(h, new Vector3(0f, 0.09f, -0.14f), new Vector3(0.30f, 0.14f, 0.12f), m);
            } },
            new HairEntry { Name = "Faux Hawk", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.02f), new Vector3(0.42f, 0.20f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.20f, 0.06f), new Vector3(0.10f, 0.14f, 0.12f), m);
                Blk(h, new Vector3(0f, 0.22f, -0.03f), new Vector3(0.10f, 0.16f, 0.12f), m);
                Blk(h, new Vector3(0f, 0.19f, -0.11f), new Vector3(0.10f, 0.13f, 0.12f), m);
                Ball(h, new Vector3(-0.15f, 0.08f, 0.00f), new Vector3(0.14f, 0.10f, 0.16f), m);
                Ball(h, new Vector3(0.15f, 0.08f, 0.00f), new Vector3(0.14f, 0.10f, 0.16f), m);
            } },
            new HairEntry { Name = "Wavy", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);
                Ball(h, new Vector3(-0.17f, 0.14f, 0.06f), new Vector3(0.14f, 0.10f, 0.14f), m);
                Ball(h, new Vector3(0.16f, 0.11f, 0.04f), new Vector3(0.14f, 0.10f, 0.14f), m);
                Ball(h, new Vector3(-0.14f, 0.06f, -0.05f), new Vector3(0.13f, 0.09f, 0.13f), m);
                Ball(h, new Vector3(0.15f, 0.04f, -0.08f), new Vector3(0.13f, 0.09f, 0.13f), m);
                Ball(h, new Vector3(0f, 0.08f, -0.15f), new Vector3(0.20f, 0.12f, 0.16f), m);
            } },
            new HairEntry { Name = "Curtains", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.15f, -0.02f), new Vector3(0.40f, 0.20f, 0.40f), m);
                Blk(h, new Vector3(-0.14f, 0.14f, 0.09f), new Vector3(0.18f, 0.14f, 0.14f), new Vector3(0f,0f,20f), m);
                Blk(h, new Vector3(0.14f, 0.14f, 0.09f), new Vector3(0.18f, 0.14f, 0.14f), new Vector3(0f,0f,-20f), m);
                Ball(h, new Vector3(-0.17f, 0.08f, 0.00f), new Vector3(0.14f, 0.12f, 0.16f), m);
                Ball(h, new Vector3(0.17f, 0.08f, 0.00f), new Vector3(0.14f, 0.12f, 0.16f), m);
            } },
            new HairEntry { Name = "Messy", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.40f, 0.22f, 0.40f), m);
                Ball(h, new Vector3(-0.14f, 0.20f, 0.06f), new Vector3(0.13f, 0.13f, 0.13f), m);
                Ball(h, new Vector3(0.12f, 0.23f, -0.02f), new Vector3(0.14f, 0.12f, 0.14f), m);
                Ball(h, new Vector3(0.02f, 0.19f, 0.10f), new Vector3(0.12f, 0.11f, 0.12f), m);
                Ball(h, new Vector3(-0.08f, 0.24f, -0.09f), new Vector3(0.13f, 0.14f, 0.13f), m);
                Ball(h, new Vector3(0.16f, 0.13f, -0.10f), new Vector3(0.12f, 0.11f, 0.12f), m);
                Ball(h, new Vector3(-0.17f, 0.09f, -0.03f), new Vector3(0.11f, 0.10f, 0.11f), m);
            } },
            new HairEntry { Name = "Textured Fringe", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.04f, 0.14f), new Vector3(0.34f, 0.08f, 0.10f), new Vector3(18f,0f,0f), m);
                Ball(h, new Vector3(-0.10f, 0.20f, 0.08f), new Vector3(0.11f, 0.10f, 0.11f), m);
                Ball(h, new Vector3(0.10f, 0.20f, 0.08f), new Vector3(0.11f, 0.10f, 0.11f), m);
                Ball(h, new Vector3(0f, 0.23f, 0.00f), new Vector3(0.12f, 0.11f, 0.12f), m);
                Blk(h, new Vector3(0f, 0.08f, -0.13f), new Vector3(0.30f, 0.12f, 0.10f), m);
            } },
            new HairEntry { Name = "Side Swept", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);
                Blk(h, new Vector3(0.05f, 0.10f, 0.11f), new Vector3(0.36f, 0.10f, 0.10f), new Vector3(10f,0f,-22f), m);
                Ball(h, new Vector3(0.17f, 0.10f, 0.02f), new Vector3(0.16f, 0.14f, 0.18f), m);
                Ball(h, new Vector3(-0.15f, 0.10f, 0.00f), new Vector3(0.12f, 0.10f, 0.14f), m);
                Blk(h, new Vector3(0f, 0.08f, -0.14f), new Vector3(0.30f, 0.12f, 0.10f), m);
            } },
            new HairEntry { Name = "Taper Waves", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.42f, 0.24f, 0.42f), m);
                Ball(h, new Vector3(-0.15f, 0.16f, 0.05f), new Vector3(0.13f, 0.09f, 0.13f), m);
                Ball(h, new Vector3(0.14f, 0.13f, 0.03f), new Vector3(0.12f, 0.08f, 0.12f), m);
                Ball(h, new Vector3(-0.10f, 0.08f, -0.06f), new Vector3(0.09f, 0.06f, 0.09f), m);
                Ball(h, new Vector3(0.09f, 0.06f, -0.08f), new Vector3(0.08f, 0.05f, 0.08f), m);
                Blk(h, new Vector3(0f, 0.07f, -0.15f), new Vector3(0.28f, 0.10f, 0.10f), m);
            } },
            new HairEntry { Name = "Corkscrew Curls", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.40f, 0.24f, 0.40f), m);
                Ball(h, new Vector3(-0.16f, 0.16f, 0.06f), new Vector3(0.10f, 0.13f, 0.10f), m);
                Ball(h, new Vector3(0.15f, 0.15f, 0.05f), new Vector3(0.10f, 0.13f, 0.10f), m);
                Ball(h, new Vector3(-0.13f, 0.06f, 0.02f), new Vector3(0.09f, 0.12f, 0.09f), m);
                Ball(h, new Vector3(0.13f, 0.05f, 0.00f), new Vector3(0.09f, 0.12f, 0.09f), m);
                Ball(h, new Vector3(-0.08f, 0.20f, -0.08f), new Vector3(0.09f, 0.12f, 0.09f), m);
                Ball(h, new Vector3(0.08f, 0.19f, -0.09f), new Vector3(0.09f, 0.12f, 0.09f), m);
                Ball(h, new Vector3(0f, 0.10f, -0.15f), new Vector3(0.11f, 0.14f, 0.11f), m);
            } },
            new HairEntry { Name = "Shag", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.22f, 0.02f), new Vector3(0.26f, 0.10f, 0.24f), m);
                Blk(h, new Vector3(-0.16f, 0.10f, 0.02f), new Vector3(0.14f, 0.12f, 0.20f), new Vector3(0f,0f,15f), m);
                Blk(h, new Vector3(0.16f, 0.10f, 0.02f), new Vector3(0.14f, 0.12f, 0.20f), new Vector3(0f,0f,-15f), m);
                Blk(h, new Vector3(0f, 0.09f, -0.15f), new Vector3(0.30f, 0.12f, 0.12f), m);
                Blk(h, new Vector3(0f, 0.05f, 0.13f), new Vector3(0.20f, 0.07f, 0.08f), new Vector3(15f,0f,0f), m);
            } },
            new HairEntry { Name = "Afro Taper", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.17f, 0f), new Vector3(0.46f, 0.34f, 0.46f), m);
                Ball(h, new Vector3(-0.17f, 0.06f, 0.00f), new Vector3(0.16f, 0.14f, 0.18f), m);
                Ball(h, new Vector3(0.17f, 0.06f, 0.00f), new Vector3(0.16f, 0.14f, 0.18f), m);
                Ball(h, new Vector3(0f, 0.05f, -0.14f), new Vector3(0.24f, 0.16f, 0.18f), m);
                Ball(h, new Vector3(0f, 0.06f, 0.08f), new Vector3(0.22f, 0.14f, 0.14f), m);
            } },
            new HairEntry { Name = "Twists", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.02f), new Vector3(0.40f, 0.18f, 0.40f), m);
                Blk(h, new Vector3(-0.12f, 0.22f, 0.06f), new Vector3(0.07f, 0.14f, 0.07f), m);
                Blk(h, new Vector3(0.12f, 0.22f, 0.06f), new Vector3(0.07f, 0.14f, 0.07f), m);
                Blk(h, new Vector3(-0.10f, 0.22f, -0.06f), new Vector3(0.07f, 0.14f, 0.07f), m);
                Blk(h, new Vector3(0.10f, 0.22f, -0.06f), new Vector3(0.07f, 0.14f, 0.07f), m);
                Blk(h, new Vector3(0f, 0.24f, 0.00f), new Vector3(0.07f, 0.15f, 0.07f), m);
                Blk(h, new Vector3(0f, 0.22f, -0.13f), new Vector3(0.07f, 0.14f, 0.07f), m);
            } },
            new HairEntry { Name = "Finger Waves", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.01f), new Vector3(0.42f, 0.20f, 0.42f), m);
                Blk(h, new Vector3(0.02f, 0.19f, 0.06f), new Vector3(0.36f, 0.05f, 0.10f), m);
                Blk(h, new Vector3(-0.02f, 0.14f, 0.02f), new Vector3(0.38f, 0.05f, 0.12f), m);
                Blk(h, new Vector3(0.02f, 0.09f, -0.02f), new Vector3(0.36f, 0.05f, 0.12f), m);
                Ball(h, new Vector3(0f, 0.06f, -0.10f), new Vector3(0.30f, 0.10f, 0.16f), m);
            } },
            new HairEntry { Name = "Brushed Up", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.24f, 0.42f), m);
                Blk(h, new Vector3(0f, 0.24f, 0.02f), new Vector3(0.30f, 0.20f, 0.20f), new Vector3(-8f,0f,0f), m);
                Ball(h, new Vector3(-0.15f, 0.08f, 0.00f), new Vector3(0.13f, 0.10f, 0.16f), m);
                Ball(h, new Vector3(0.15f, 0.08f, 0.00f), new Vector3(0.13f, 0.10f, 0.16f), m);
                Blk(h, new Vector3(0f, 0.09f, -0.14f), new Vector3(0.28f, 0.12f, 0.10f), m);
            } },
            new HairEntry { Name = "Middle Part", Group = HairGroup.Medium, Build = (h,m) => {
                Ball(h, new Vector3(-0.12f, 0.15f, -0.01f), new Vector3(0.22f, 0.24f, 0.36f), m);
                Ball(h, new Vector3(0.12f, 0.15f, -0.01f), new Vector3(0.22f, 0.24f, 0.36f), m);
                Ball(h, new Vector3(0f, 0.10f, -0.13f), new Vector3(0.34f, 0.18f, 0.20f), m);
                Blk(h, new Vector3(-0.16f, 0.08f, 0.05f), new Vector3(0.14f, 0.12f, 0.16f), new Vector3(0f,0f,10f), m);
                Blk(h, new Vector3(0.16f, 0.08f, 0.05f), new Vector3(0.14f, 0.12f, 0.16f), new Vector3(0f,0f,-10f), m);
            } },

            // LONG --------------------------------------------------------------
            new HairEntry { Name = "Long", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.28f, 0.44f), m);                                  // crown cap
                Blk(h, new Vector3(0f, 0.02f, 0.14f), new Vector3(0.28f, 0.07f, 0.05f), m);                                    // thin center fringe
                Blk(h, new Vector3(-0.17f, -0.06f, -0.04f), new Vector3(0.10f, 0.34f, 0.11f), new Vector3(0f, 0f, 6f), m);     // left curtain
                Blk(h, new Vector3(0.17f, -0.06f, -0.04f), new Vector3(0.10f, 0.34f, 0.11f), new Vector3(0f, 0f, -6f), m);     // right curtain
                Blk(h, new Vector3(0f, -0.10f, -0.19f), new Vector3(0.32f, 0.38f, 0.11f), m);                                  // back panel
            } },
            new HairEntry { Name = "Afro", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.20f, -0.02f), new Vector3(0.52f, 0.50f, 0.52f), m);   // big rounded crown
                Ball(h, new Vector3(-0.16f, 0.10f, -0.08f), new Vector3(0.22f, 0.22f, 0.22f), m); // left texture lump
                Ball(h, new Vector3(0.16f, 0.10f, -0.08f), new Vector3(0.22f, 0.22f, 0.22f), m);  // right texture lump
                Ball(h, new Vector3(0f, 0.08f, -0.20f), new Vector3(0.26f, 0.24f, 0.24f), m);     // back texture lump
                Ball(h, new Vector3(0f, 0.30f, -0.04f), new Vector3(0.20f, 0.18f, 0.20f), m);     // top texture lump
            } },
            new HairEntry { Name = "Ponytail", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.03f), new Vector3(0.44f, 0.32f, 0.46f), m);   // pulled-back cap
                Blk(h, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.20f, 0.05f, 0.04f), m);     // short swept fringe
                Ball(h, new Vector3(0f, 0.06f, -0.20f), new Vector3(0.13f, 0.13f, 0.16f), m);   // tie
                Blk(h, new Vector3(0f, -0.16f, -0.22f), new Vector3(0.09f, 0.42f, 0.09f), m);   // long tail down back
                Blk(h, new Vector3(0.02f, -0.30f, -0.19f), new Vector3(0.05f, 0.10f, 0.05f), m); // tail tip flick
            } },
            new HairEntry { Name = "High Ponytail", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.30f, 0.44f), m);   // smooth pulled-back cap
                Ball(h, new Vector3(0f, 0.26f, -0.10f), new Vector3(0.15f, 0.15f, 0.15f), m);   // high tie on crown
                Blk(h, new Vector3(0f, 0.30f, -0.20f), new Vector3(0.09f, 0.10f, 0.10f), m);    // tie knot bump
                Blk(h, new Vector3(0f, 0.10f, -0.26f), new Vector3(0.08f, 0.38f, 0.08f), new Vector3(20f, 0f, 0f), m); // tail arcing off crown
                Blk(h, new Vector3(0f, -0.08f, -0.20f), new Vector3(0.07f, 0.20f, 0.07f), m);   // tail continuation down back
            } },
            new HairEntry { Name = "Man Bun", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);   // pulled-back cap
                Blk(h, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.10f, 0.05f, 0.04f), m);     // short front strand
                Ball(h, new Vector3(0f, 0.22f, -0.16f), new Vector3(0.20f, 0.18f, 0.20f), m);   // top-back bun
                Ball(h, new Vector3(0f, 0.19f, -0.21f), new Vector3(0.08f, 0.08f, 0.08f), m);   // tie wrap on bun
            } },
            new HairEntry { Name = "Top Knot", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.10f, -0.01f), new Vector3(0.40f, 0.16f, 0.40f), m);   // close-cropped cap, shaved-ish sides
                Blk(h, new Vector3(0f, -0.04f, -0.14f), new Vector3(0.30f, 0.10f, 0.10f), m);   // trimmed nape line
                Ball(h, new Vector3(0f, 0.27f, -0.02f), new Vector3(0.17f, 0.17f, 0.17f), m);   // small tight knot high on crown
                Ball(h, new Vector3(0f, 0.24f, -0.02f), new Vector3(0.06f, 0.06f, 0.06f), m);   // knot wrap
            } },
            new HairEntry { Name = "Dreadlocks", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.40f, 0.24f, 0.40f), m);        // crown base
                Blk(h, new Vector3(-0.16f, -0.08f, -0.08f), new Vector3(0.045f, 0.38f, 0.045f), m);  // dreadlock
                Blk(h, new Vector3(-0.08f, -0.10f, -0.12f), new Vector3(0.045f, 0.42f, 0.045f), m);  // dreadlock
                Blk(h, new Vector3(0f, -0.12f, -0.16f), new Vector3(0.045f, 0.44f, 0.045f), m);      // dreadlock
                Blk(h, new Vector3(0.08f, -0.10f, -0.12f), new Vector3(0.045f, 0.42f, 0.045f), m);   // dreadlock
                Blk(h, new Vector3(0.16f, -0.08f, -0.08f), new Vector3(0.045f, 0.38f, 0.045f), m);   // dreadlock
                Blk(h, new Vector3(-0.12f, -0.06f, -0.16f), new Vector3(0.04f, 0.34f, 0.04f), m);    // dreadlock
                Blk(h, new Vector3(0.12f, -0.06f, -0.16f), new Vector3(0.04f, 0.34f, 0.04f), m);     // dreadlock
            } },
            new HairEntry { Name = "Cornrows", Group = HairGroup.Long, Build = (h,m) => {
                Blk(h, new Vector3(-0.12f, 0.20f, -0.02f), new Vector3(0.045f, 0.05f, 0.34f), m);  // ridge braid
                Blk(h, new Vector3(-0.06f, 0.22f, -0.02f), new Vector3(0.045f, 0.05f, 0.36f), m);  // ridge braid
                Blk(h, new Vector3(0f, 0.23f, -0.02f), new Vector3(0.045f, 0.05f, 0.36f), m);      // ridge braid
                Blk(h, new Vector3(0.06f, 0.22f, -0.02f), new Vector3(0.045f, 0.05f, 0.36f), m);   // ridge braid
                Blk(h, new Vector3(0.12f, 0.20f, -0.02f), new Vector3(0.045f, 0.05f, 0.34f), m);   // ridge braid
                Blk(h, new Vector3(0f, -0.02f, -0.20f), new Vector3(0.34f, 0.14f, 0.10f), m);      // short gathered nape
            } },
            new HairEntry { Name = "Braided", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);   // crown cap
                Ball(h, new Vector3(0f, 0.02f, -0.20f), new Vector3(0.15f, 0.13f, 0.14f), m);   // braid segment 1
                Ball(h, new Vector3(0f, -0.10f, -0.21f), new Vector3(0.13f, 0.13f, 0.13f), m);  // braid segment 2
                Ball(h, new Vector3(0f, -0.22f, -0.20f), new Vector3(0.12f, 0.12f, 0.12f), m);  // braid segment 3
                Ball(h, new Vector3(0f, -0.33f, -0.18f), new Vector3(0.10f, 0.10f, 0.10f), m);  // braid tip
            } },
            new HairEntry { Name = "Twin Braids", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.42f, 0.28f, 0.42f), m);      // crown cap
                Ball(h, new Vector3(-0.18f, -0.02f, -0.06f), new Vector3(0.11f, 0.11f, 0.11f), m); // left braid seg 1
                Ball(h, new Vector3(-0.19f, -0.14f, -0.06f), new Vector3(0.10f, 0.10f, 0.10f), m); // left braid seg 2
                Ball(h, new Vector3(-0.19f, -0.25f, -0.05f), new Vector3(0.09f, 0.09f, 0.09f), m); // left braid tip
                Ball(h, new Vector3(0.18f, -0.02f, -0.06f), new Vector3(0.11f, 0.11f, 0.11f), m);  // right braid seg 1
                Ball(h, new Vector3(0.19f, -0.14f, -0.06f), new Vector3(0.10f, 0.10f, 0.10f), m);  // right braid seg 2
                Ball(h, new Vector3(0.19f, -0.25f, -0.05f), new Vector3(0.09f, 0.09f, 0.09f), m);  // right braid tip
            } },
            new HairEntry { Name = "Shoulder Length", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.01f), new Vector3(0.44f, 0.30f, 0.44f), m);    // crown cap
                Blk(h, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.30f, 0.07f, 0.05f), m);      // fringe
                Blk(h, new Vector3(-0.18f, -0.12f, -0.03f), new Vector3(0.11f, 0.40f, 0.12f), m); // left curtain to shoulder
                Blk(h, new Vector3(0.18f, -0.12f, -0.03f), new Vector3(0.11f, 0.40f, 0.12f), m);  // right curtain to shoulder
                Blk(h, new Vector3(0f, -0.14f, -0.20f), new Vector3(0.34f, 0.44f, 0.12f), m);     // back panel
            } },
            new HairEntry { Name = "Flowing", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);                                    // crown cap
                Blk(h, new Vector3(-0.15f, -0.02f, -0.10f), new Vector3(0.10f, 0.30f, 0.12f), new Vector3(0f, 0f, 18f), m);      // left swept piece
                Blk(h, new Vector3(0.15f, -0.02f, -0.10f), new Vector3(0.10f, 0.30f, 0.12f), new Vector3(0f, 0f, -18f), m);      // right swept piece
                Blk(h, new Vector3(0f, -0.14f, -0.20f), new Vector3(0.30f, 0.46f, 0.13f), m);                                    // long flowing back panel
                Blk(h, new Vector3(0f, -0.32f, -0.16f), new Vector3(0.20f, 0.16f, 0.10f), m);                                    // flared tail end
            } },
            new HairEntry { Name = "Wavy Long", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);     // crown cap
                Ball(h, new Vector3(-0.17f, -0.02f, -0.03f), new Vector3(0.15f, 0.15f, 0.15f), m); // left wave 1
                Ball(h, new Vector3(-0.20f, -0.11f, -0.06f), new Vector3(0.14f, 0.14f, 0.14f), m); // left wave 2 (offset)
                Ball(h, new Vector3(-0.16f, -0.20f, -0.04f), new Vector3(0.13f, 0.13f, 0.13f), m); // left wave 3 (offset)
                Ball(h, new Vector3(0.17f, -0.02f, -0.03f), new Vector3(0.15f, 0.15f, 0.15f), m);  // right wave 1
                Ball(h, new Vector3(0.20f, -0.11f, -0.06f), new Vector3(0.14f, 0.14f, 0.14f), m);  // right wave 2 (offset)
                Ball(h, new Vector3(0.16f, -0.20f, -0.04f), new Vector3(0.13f, 0.13f, 0.13f), m);  // right wave 3 (offset)
                Blk(h, new Vector3(0f, -0.10f, -0.20f), new Vector3(0.28f, 0.32f, 0.11f), m);      // back panel
            } },
            new HairEntry { Name = "Half Up", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);    // base cap
                Ball(h, new Vector3(0f, 0.26f, -0.04f), new Vector3(0.15f, 0.14f, 0.15f), m);    // small top bun
                Ball(h, new Vector3(0f, 0.24f, -0.06f), new Vector3(0.06f, 0.06f, 0.06f), m);    // bun tie
                Blk(h, new Vector3(-0.17f, -0.10f, -0.05f), new Vector3(0.10f, 0.34f, 0.11f), m); // loose left curtain
                Blk(h, new Vector3(0.17f, -0.10f, -0.05f), new Vector3(0.10f, 0.34f, 0.11f), m);  // loose right curtain
                Blk(h, new Vector3(0f, -0.12f, -0.20f), new Vector3(0.28f, 0.36f, 0.10f), m);     // loose back
            } },
            new HairEntry { Name = "Bun", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.28f, 0.42f), m);   // pulled-back cap
                Blk(h, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.10f, 0.05f, 0.04f), m);     // short front strand
                Ball(h, new Vector3(0f, -0.06f, -0.22f), new Vector3(0.26f, 0.24f, 0.24f), m);  // large low bun at nape
                Ball(h, new Vector3(0f, -0.05f, -0.24f), new Vector3(0.08f, 0.08f, 0.08f), m);  // wrap tie on bun
            } },
            new HairEntry { Name = "Space Buns", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);     // pulled-back cap
                Ball(h, new Vector3(-0.14f, 0.24f, -0.06f), new Vector3(0.18f, 0.18f, 0.18f), m); // left high bun
                Ball(h, new Vector3(-0.14f, 0.22f, -0.09f), new Vector3(0.06f, 0.06f, 0.06f), m); // left bun tie
                Ball(h, new Vector3(0.14f, 0.24f, -0.06f), new Vector3(0.18f, 0.18f, 0.18f), m);  // right high bun
                Ball(h, new Vector3(0.14f, 0.22f, -0.09f), new Vector3(0.06f, 0.06f, 0.06f), m);  // right bun tie
            } },
            new HairEntry { Name = "Slicked Long", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.13f, -0.02f), new Vector3(0.40f, 0.24f, 0.40f), m);   // sleek low-profile cap
                Blk(h, new Vector3(0f, -0.04f, -0.14f), new Vector3(0.32f, 0.18f, 0.14f), m);   // swept transition
                Blk(h, new Vector3(0f, -0.18f, -0.20f), new Vector3(0.26f, 0.36f, 0.09f), m);   // long straight back panel
                Blk(h, new Vector3(0f, -0.36f, -0.18f), new Vector3(0.18f, 0.14f, 0.08f), m);   // straight tapered tip
            } },
            new HairEntry { Name = "Undercut Long", Group = HairGroup.Long, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.05f, 0.10f), new Vector3(0.24f, 0.06f, 0.06f), m);     // swept front strand
                Ball(h, new Vector3(0f, 0.16f, 0.00f), new Vector3(0.34f, 0.24f, 0.40f), m);    // top volume strip, sides shaved
                Blk(h, new Vector3(0f, -0.06f, -0.17f), new Vector3(0.20f, 0.30f, 0.12f), m);   // long back length
                Blk(h, new Vector3(0f, -0.22f, -0.16f), new Vector3(0.16f, 0.16f, 0.10f), m);   // back tip
            } },
            new HairEntry { Name = "Mullet", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.16f, 0.02f), new Vector3(0.40f, 0.20f, 0.36f), m);    // short cropped top/sides
                Blk(h, new Vector3(0f, -0.02f, -0.19f), new Vector3(0.20f, 0.30f, 0.11f), m);   // long back panel start
                Blk(h, new Vector3(0f, -0.20f, -0.18f), new Vector3(0.16f, 0.24f, 0.09f), m);   // long back panel continuation
                Blk(h, new Vector3(0f, -0.36f, -0.15f), new Vector3(0.12f, 0.14f, 0.07f), m);   // tapered tail tip
            } },
            new HairEntry { Name = "Hime", Group = HairGroup.Long, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.14f, -0.02f), new Vector3(0.42f, 0.26f, 0.42f), m);    // crown cap
                Blk(h, new Vector3(0f, 0.04f, 0.15f), new Vector3(0.32f, 0.10f, 0.05f), m);      // blunt fringe slab
                Blk(h, new Vector3(-0.18f, -0.06f, -0.02f), new Vector3(0.09f, 0.34f, 0.10f), m); // blunt straight left lock
                Blk(h, new Vector3(0.18f, -0.06f, -0.02f), new Vector3(0.09f, 0.34f, 0.10f), m);  // blunt straight right lock
                Blk(h, new Vector3(0f, -0.10f, -0.20f), new Vector3(0.34f, 0.40f, 0.10f), m);     // straight back panel
            } },
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
            // Goatee: narrow chin tuft (flush polygon wedge) under a small mustache. No sphere.
            new FacialEntry { Name = "Goatee",    Build = (h,m) => {
                Blk(h, new Vector3(0f, -0.05f, 0.18f), new Vector3(0.10f, 0.025f, 0.045f), m);
                BeardMesh(h, m, thetaMax: 0.45f, phiTop: -0.55f, phiBot: -1.30f,
                          drop: 0.025f, bulge: 0.012f, widenBottom: 0.55f); } },
            // Stubble: dead-flush shell on chin + jaw (bulge/drop 0), no protrusion at all.
            new FacialEntry { Name = "Stubble",   Build = (h,m) =>
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
                Ball(h, new Vector3(0.0f, -0.18f, 0.13f), new Vector3(0.014f, 0.014f, 0.014f), m);     // chain link, front
                Ball(h, new Vector3(0.106f, -0.18f, 0.086f), new Vector3(0.014f, 0.014f, 0.014f), m);  // chain link
                Ball(h, new Vector3(0.15f, -0.18f, -0.02f), new Vector3(0.014f, 0.014f, 0.014f), m);   // chain link, right side
                Ball(h, new Vector3(0.106f, -0.18f, -0.126f), new Vector3(0.014f, 0.014f, 0.014f), m); // chain link
                Blk(h, new Vector3(0.0f, -0.18f, -0.17f), new Vector3(0.02f, 0.014f, 0.01f), Dark());  // clasp at the back
                Ball(h, new Vector3(-0.106f, -0.18f, -0.126f), new Vector3(0.014f, 0.014f, 0.014f), m); // chain link
                Ball(h, new Vector3(-0.15f, -0.18f, -0.02f), new Vector3(0.014f, 0.014f, 0.014f), m);  // chain link, left side
                Ball(h, new Vector3(-0.106f, -0.18f, 0.086f), new Vector3(0.014f, 0.014f, 0.014f), m); // chain link
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
                Ball(h, new Vector3(0f, 0.30f, -0.01f), new Vector3(0.06f, 0.06f, 0.06f), m);     // top button
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
                Ball(h, new Vector3(0f, 0.34f, -0.01f), new Vector3(0.10f, 0.10f, 0.10f), m);     // pom
            } },
            new AccessoryEntry { Name = "Bucket Hat", Headgear = true, Build = (h,m) => {
                Ball(h, new Vector3(0f, 0.16f, -0.01f), new Vector3(0.44f, 0.24f, 0.44f), m);     // short crown
                Ball(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.66f, 0.06f, 0.66f), m);         // all-around sloped brim
            } },
            new AccessoryEntry { Name = "Fedora", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.20f, -0.01f), new Vector3(0.38f, 0.20f, 0.38f), m);      // crown
                Blk(h, new Vector3(0f, 0.25f, 0.05f), new Vector3(0.30f, 0.03f, 0.10f), m);       // front pinch crease
                Blk(h, new Vector3(0f, 0.12f, 0f), new Vector3(0.66f, 0.03f, 0.66f), m);          // wide flat brim
                Blk(h, new Vector3(0f, 0.10f, 0f), new Vector3(0.40f, 0.04f, 0.40f), Dark());     // band
            } },
            new AccessoryEntry { Name = "Top Hat", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.09f, 0f), new Vector3(0.56f, 0.03f, 0.56f), m);          // flat brim
                Blk(h, new Vector3(0f, 0.28f, -0.01f), new Vector3(0.34f, 0.34f, 0.34f), m);      // tall cylinder crown
                Blk(h, new Vector3(0f, 0.14f, 0f), new Vector3(0.36f, 0.05f, 0.36f), Dark());     // band
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
                Blk(h, new Vector3(0f, 0.09f, 0.20f), new Vector3(0.30f, 0.03f, 0.12f), m);       // short stubby brim
                Ball(h, new Vector3(0f, 0.24f, -0.01f), new Vector3(0.05f, 0.05f, 0.05f), m);     // top button
            } },
            new AccessoryEntry { Name = "Visor", Headgear = true, Build = (h,m) => {
                Blk(h, new Vector3(0f, 0.08f, 0f), new Vector3(0.48f, 0.06f, 0.48f), m);          // headband ring
                Blk(h, new Vector3(0f, 0.09f, 0.22f), new Vector3(0.34f, 0.03f, 0.20f), m);       // forward brim
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
