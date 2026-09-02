using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // THE HORSE. Its mane is no longer the human hair catalog tilted onto the neck (the Crown /
    // Ring / FrontSweep roots scatter over the whole sphere, so on a horse they sprouted from the
    // muzzle and both cheeks). It has its own seven-entry list, every entry rooted along the CREST
    // POLYLINE - poll, down the top of the neck, over the cap to the withers - with capsule
    // collision against the neck so cards drape down the off flank instead of passing through it.
    // Style indices stay a plain U8 on the wire; the list is append-only.
    public static partial class Cosmetics
    {
        public class ManeEntry
        {
            public string Name;
            public HairSim.HairDef Def;    // rootPath/rootNormals are filled per build from HorseCrest
            public bool Forelock;          // adds the shared forelock component
            public bool Strip;             // draws an opaque crest strip under the roots
            public bool NoCards;           // solid pieces only (Button Braids)
            public Action<Transform, ActiveRagdoll, Material> Extra;
        }

        /// <summary>
        /// The crest polyline in HEAD-LOCAL metres for this build (h = height scale, g = girth scale):
        /// A) a great-circle arc over the skull from the poll to where the neck crest enters the
        /// skull, B) the straight top of the neck capsule, C) the capsule's lower cap down to the
        /// barrel top. Normals are the surface normals at each point. Everything is derived from the
        /// BodyLayout neck constants so the hair cannot drift off the neck.
        /// </summary>
        public static void HorseCrest(ActiveRagdoll rag, out Vector3[] pts, out Vector3[] nrm)
        {
            float h = rag.HeightScale, g = rag.GirthScale;
            Vector3 C = new Vector3(BodyLayout.HorseNeckOff.x * g, BodyLayout.HorseNeckOff.y * h, BodyLayout.HorseNeckOff.z * h);
            float pitch = BodyLayout.HorseNeckPitch * Mathf.Deg2Rad;
            Vector3 a = new Vector3(0f, Mathf.Cos(pitch), Mathf.Sin(pitch));        // capsule axis, toward the skull
            Vector3 n = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch));       // crest side (up and back)
            float r = BodyLayout.HorseNeckR * g;
            float half = BodyLayout.HorseNeckLen * 0.5f * h - r;
            float R = rag.HeadVisualRadius;
            var P = new List<Vector3>(); var N = new List<Vector3>();
            // Exit point: march down the crest line until it enters the skull.
            float sExit = half;
            for (float s = half; s > -half; s -= 0.005f) { if ((C + n * r + a * s).magnitude < R) { sExit = s; break; } }
            Vector3 exitDir = (C + n * r + a * sExit).normalized;
            Vector3 pollDir = new Vector3(0f, 0.8f, -0.6f).normalized;
            for (int i = 0; i <= 5; i++)
            {
                Vector3 d = Vector3.Slerp(pollDir, exitDir, i / 5f).normalized;
                P.Add(d * (R + 0.004f)); N.Add(d);
            }
            for (int i = 1; i <= 8; i++)
            {
                float s = Mathf.Lerp(sExit, -half, i / 8f);
                P.Add(C + n * (r + 0.004f) + a * s); N.Add(n);
            }
            Vector3 capCentre = C - a * half;
            float yStop = -0.31f * h;
            for (int i = 1; i <= 5; i++)
            {
                float ang = i / 5f * Mathf.PI * 0.5f;             // sweep from the crest normal toward -a
                Vector3 d = (n * Mathf.Cos(ang) - a * Mathf.Sin(ang)).normalized;
                Vector3 p = capCentre + d * (r + 0.004f);
                if (p.y < yStop) break;
                P.Add(p); N.Add(d);
            }
            pts = P.ToArray(); nrm = N.ToArray();
        }

        /// <summary>An opaque hair-coloured ribbon along the crest under the roots.</summary>
        static void CrestStrip(Transform head, Material mat, Vector3[] pts, Vector3[] nrm, float width)
        {
            int n = pts.Length;
            var m = MeshGen.Param((u, v) =>
            {
                float t = u * (n - 1); int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 2); float f = t - k;
                Vector3 p = Vector3.Lerp(pts[k], pts[k + 1], f);
                Vector3 nn = Vector3.Slerp(nrm[k], nrm[k + 1], f).normalized;
                Vector3 tan = (pts[k + 1] - pts[k]).normalized;
                Vector3 side = Vector3.Cross(tan, nn).normalized;
                return p + nn * 0.002f + side * ((v - 0.5f) * width);
            }, (n - 1) * 2, 2);
            var vv = m.vertices; var norms = new Vector3[vv.Length]; var tans = new Vector4[vv.Length];
            for (int i = 0; i < vv.Length; i++)
            {
                int col = i % ((n - 1) * 2 + 1); float t = col / (float)((n - 1) * 2) * (n - 1); int k = Mathf.Clamp(Mathf.FloorToInt(t), 0, n - 2);
                Vector3 nn = Vector3.Slerp(nrm[k], nrm[k + 1], t - k).normalized;
                Vector3 tan = (pts[k + 1] - pts[k]).normalized;
                norms[i] = tan; tans[i] = new Vector4(nn.x, nn.y, nn.z, 1f);
            }
            m.normals = norms; m.tangents = tans;
            Piece(head, m, mat);
        }

        static readonly Vector3 ForelockDir = new Vector3(0f, 0.99f, -0.14f).normalized;

        /// <summary>Build the mane, forelock and crest strip for a horse.</summary>
        static void AttachMane(ActiveRagdoll rag, PlayerAppearance a, Transform head)
        {
            int idx = a.HairStyle;
            if (idx < 0 || idx >= _mane.Count) idx = 0;
            var entry = _mane[idx];
            float h = rag.HeightScale, g = rag.GirthScale;
            HorseCrest(rag, out var pts, out var nrm);
            var mats = new HairMats(rag, a.HairColor);

            if (entry.Strip) CrestStrip(head, mats.Cap, pts, nrm, 0.03f);
            entry.Extra?.Invoke(head, rag, mats.Cap);
            if (!entry.NoCards)
            {
                var def = entry.Def;
                def.rootPath = pts; def.rootNormals = nrm;
                def.length *= h;
                var go = new GameObject("ManeSim");
                go.transform.SetParent(head, false);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                var sim = go.AddComponent<HairSim>();
                Vector3 C = new Vector3(BodyLayout.HorseNeckOff.x * g, BodyLayout.HorseNeckOff.y * h, BodyLayout.HorseNeckOff.z * h);
                float pitch = BodyLayout.HorseNeckPitch * Mathf.Deg2Rad;
                Vector3 ax = new Vector3(0f, Mathf.Cos(pitch), Mathf.Sin(pitch));
                float r = BodyLayout.HorseNeckR * g, half = BodyLayout.HorseNeckLen * 0.5f * h - r;
                sim.AddCapsule(head, C + ax * half, C - ax * half, r);
                sim.Build(head, def, mats.Cards, rag.HeadVisualRadius);
            }
            if (entry.Forelock)
            {
                Vector3 p = ForelockDir * (rag.HeadVisualRadius + 0.01f);
                var fl = new HairSim.HairDef
                {
                    root = HairSim.RootMode.Path, rootPath = new[] { p }, rootNormals = new[] { ForelockDir }, rootSpread = 0.025f,
                    strands = 16, nodes = 4, length = 0.10f * h, fan = 4, stiffness = 0.3f, flow = new Vector3(0f, -0.6f, 0.8f),
                    curl = 0.01f, jitter = 0.12f, thickness = 0.04f,
                };
                Sim(head, fl, mats, rag.HeadVisualRadius, Quaternion.identity);
            }
        }

        static void BraidKnobs(Transform head, ActiveRagdoll rag, Material mat)
        {
            HorseCrest(rag, out var pts, out var nrm);
            // Even arc-length spacing from t 0.08 to 0.95 of the crest, plus one at the forelock root.
            float total = 0f; var cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) { total += Vector3.Distance(pts[i - 1], pts[i]); cum[i] = total; }
            var parts = new List<Mesh>();
            void Knob(Vector3 p, Vector3 nn)
            {
                var k = MeshGen.Lathe(new[] { new Vector2(0.022f, 0f), new Vector2(0.028f, 0.012f), new Vector2(0.026f, 0.026f), new Vector2(0.015f, 0.034f), new Vector2(0f, 0.036f) }, 12);
                MeshGen.Transform(k, p + nn * 0.004f, Quaternion.FromToRotation(Vector3.up, nn));
                parts.Add(k);
            }
            for (int j = 0; j < 8; j++)
            {
                float target = Mathf.Lerp(0.08f, 0.95f, j / 7f) * total;
                int k = 1; while (k < pts.Length - 1 && cum[k] < target) k++;
                float f = Mathf.InverseLerp(cum[k - 1], cum[k], target);
                Knob(Vector3.Lerp(pts[k - 1], pts[k], f), Vector3.Slerp(nrm[k - 1], nrm[k], f).normalized);
            }
            Knob(ForelockDir * (rag.HeadVisualRadius + 0.002f), ForelockDir);
            var mesh = MeshGen.Combine(parts.ToArray());
            var v = mesh.vertices; var nn2 = mesh.normals; var t2 = new Vector4[v.Length];
            for (int i = 0; i < v.Length; i++) t2[i] = new Vector4(nn2[i].x, nn2[i].y, nn2[i].z, 1f);
            mesh.tangents = t2;
            Piece(head, mesh, mat);
        }

        // ---- the mane catalog (index 0 = Roached; order is wire state) --------------------------
        static readonly List<ManeEntry> _mane = new List<ManeEntry>
        {
            new ManeEntry { Name = "Roached", Strip = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 28, nodes = 3, length = 0.045f, fan = 3, staticToHead = true,
                stiffness = 1f, flow = Vector3.zero, jitter = 0.15f, thickness = 0.03f, rootSpread = 0.012f, rootSideBias = 0f, normalBlend = 0.9f } },
            new ManeEntry { Name = "Short", Forelock = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 30, nodes = 5, length = 0.11f, fan = 4,
                stiffness = 0.35f, flow = new Vector3(0.8f, -0.6f, -0.1f), jitter = 0.08f, thickness = 0.05f, rootSpread = 0.02f, rootSideBias = 0.6f } },
            new ManeEntry { Name = "Flowing", Forelock = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 36, nodes = 7, length = 0.28f, fan = 5,
                stiffness = 0.15f, flow = new Vector3(0.8f, -0.6f, -0.05f), curl = 0.035f, jitter = 0.12f, thickness = 0.06f, rootSpread = 0.02f, rootSideBias = 0.6f } },
            new ManeEntry { Name = "Long Flowing", Forelock = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 40, nodes = 8, length = 0.40f, fan = 6,
                stiffness = 0.08f, flow = new Vector3(0.7f, -0.7f, -0.05f), curl = 0.04f, jitter = 0.10f, thickness = 0.055f, rootSpread = 0.02f, rootSideBias = 0.6f } },
            new ManeEntry { Name = "Standing", Strip = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 34, nodes = 4, length = 0.11f, fan = 4,
                stiffness = 0.9f, flow = new Vector3(0f, 0.55f, -0.83f), curl = 0.005f, jitter = 0.18f, thickness = 0.035f, rootSpread = 0.02f, rootSideBias = 0f, normalBlend = 0.6f } },
            new ManeEntry { Name = "Button Braids", Strip = true, NoCards = true, Extra = BraidKnobs },
            new ManeEntry { Name = "Wild", Forelock = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Path, strands = 38, nodes = 7, length = 0.22f, fan = 5,
                stiffness = 0.12f, flow = new Vector3(0.75f, -0.65f, -0.1f), curl = 0.03f, jitter = 0.35f, thickness = 0.06f, rootSpread = 0.025f, splitSides = true } },
        };
        public static IReadOnlyList<ManeEntry> Manes => _mane;
        public static string[] ManeNames { get { var n = new string[_mane.Count]; for (int i = 0; i < n.Length; i++) n[i] = _mane[i].Name; return n; } }
    }
}
