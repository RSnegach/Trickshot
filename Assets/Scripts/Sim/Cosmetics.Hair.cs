using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    // HUMAN HAIR: the catalog and the builders behind it. Every short style is a SHAPED SCALP
    // SHELL (a loft with a real hairline and a rounded lip, see HairShape) plus either tapered
    // TUFT cards (the Tuft atlas), generated geometry (spikes, a fin, helices, a bun) or a drawn-on
    // decal. Long / Shoulder Length are untouched HairSim card styles and remain the quality bar.
    //
    // The horse MANE shares this catalog by index (SpeciesCosmetics.UsesHumanHair) but never the
    // shells: on a horse only ManeDef (or Def) builds, as tilted cards on the neck crest.
    public static partial class Cosmetics
    {
        /// <summary>
        /// The hair materials one build may need, created on demand and registered on the ragdoll
        /// for teardown. Cards = the Long atlas cutout, Cap = opaque shell, Tuft = the Tuft atlas
        /// cutout. Own() registers any extra material a builder makes (a decal, a dark elastic).
        /// </summary>
        public class HairMats
        {
            readonly ActiveRagdoll _rag;
            public readonly Color Color;
            Material _cards, _cap, _tuft;
            public HairMats(ActiveRagdoll rag, Color c) { _rag = rag; Color = c; }
            public Material Cards => _cards ??= Own(Make.Hair(Color));
            public Material Cap => _cap ??= Own(Make.HairCap(Color));
            public Material Tuft => _tuft ??= Own(Make.HairTuftCards(Color));
            public Material Own(Material m) { _rag?.RegisterCosmeticMaterial(m); return m; }
            /// <summary>A fresh opaque shell material with its own shader knobs.</summary>
            public Material CapWith(float normalWeight, float specStr = -1f)
            {
                var m = Own(Make.HairCap(Color));
                m.SetFloat("_NormalWeight", normalWeight);
                if (specStr >= 0f) m.SetFloat("_SpecStr", specStr);
                return m;
            }
            /// <summary>A fresh Long-atlas cutout material (beard-style low cutoff) with its own knobs.</summary>
            public Material TuftWith(float solidToTip, float normalWeight = 0f)
            {
                var m = Own(Make.HairTuft(Color));
                m.SetFloat("_SolidToTip", solidToTip);
                m.SetFloat("_NormalWeight", normalWeight);
                return m;
            }
        }

        // ---- collider-less generated piece ---------------------------------------------------
        /// <summary>Wrap a generated mesh in a collider-less child of <paramref name="parent"/> with
        /// GeneratedMeshOwner teardown. Positions are the mesh's own (head-local, already scaled).</summary>
        public static GameObject Piece(Transform parent, Mesh mesh, Material mat, bool castShadows = true)
        {
            var go = new GameObject("cz");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<GeneratedMeshOwner>().Mesh = mesh;
            return go;
        }

        /// <summary>One HairSim component under the head for a def; the card material is picked
        /// by the def's atlas. Returns the component so a builder can keep a handle.</summary>
        public static HairSim Sim(Transform head, in HairSim.HairDef def, HairMats mats, float headRadius, Quaternion localRot)
        {
            var go = new GameObject("HairSim");
            go.transform.SetParent(head, false);
            go.transform.localPosition = Vector3.zero;   // ROTATION ONLY, see HairSim.Build's anchor note
            go.transform.localRotation = localRot;
            go.transform.localScale = Vector3.one;
            var sim = go.AddComponent<HairSim>();
            sim.Build(head, def, def.atlas == HairSim.Atlas.Tuft ? mats.Tuft : mats.Cards, headRadius);
            return sim;
        }

        // Scaled shell radius for the CURRENT build (girth-1 metres times _cosScale).
        static float ShellR(Vector3 dir, HairShape.ShellParams p) => HairShape.ShellRadius(dir, p, HeadR) * _cosScale;
        static Func<Vector3, float> ShellRootFn(HairShape.ShellParams p, float extra = 0f)
            => dir => (HairShape.ShellRadius(dir, p, HeadR) + extra) * _cosScale;

        // ---- the scalp shell ---------------------------------------------------------------
        /// <summary>
        /// The shaped scalp cap that replaced CrownPatch: a loft bounded by the shared hairline,
        /// with per-direction thickness, optional noise, a rounded lip down to the skin, comb
        /// tangents for the anisotropic sheen and the geometric normal in mesh.tangents for form
        /// shading. In decal mode (LipRings = 0) it is a flush surface at HeadR+Proud carrying a
        /// vertex-alpha edge fade for a blended decal material.
        /// </summary>
        public static Mesh HairShellMesh(HairShape.ShellParams p)
        {
            int cols = p.Cols, capRows = p.Rows;
            int lip = p.Decal ? 0 : Mathf.Max(1, (int)p.LipRings);
            int rows = capRows + lip;
            float skin = HeadR + 0.003f;
            // v in [0,1]: the first capRows/rows of it sweep the cap crown -> hairline, the rest
            // the lip. Returns girth-1 metres; scaled at the end.
            Vector3 F(float u, float v)
            {
                float theta = -Mathf.PI + u * Mathf.PI * 2f;
                float edge = HairShape.HairlinePhi(theta);
                float capV = rows > 0 ? (float)capRows / rows : 1f;
                if (v <= capV || lip == 0)
                {
                    float phi = Mathf.Clamp01(v / capV) * edge;
                    Vector3 d = HairShape.Dir(phi, theta);
                    return d * HairShape.ShellRadius(d, p, HeadR);
                }
                else
                {
                    // Rounded lip: from the edge radius down to the skin over a short arc whose
                    // width follows the thickness there (thick shells get a wider, still-rounded lip).
                    float t = Mathf.Clamp01((v - capV) / (1f - capV));
                    Vector3 de = HairShape.Dir(edge, theta);
                    float rEdge = HairShape.ShellRadius(de, p, HeadR);
                    float thick = Mathf.Max(rEdge - skin, 0.001f);
                    float lipArc = Mathf.Clamp(thick * 0.7f, 0.004f, 0.035f) / HeadR;
                    float phi = edge + t * lipArc;
                    Vector3 d = HairShape.Dir(phi, theta);
                    float r = skin + (rEdge - skin) * Mathf.Cos(t * Mathf.PI * 0.5f);
                    return d * r;
                }
            }
            var m = MeshGen.Param(F, cols, rows, wrapU: true, wrapV: false, flip: true);
            // Geometric normals -> tangents; comb field -> normals; rim -> uv2.x; fade -> colours.
            var verts = m.vertices; var geo = m.normals;
            int count = verts.Length;
            var norms = new Vector3[count]; var tans = new Vector4[count]; var uv2 = new Vector2[count]; var colors = new Color32[count];
            int cw = cols + 1;
            for (int idx = 0; idx < count; idx++)
            {
                int i = idx % cw, j = idx / cw;
                float u = i / (float)cols, v = j / (float)rows;
                Vector3 pos = verts[idx];
                Vector3 dir = pos.sqrMagnitude > 1e-10f ? pos.normalized : Vector3.up;
                Vector3 gn = geo[idx];
                if (Vector3.Dot(gn, dir) < 0f) gn = -gn;   // outward, whatever the winding did
                tans[idx] = new Vector4(gn.x, gn.y, gn.z, 1f);
                norms[idx] = CombTangent(dir, p);
                float capV = (float)capRows / rows;
                float rim = v <= capV ? 1f : Mathf.Lerp(1f, 0.55f, (v - capV) / Mathf.Max(1e-3f, 1f - capV));
                uv2[idx] = new Vector2(rim, 0f);
                // Vertex alpha: hairline fade (decals) and a polar ramp.
                float a = 1f;
                if (p.EdgeFade > 0f)
                {
                    float sdf = HairShape.HairlineSdf(dir) * HeadR;   // metres inside the hairline
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sdf / p.EdgeFade));
                }
                if (p.PhiRampAlpha < 1f)
                {
                    HairShape.Polar(dir, out float phi, out _);
                    a *= Mathf.Lerp(1f, p.PhiRampAlpha, Mathf.Clamp01(phi / 1.2f));
                }
                byte ab = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                colors[idx] = new Color32(255, 255, 255, ab);
                verts[idx] = pos * _cosScale;
            }
            m.vertices = verts; m.normals = norms; m.tangents = tans; m.uv2 = uv2; m.colors32 = colors;
            m.RecalculateBounds();
            return m;
        }

        static Vector3 CombTangent(Vector3 dir, HairShape.ShellParams p)
        {
            Vector3 t;
            switch (p.CombMode)
            {
                case HairShape.Comb.ForwardUp: t = new Vector3(0f, 0.7f, 0.7f); break;
                case HairShape.Comb.TowardPoint: t = p.CombPoint.normalized - dir; break;
                case HairShape.Comb.RandomSmooth: return HairShape.NoiseTangent(dir, 3f, p.Seed + 7);
                case HairShape.Comb.Outward: t = Vector3.Cross(dir, Vector3.up); break;
                default: // Meridian: crown -> down the head
                    HairShape.Polar(dir, out float phi, out float theta);
                    t = new Vector3(Mathf.Cos(phi) * Mathf.Sin(theta), -Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Cos(theta));
                    break;
            }
            t -= dir * Vector3.Dot(t, dir);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.Cross(dir, Vector3.right);
            return t.normalized;
        }

        public static GameObject HairShell(Transform head, Material mat, HairShape.ShellParams p)
            => Piece(head, HairShellMesh(p), mat, castShadows: !p.Decal);

        // Deterministic per-build RNG for the generated geometry (no UnityEngine.Random).
        struct Lcg
        {
            uint _s;
            public Lcg(uint seed) { _s = seed * 2654435761u + 1013904223u; }
            public float Next() { _s = _s * 1664525u + 1013904223u; return (_s >> 8) * (1f / 16777216f); }
            public float Range(float a, float b) => Mathf.Lerp(a, b, Next());
            public float Sym() => Next() * 2f - 1f;
        }

        // A root direction inside the hairline (area-uniform, thinning to the edge), from the LCG.
        static Vector3 HairlineRoot(ref Lcg rng, float edgeFade = 0.15f, float phiMax = 1.6f)
        {
            for (int attempt = 0; attempt < 32; attempt++)
            {
                float cphi = Mathf.Lerp(1f, Mathf.Cos(phiMax), rng.Next());
                float phi = Mathf.Acos(cphi);
                float theta = rng.Sym() * Mathf.PI;
                Vector3 d = HairShape.Dir(phi, theta);
                float sdf = HairShape.HairlineSdf(d);
                if (sdf <= 0f) continue;
                if (rng.Next() <= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sdf / edgeFade))) return d;
            }
            return Vector3.up;
        }

        // Rotate `dir` away from `axisFrom` toward `toward` by `deg` (a tilt in the plane of the two).
        static Vector3 Tilt(Vector3 dir, Vector3 toward, float deg)
        {
            Vector3 axis = Vector3.Cross(dir, toward);
            if (axis.sqrMagnitude < 1e-8f) return dir;
            return Quaternion.AngleAxis(deg, axis.normalized) * dir;
        }

        static void SetNormalsTangents(Mesh m, Vector3 normalForAll)
        {
            var geo = m.normals; var n = new Vector3[geo.Length]; var t = new Vector4[geo.Length];
            for (int i = 0; i < geo.Length; i++) { n[i] = normalForAll; t[i] = new Vector4(geo[i].x, geo[i].y, geo[i].z, 1f); }
            m.normals = n; m.tangents = t;
        }

        // ---- shell presets ---------------------------------------------------------------
        static readonly HairShape.ShellParams ShellCrew = new HairShape.ShellParams
        { ThickCrown = 0.016f, ThickSide = 0.006f, ThickEdge = 0.004f, QuiffLip = 0.004f, CombMode = HairShape.Comb.ForwardUp, PlaneClampY = 0.017f, NoiseAmp = 0.0015f, NoiseFreq = 9f, NoiseOctaves = 2, Seed = 11 };
        static readonly HairShape.ShellParams ShellSpiky = new HairShape.ShellParams
        { ThickCrown = 0.008f, ThickSide = 0.008f, ThickEdge = 0.006f, CombMode = HairShape.Comb.Meridian };
        static readonly HairShape.ShellParams ShellBob = new HairShape.ShellParams
        { ThickCrown = 0.012f, ThickSide = 0.006f, ThickEdge = 0.004f, CombMode = HairShape.Comb.Meridian };
        static readonly HairShape.ShellParams ShellMessy = new HairShape.ShellParams
        { ThickCrown = 0.012f, ThickSide = 0.008f, ThickEdge = 0.004f, NoiseAmp = 0.004f, NoiseFreq = 5f, NoiseOctaves = 2, Seed = 3, CombMode = HairShape.Comb.RandomSmooth };
        static readonly HairShape.ShellParams ShellCurly = new HairShape.ShellParams
        { ThickCrown = 0.018f, ThickSide = 0.012f, ThickEdge = 0.005f, NoiseAmp = 0.007f, NoiseFreq = 6f, NoiseOctaves = 2, Seed = 5, CombMode = HairShape.Comb.RandomSmooth };
        static readonly HairShape.ShellParams ShellAfro = new HairShape.ShellParams
        {
            ThickOverride = d => 0.080f + 0.015f * Mathf.Max(0f, Vector3.Dot(d, new Vector3(0f, 0.7f, -0.7f))),
            NoiseAmp = 0.009f, NoiseFreq = 7f, NoiseOctaves = 3, Seed = 9, CombMode = HairShape.Comb.RandomSmooth, Cols = 40, Rows = 20,
        };
        static readonly Vector3 TieDir = new Vector3(0f, Mathf.Cos(0.98f), -Mathf.Sin(0.98f));
        static readonly Vector3 BunDir = new Vector3(0f, Mathf.Cos(0.65f), -Mathf.Sin(0.65f));
        static readonly HairShape.ShellParams ShellSleekTie = new HairShape.ShellParams
        { ThickCrown = 0.008f, ThickSide = 0.006f, ThickEdge = 0.004f, CombMode = HairShape.Comb.TowardPoint, CombPoint = new Vector3(0f, Mathf.Cos(0.98f), -Mathf.Sin(0.98f)) };
        static readonly HairShape.ShellParams ShellSleekBun = new HairShape.ShellParams
        { ThickCrown = 0.008f, ThickSide = 0.006f, ThickEdge = 0.004f, CombMode = HairShape.Comb.TowardPoint, CombPoint = new Vector3(0f, Mathf.Cos(0.65f), -Mathf.Sin(0.65f)) };
        static readonly HairShape.ShellParams DecalBuzz = new HairShape.ShellParams
        { ThickOverride = _ => 0f, LipRings = 0, Proud = 0.002f, EdgeFade = 0.010f, PhiRampAlpha = 0.7f, Cols = 32, Rows = 14 };

        // ---- style builders ---------------------------------------------------------------
        // Every builder works in head-local metres; shell/geometry scale by _cosScale; sim roots
        // via ShellRootFn so cards sit ON the shell.

        static void BuildBuzz(Transform head, HairMats m)
        {
            var c = Color.Lerp(m.Color, Color.black, 0.25f); c.a = 0.9f;
            var decal = m.Own(Make.Decal(c, Make.Stipple, 6f, 3f));
            HairShell(head, decal, DecalBuzz);
        }

        static void BuildCrewCut(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellCrew);
            var def = new HairSim.HairDef
            {
                root = HairSim.RootMode.Hairline, strands = 220, nodes = 4, length = 0.026f, fan = 1, staticToHead = true,
                stiffness = 1f, flow = new Vector3(0f, 0.55f, 1f), curl = 0f, jitter = 0.25f, thickness = 0.02f,
                atlas = HairSim.Atlas.Tuft, strips = 0b0111, rootRadiusAt = ShellRootFn(ShellCrew), normalBlend = 0.4f,
                frontBias = 0.5f, tieDir = new Vector3(0f, 0.6f, 0.8f),
            };
            Sim(head, def, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildSpiky(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellSpiky);
            var rng = new Lcg(1234);
            var parts = new List<Mesh>();
            Vector3 flow = Vector3.up;
            for (int i = 0; i < 64; i++)
            {
                Vector3 dir = HairlineRoot(ref rng, 0.25f, 1.35f);
                HairShape.Polar(dir, out float phi, out float theta);
                bool front = Mathf.Abs(theta) < 0.6f && phi < 0.9f;
                bool side = Mathf.Abs(theta) > 1.0f && phi > 0.9f;
                float len = side ? rng.Range(0.032f, 0.040f) : rng.Range(0.045f, 0.070f);
                // Axis: the shell normal tilted away from the crown, forward at the front, out at the sides.
                Vector3 axis = Tilt(dir, Vector3.down, rng.Range(10f, 25f));
                if (front) axis = Tilt(axis, Vector3.forward, 30f);
                else if (side) axis = Tilt(axis, new Vector3(Mathf.Sign(dir.x), 0f, 0f), 35f);
                Vector3 bendTo = Vector3.Slerp(axis, flow, 0.5f);
                float bend = rng.Range(10f, 20f);
                float r0 = HairShape.ShellRadius(dir, ShellSpiky, HeadR) - 0.004f;
                Vector3 p0 = dir * r0;
                var path = new Vector3[4];
                Vector3 a = axis;
                path[0] = p0;
                for (int k = 1; k < 4; k++)
                {
                    a = Tilt(a, bendTo, bend / 3f);
                    path[k] = path[k - 1] + a * (len / 3f);
                }
                var spike = MeshGen.Tube(path, new[] { 0.008f, 0.006f, 0.003f, 0.001f }, 6, false, true);
                SetNormalsTangents(spike, (path[3] - path[0]).normalized);
                parts.Add(spike);
            }
            var mesh = MeshGen.Combine(parts.ToArray());
            MeshGen.Transform(mesh, Vector3.zero, Quaternion.identity, Vector3.one * _cosScale);
            Piece(head, mesh, m.CapWith(0.5f, 0.35f));
            var tufts = new HairSim.HairDef
            {
                root = HairSim.RootMode.Hairline, strands = 140, nodes = 4, length = 0.026f, fan = 1, staticToHead = true,
                stiffness = 1f, flow = new Vector3(0f, 1f, 0.2f), jitter = 0.3f, thickness = 0.02f,
                atlas = HairSim.Atlas.Tuft, strips = 0b0001, rootRadiusAt = ShellRootFn(ShellSpiky), normalBlend = 0.6f,
            };
            Sim(head, tufts, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildFringe(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellBob);
            // Bang panel over the forehead: solid at the root, feathered and scalloped at the brow.
            Vector2 strip = HairSim.AtlasStripsU[2];
            Vector3 F(float u, float v)
            {
                float theta = Mathf.Lerp(-0.85f, 0.85f, u);
                float phi = Mathf.Lerp(0.50f, 1.15f, v) + 0.06f * Mathf.Sin(6f * u * Mathf.PI) * Smooth(0.7f, 1f, v);
                Vector3 d = HairShape.Dir(phi, theta);
                float rTop = HairShape.ShellRadius(HairShape.Dir(0.5f, theta), ShellBob, HeadR);
                float rBot = HeadR + 0.007f + 0.003f * Smooth(0.66f, 1f, v);
                return d * Mathf.Lerp(rTop, rBot, Mathf.SmoothStep(0f, 1f, v));
            }
            var panel = MeshGen.Param(F, 24, 10, wrapU: false, wrapV: false, flip: true);
            var pv = panel.vertices; var pg = panel.normals;
            var pn = new Vector3[pv.Length]; var pt = new Vector4[pv.Length]; var puv = new Vector2[pv.Length]; var puv2 = new Vector2[pv.Length];
            for (int idx = 0; idx < pv.Length; idx++)
            {
                int i = idx % 25, j = idx / 25;
                float u = i / 24f, v = j / 10f;
                Vector3 dir = pv[idx].normalized;
                Vector3 gn = pg[idx]; if (Vector3.Dot(gn, dir) < 0f) gn = -gn;
                pt[idx] = new Vector4(gn.x, gn.y, gn.z, 1f);
                HairShape.Polar(dir, out float phi, out float theta);
                pn[idx] = new Vector3(Mathf.Cos(phi) * Mathf.Sin(theta), -Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Cos(theta)); // down the panel
                puv[idx] = new Vector2(Mathf.Lerp(strip.x, strip.y, Mathf.Repeat(u * 3f, 1f)), Mathf.Lerp(HairSim.AtlasVRoot, HairSim.AtlasVTip, v));
                puv2[idx] = new Vector2(v, 0f);
                pv[idx] *= _cosScale;
            }
            panel.vertices = pv; panel.normals = pn; panel.tangents = pt; panel.uv = puv; panel.uv2 = puv2; panel.RecalculateBounds();
            Piece(head, panel, m.TuftWith(0.55f, 0.3f));
            var def = new HairSim.HairDef
            {
                root = HairSim.RootMode.FrontHairline, strands = 18, nodes = 5, length = 0.06f, fan = 1, staticToHead = false,
                stiffness = 0.3f, flow = new Vector3(0f, -1f, 0.12f), curl = 0.008f, jitter = 0.12f, thickness = 0.02f,
                atlas = HairSim.Atlas.Long, rootRadiusAt = ShellRootFn(ShellBob, 0.003f), normalBlend = 0.15f, hugMax = 0.03f,
            };
            Sim(head, def, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildMohawk(Transform head, HairMats m)
        {
            // Stubble band either side of the crest.
            var c = m.Color; c.a = 0.75f;
            var band = MeshGen.Param((u, v) =>
            {
                float a = Mathf.Lerp(-1.3f, 1.3f, u);
                float beta = Mathf.Lerp(-0.34f, 0.34f, v);
                Vector3 d = new Vector3(Mathf.Sin(beta), Mathf.Cos(beta) * Mathf.Cos(a), Mathf.Cos(beta) * Mathf.Sin(a));
                return d * (HeadR + 0.002f) * _cosScale;
            }, 26, 8, flip: false);
            {
                var bv = band.vertices; var bc = new Color32[bv.Length]; var bn = new Vector3[bv.Length];
                for (int idx = 0; idx < bv.Length; idx++)
                {
                    int i = idx % 27, j = idx / 27; float u = i / 26f, v = j / 8f;
                    float beta = Mathf.Abs(Mathf.Lerp(-0.34f, 0.34f, v));
                    float alpha = 1f - Smooth(0.20f, 0.34f, beta);
                    alpha *= Smooth(0f, 0.12f, u) * Smooth(0f, 0.12f, 1f - u);   // fade the ends
                    bc[idx] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                    bn[idx] = bv[idx].normalized;
                }
                band.colors32 = bc; band.normals = bn;
            }
            Piece(head, band, m.Own(Make.Decal(c, Make.Stipple, 4f, 1f)), castShadows: false);

            // The fin: a lofted triangular section along the sagittal arc with serrated teeth.
            const int teeth = 12;
            float Height(float a)
            {
                float k = Mathf.Clamp01(1f - (Mathf.Abs(a) / 1.15f) * (Mathf.Abs(a) / 1.15f));
                float h = Mathf.Lerp(0.006f, 0.115f, Mathf.Pow(k, 0.6f));
                float tri = Mathf.Abs(Mathf.Repeat((a + 1.15f) / 2.3f * teeth, 1f) * 2f - 1f);   // 1 at tooth tips, 0 between
                return h - 0.010f * (1f - tri);
            }
            Vector2 stripFin = HairSim.AtlasStripsU[3];
            var fin = MeshGen.Param((u, v) =>
            {
                float a = Mathf.Lerp(-1.15f, 1.15f, u);
                Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                float h = Height(a);
                // Triangle loop: left base -> apex -> right base -> (under) -> left base.
                float x, y;
                if (v < 1f / 3f) { float t = v * 3f; x = Mathf.Lerp(-0.024f, -0.002f, t); y = t * h; }
                else if (v < 2f / 3f) { float t = (v - 1f / 3f) * 3f; x = Mathf.Lerp(0.002f, 0.024f, t); y = (1f - t) * h; }
                else { float t = (v - 2f / 3f) * 3f; x = Mathf.Lerp(0.024f, -0.024f, t); y = -0.004f; }
                return (d * (HeadR + 0.002f + y) + Vector3.right * x) * _cosScale;
            }, 48, 12, wrapU: false, wrapV: true, flip: false);
            {
                var fv = fin.vertices; var fg = fin.normals;
                var fn = new Vector3[fv.Length]; var ft = new Vector4[fv.Length]; var fuv = new Vector2[fv.Length]; var fuv2 = new Vector2[fv.Length];
                for (int idx = 0; idx < fv.Length; idx++)
                {
                    int i = idx % 49, j = idx / 49;
                    float u = i / 48f, v = j / 12f;
                    float a = Mathf.Lerp(-1.15f, 1.15f, u);
                    Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                    float h = Mathf.Max(Height(a), 1e-3f);
                    float y = v < 1f / 3f ? v * 3f * h : v < 2f / 3f ? (1f - (v - 1f / 3f) * 3f) * h : 0f;
                    float tip = Mathf.Clamp01(y / h);
                    fn[idx] = d;                                   // strands run up the fin
                    Vector3 gn = fg[idx]; ft[idx] = new Vector4(gn.x, gn.y, gn.z, 1f);
                    fuv[idx] = new Vector2(Mathf.Lerp(stripFin.x, stripFin.y, Mathf.Repeat(u * 6f, 1f)), Mathf.Lerp(HairSim.AtlasVRoot, HairSim.AtlasVTip, tip));
                    fuv2[idx] = new Vector2(tip, 0f);
                }
                fin.normals = fn; fin.tangents = ft; fin.uv = fuv; fin.uv2 = fuv2;
            }
            Piece(head, fin, m.TuftWith(0.82f, 0.4f));

            // Crest cards rooted at the tooth tips, for wobble.
            var roots = new Vector3[teeth]; var dirs = new Vector3[teeth];
            for (int k = 0; k < teeth; k++)
            {
                float a = Mathf.Lerp(-1.0f, 1.0f, (k + 0.5f) / teeth);
                Vector3 d = new Vector3(0f, Mathf.Cos(a), Mathf.Sin(a));
                roots[k] = d * (HeadR + 0.002f + Height(a) - 0.012f) * _cosScale;
                dirs[k] = Quaternion.AngleAxis(k % 2 == 0 ? 15f : -15f, new Vector3(0f, -Mathf.Sin(a), Mathf.Cos(a))) * d;
            }
            var crest = new HairSim.HairDef
            {
                root = HairSim.RootMode.Explicit, roots = roots, dirs = dirs, nodes = 5, length = 0.06f, fan = 1,
                stiffness = 0.85f, flow = Vector3.up, jitter = 0.05f, thickness = 0.025f, atlas = HairSim.Atlas.Tuft, strips = 0b0100,
            };
            Sim(head, crest, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildMessy(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellMessy);
            var stat = new HairSim.HairDef
            {
                root = HairSim.RootMode.Hairline, strands = 140, nodes = 4, length = 0.065f, fan = 1, staticToHead = true,
                stiffness = 1f, flow = new Vector3(0f, 0.35f, 0f), curl = 0.015f, jitter = 0.5f, thickness = 0.03f,
                atlas = HairSim.Atlas.Tuft, strips = 0b1010, rootRadiusAt = ShellRootFn(ShellMessy), normalBlend = 0.3f,
                growthClampDir = Vector3.forward, growthClampDot = 0.3f,
            };
            Sim(head, stat, m, HeadR * _cosScale, Quaternion.identity);
            var dyn = new HairSim.HairDef
            {
                root = HairSim.RootMode.Hairline, strands = 24, nodes = 5, length = 0.085f, fan = 1, staticToHead = false,
                stiffness = 0.55f, flow = new Vector3(0f, 0.5f, 0f), curl = 0.01f, jitter = 0.6f, thickness = 0.025f,
                atlas = HairSim.Atlas.Tuft, strips = 0b1010, rootRadiusAt = ShellRootFn(ShellMessy), normalBlend = 0.3f, hugMax = 0.035f,
                growthClampDir = Vector3.forward, growthClampDot = 0.3f,
            };
            Sim(head, dyn, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildCurly(Transform head, HairMats m)
        {
            HairShell(head, m.CapWith(0.6f), ShellCurly);
            var rng = new Lcg(777);
            var ribbons = new List<Mesh>();
            var tubes = new List<Mesh>();
            Vector2 wavy = HairSim.AtlasStripsU[0];
            for (int i = 0; i < 140; i++)
            {
                Vector3 dir = HairlineRoot(ref rng, 0.12f, 1.5f);
                HairShape.Polar(dir, out float phi, out _);
                float surf = HairShape.ShellRadius(dir, ShellCurly, HeadR);
                Vector3 axis = Tilt(dir, new Vector3(rng.Sym(), rng.Sym(), rng.Sym()), rng.Range(25f, 40f));
                float radius = rng.Range(0.009f, 0.012f);
                float turns = phi > 1.2f ? 1f : rng.Range(1.25f, 1.75f);
                Vector3 centre = dir * (surf - 0.003f) - axis * 0.002f;
                var path = MeshGen.Helix(centre, axis, radius, 0.009f, turns, 16, rng.Next() * 6.28f);
                bool tube = i >= 110;
                if (!tube) ribbons.Add(MeshGen.Ribbon(path, 0.009f, Vector3.zero, wavy, HairSim.AtlasVRoot, HairSim.AtlasVTip, 0.3f));
                else tubes.Add(MeshGen.Tube(path, rng.Range(0.0035f, 0.004f), 6, false, true));
            }
            var rib = MeshGen.Combine(ribbons.ToArray());
            MeshGen.Transform(rib, Vector3.zero, Quaternion.identity, Vector3.one * _cosScale);
            Piece(head, rib, m.TuftWith(0.35f), castShadows: false);
            var tub = MeshGen.Combine(tubes.ToArray());
            MeshGen.Transform(tub, Vector3.zero, Quaternion.identity, Vector3.one * _cosScale);
            Piece(head, tub, m.Cap);
        }

        static void BuildAfro(Transform head, HairMats m)
        {
            HairShell(head, m.CapWith(0.75f, 0.04f), ShellAfro);
            var fuzz = new HairSim.HairDef
            {
                root = HairSim.RootMode.Hairline, strands = 700, nodes = 3, length = 0.022f, fan = 1, staticToHead = true,
                stiffness = 1f, flow = Vector3.up, jitter = 0.5f, thickness = 0.016f,
                atlas = HairSim.Atlas.Tuft, strips = 0b1000, rootRadiusAt = ShellRootFn(ShellAfro, -0.004f), normalBlend = 0.9f,
            };
            Sim(head, fuzz, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildPonytail(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellSleekTie);
            var tie = MeshGen.Torus(0.018f, 0.006f, 24, 8);
            MeshGen.Transform(tie, TieDir * (HairShape.ShellRadius(TieDir, ShellSleekTie, HeadR) + 0.004f) * _cosScale,
                              Quaternion.FromToRotation(Vector3.up, TieDir), Vector3.one * _cosScale);
            Piece(head, tie, m.Own(Make.Mat(new Color(0.12f, 0.10f, 0.10f), 0.3f)));
            var tail = new HairSim.HairDef
            {
                root = HairSim.RootMode.TieCluster, tieDir = TieDir, strands = 30, nodes = 9, length = 0.32f, fan = 4, staticToHead = false,
                stiffness = 0.15f, flow = new Vector3(0f, -1f, -0.1f), curl = 0.022f, jitter = 0.16f, thickness = 0.03f,
                atlas = HairSim.Atlas.Long, rootRadiusAt = ShellRootFn(ShellSleekTie, 0.004f), bundle = 0.5f, bundleRadius = 0.018f, normalBlend = 0.25f,
            };
            Sim(head, tail, m, HeadR * _cosScale, Quaternion.identity);
        }

        static void BuildManBun(Transform head, HairMats m)
        {
            HairShell(head, m.Cap, ShellSleekBun);
            float baseR = HairShape.ShellRadius(BunDir, ShellSleekBun, HeadR) - 0.004f;
            var rot = Quaternion.FromToRotation(Vector3.up, BunDir);
            var bun = MeshGen.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(0.038f, 0f), new Vector2(0.056f, 0.012f), new Vector2(0.055f, 0.028f),
                new Vector2(0.038f, 0.040f), new Vector2(0.015f, 0.038f), new Vector2(0f, 0.033f),
            }, 28);
            // Circumferential comb: the sheen rings the knot.
            {
                var bv = bun.vertices; var bg = bun.normals; var bn = new Vector3[bv.Length]; var bt = new Vector4[bv.Length];
                for (int i = 0; i < bv.Length; i++)
                {
                    Vector3 radial = new Vector3(bv[i].x, 0f, bv[i].z);
                    Vector3 t = Vector3.Cross(Vector3.up, radial);
                    bn[i] = t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
                    bt[i] = new Vector4(bg[i].x, bg[i].y, bg[i].z, 1f);
                }
                bun.normals = bn; bun.tangents = bt;
            }
            var coilParts = new List<Mesh>();
            for (int k = 0; k < 3; k++)
            {
                var path = MeshGen.Helix(new Vector3(0f, 0.010f + k * 0.009f, 0f), Vector3.up, 0.055f - k * 0.005f, 0.007f, 1.5f, 24, k * 2.1f);
                coilParts.Add(MeshGen.Tube(path, 0.0025f, 6, true, true));
            }
            var coils = MeshGen.Combine(coilParts.ToArray());
            SetNormalsTangents(coils, Vector3.forward);
            var knot = MeshGen.Combine(bun, coils);
            MeshGen.Transform(knot, BunDir * baseR * _cosScale, rot, Vector3.one * _cosScale);
            Piece(head, knot, m.CapWith(0.6f));
            var tie = MeshGen.Torus(0.038f, 0.004f, 28, 8);
            MeshGen.Transform(tie, BunDir * (baseR + 0.003f) * _cosScale, rot, Vector3.one * _cosScale);
            Piece(head, tie, m.Own(Make.Mat(new Color(0.12f, 0.10f, 0.10f), 0.3f)));
        }

        // ---- the catalog (index 0 = Bald; 13 entries, order is wire state) --------------------
        // Def is the card style the HORSE mane builds (and, for Shoulder Length / Long, the human
        // style too). Human short styles build through Extra, which owns their shell, geometry
        // and any sims; Human = true marks entries whose Def is for the mane only.
        static readonly List<HairEntry> _hair = new List<HairEntry>
        {
            new HairEntry { Name = "Bald", Group = HairGroup.Short, Bald = true },

            // SHORT --------------------------------------------------------------------------
            new HairEntry { Name = "Buzz", Group = HairGroup.Short, Extra = BuildBuzz, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 40, nodes = 3, length = 0.02f, fan = 1, staticToHead = true,
                stiffness = 1f, flow = new Vector3(0f, 1f, 0f), jitter = 0.3f, thickness = 0.015f, atlas = HairSim.Atlas.Tuft } },
            new HairEntry { Name = "Crew Cut", Group = HairGroup.Short, Extra = BuildCrewCut, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 130, nodes = 3, length = 0.075f, fan = 4, staticToHead = true,
                stiffness = 0.92f, flow = new Vector3(0f, 1f, 0.15f), curl = 0f, jitter = 0.2f, thickness = 0.05f } },
            new HairEntry { Name = "Spiky", Group = HairGroup.Short, Extra = BuildSpiky, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 95, nodes = 3, length = 0.17f, fan = 3, staticToHead = false,
                stiffness = 0.92f, flow = new Vector3(0f, 1f, 0f), curl = 0f, jitter = 0.55f, thickness = 0.035f } },
            new HairEntry { Name = "Fringe", Group = HairGroup.Short, Extra = BuildFringe, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.FrontSweep, strands = 70, nodes = 5, length = 0.19f, fan = 5, staticToHead = false,
                stiffness = 0.3f, flow = new Vector3(0f, -0.75f, 0.6f), curl = 0.012f, jitter = 0.15f, thickness = 0.055f } },
            new HairEntry { Name = "Mohawk", Group = HairGroup.Medium, Extra = BuildMohawk, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Strip, strands = 34, nodes = 5, length = 0.26f, fan = 5, staticToHead = false,
                stiffness = 0.88f, flow = new Vector3(0f, 1f, 0f), curl = 0.008f, jitter = 0.08f, thickness = 0.05f } },

            // MEDIUM -------------------------------------------------------------------------
            new HairEntry { Name = "Messy", Group = HairGroup.Medium, Extra = BuildMessy, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 95, nodes = 4, length = 0.16f, fan = 4, staticToHead = false,
                stiffness = 0.32f, flow = new Vector3(0f, 0.5f, 0f), curl = 0.02f, jitter = 0.7f, thickness = 0.05f } },
            new HairEntry { Name = "Curly", Group = HairGroup.Medium, Extra = BuildCurly, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 110, nodes = 6, length = 0.17f, fan = 4, staticToHead = false,
                stiffness = 0.42f, flow = new Vector3(0f, 0.3f, 0f), curl = 0.085f, jitter = 0.5f, thickness = 0.05f } },
            new HairEntry { Name = "Afro", Group = HairGroup.Medium, Extra = BuildAfro, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.Crown, strands = 130, nodes = 4, length = 0.19f, fan = 5, staticToHead = false,
                stiffness = 0.6f, flow = new Vector3(0f, 1f, 0f), curl = 0.055f, jitter = 0.7f, thickness = 0.08f } },

            // LONG ---------------------------------------------------------------------------
            new HairEntry { Name = "Ponytail", Group = HairGroup.Long, Extra = BuildPonytail, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.BackCluster, strands = 42, nodes = 11, length = 0.52f, fan = 4, staticToHead = false,
                stiffness = 0.15f, flow = new Vector3(0f, -1f, -0.15f), curl = 0.01f, jitter = 0.05f, thickness = 0.05f } },
            new HairEntry { Name = "Man Bun", Group = HairGroup.Long, Extra = BuildManBun, HumanOnlyDef = true, Def = new HairSim.HairDef {
                root = HairSim.RootMode.BackCluster, strands = 42, nodes = 11, length = 0.52f, fan = 4, staticToHead = false,
                stiffness = 0.15f, flow = new Vector3(0f, -1f, -0.15f), curl = 0.01f, jitter = 0.05f, thickness = 0.05f } },
            // Shoulder Length and Long: unchanged card styles (the reference the short ones meet).
            new HairEntry { Name = "Shoulder Length", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.TopSidesBack, strands = 66, nodes = 10, length = 0.36f, fan = 5, staticToHead = false,
                stiffness = 0.18f, flow = new Vector3(0f, -1f, -0.05f), curl = 0.03f, jitter = 0.14f, thickness = 0.065f } },
            new HairEntry { Name = "Long", Group = HairGroup.Long, Def = new HairSim.HairDef {
                root = HairSim.RootMode.TopSidesBack, strands = 80, nodes = 12, length = 0.54f, fan = 6, staticToHead = false,
                stiffness = 0.07f, flow = new Vector3(0f, -1f, -0.06f), curl = 0.018f, jitter = 0.1f, thickness = 0.055f } },
        };
    }
}
