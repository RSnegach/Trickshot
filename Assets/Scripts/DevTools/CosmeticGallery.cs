#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// EDITOR-ONLY review tool. In play mode, builds every cosmetic (human hair / facial /
    /// accessory, horse mane / markings / tack, elephant ears / tusks / tack) on a kinematic
    /// display body at a far-off stage and photographs each from a few fixed angles into PNGs,
    /// so the catalog can be reviewed as rendered rather than as code.
    ///
    /// Start it from the MCP bridge or the console with:
    ///     Trickshot.CosmeticGallery.Begin(outDir, filter);
    /// filter is a comma list of job prefixes ("human_hair,horse_tack"), or empty for everything.
    /// Poll <see cref="Status"/> / <see cref="Done"/>.
    /// </summary>
    public class CosmeticGallery : MonoBehaviour
    {
        public static string Status = "idle";
        public static bool Done;
        public static int Written;

        static readonly Vector3 Stage = new Vector3(2000f, 0f, 2000f);
        const int Size = 640;

        string _outDir;
        string[] _filter;
        Camera _cam;
        RenderTexture _rt;
        Texture2D _tex;

        public static string Begin(string outDir, string filter = "", bool lightweight = false)
        {
            var old = FindAnyObjectByType<CosmeticGallery>();
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("CosmeticGallery");
            var g = go.AddComponent<CosmeticGallery>();
            g._outDir = outDir;
            g._filter = string.IsNullOrEmpty(filter) ? new string[0] : filter.Split(',');
            Done = false; Written = 0; Status = "starting";
            // The editor compiles new shader variants asynchronously and draws a CYAN placeholder
            // until they are ready, which is exactly what a first capture of a new material shows.
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            g.StartCoroutine(g.Run());
            return "started";
        }

        /// <summary>
        /// One-off: photograph an arbitrary GameObject (e.g. a MeshGen test piece) from four angles.
        /// Builds the rig on demand; the object is moved to the stage and destroyed afterwards
        /// unless keep is true. Returns the number of images written.
        /// </summary>
        public static int SnapObject(GameObject go, string outDir, string name, float dist = 1.2f, bool keep = false)
        {
            var g = FindAnyObjectByType<CosmeticGallery>();
            if (g == null)
            {
                var host = new GameObject("CosmeticGallery");
                g = host.AddComponent<CosmeticGallery>();
                g._outDir = outDir;
                g._filter = new string[0];
            }
            g._outDir = outDir;
            Directory.CreateDirectory(outDir);
            if (g._cam == null) g.BuildRig();
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            go.transform.position = Stage + Vector3.up * 1.0f;
            var rends = go.GetComponentsInChildren<Renderer>();
            Bounds b = rends.Length > 0 ? rends[0].bounds : new Bounds(go.transform.position, Vector3.one * 0.3f);
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var views = new[] { V("front", 0f, 10f, dist, false), V("q34", 40f, 25f, dist, false), V("side", 90f, 5f, dist, false), V("top", 20f, 70f, dist, false) };
            WarmShaders(go);
            g._cam.transform.position = b.center - Vector3.forward * dist; g._cam.transform.LookAt(b.center);
            g._cam.targetTexture = g._rt; g._cam.Render(); g._cam.targetTexture = null;
            for (int w = 0; w < 200 && UnityEditor.ShaderUtil.anythingCompiling; w++) System.Threading.Thread.Sleep(25);
            int before = Written;
            foreach (var v in views)
            {
                var rot = Quaternion.Euler(v.Pitch, 180f + v.Yaw, 0f);
                var dir = rot * Vector3.forward;
                g._cam.transform.position = b.center - dir * v.Dist;
                g._cam.transform.rotation = rot;
                g.Capture(Path.Combine(outDir, name + "_" + v.Name + ".png"));
            }
            if (!keep) DestroyImmediate(go);   // immediate: several snaps can run in one frame
            return Written - before;
        }

        void BuildRig()
        {
            var camGo = new GameObject("GalleryCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.30f, 0.33f, 0.38f);
            _cam.nearClipPlane = 0.02f;
            _cam.farClipPlane = 60f;
            _cam.fieldOfView = 30f;
            _cam.enabled = false;
            _rt = new RenderTexture(Size, Size, 24);
            _tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);

            var lgo = new GameObject("GalleryKey");
            lgo.transform.SetParent(transform, false);
            var key = lgo.AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 0.95f;
            key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            key.shadows = LightShadows.Soft;
            var fgo = new GameObject("GalleryFill");
            fgo.transform.SetParent(transform, false);
            var fill = fgo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.30f;
            fill.color = new Color(0.85f, 0.9f, 1f);
            fill.transform.rotation = Quaternion.Euler(15f, -40f, 0f);
            fill.shadows = LightShadows.None;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.36f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "GalleryFloor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Stage + new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            floor.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.16f, 0.30f, 0.16f), 0.05f);
        }

        struct View { public string Name; public float Yaw; public float Pitch; public float Dist; public bool Head; public float LiftY; }
        struct Job { public string Id; public PlayerAppearance App; public View[] Views; }

        static View V(string n, float yaw, float pitch, float dist, bool head, float lift = 0f)
            => new View { Name = n, Yaw = yaw, Pitch = pitch, Dist = dist, Head = head, LiftY = lift };

        // Head close-ups for a human: front, three-quarter, side, plus a chest shot.
        static readonly View[] HumanHead =
        {
            V("front", 0f, 5f, 1.05f, true),
            V("q34", 40f, 12f, 1.05f, true),
            V("side", 90f, 5f, 1.05f, true),
            V("back", 180f, 10f, 1.05f, true),
            V("body", 25f, 8f, 2.6f, true, -0.45f),
        };
        static readonly View[] Quad =
        {
            V("side", 90f, 8f, 4.2f, false, 0.3f),
            V("q34", 40f, 12f, 4.0f, false, 0.3f),
            V("front", 0f, 8f, 3.2f, false, 0.4f),
            V("head", 35f, 10f, 1.6f, true),
        };
        static readonly View[] QuadBody =
        {
            V("side", 90f, 8f, 4.2f, false, 0.3f),
            V("q34", 40f, 15f, 4.0f, false, 0.3f),
            V("front", 0f, 8f, 3.2f, false, 0.4f),
            V("rear", 200f, 12f, 3.6f, false, 0.3f),
        };

        static PlayerAppearance Base(byte species)
        {
            var a = PlayerAppearance.Default;
            a.SpeciesId = species;
            switch (species)
            {
                case 1: // horse: bay coat, black mane, white markings, brown leather tack
                    a.Skin = new Color(0.42f, 0.24f, 0.12f);
                    a.HairColor = new Color(0.08f, 0.06f, 0.05f);
                    a.FacialColor = new Color(0.95f, 0.94f, 0.90f);
                    a.AccessoryColor = new Color(0.32f, 0.20f, 0.10f);
                    break;
                case 2: // elephant: grey hide, ears in hide colour, ivory tusks, red cloth
                    a.Skin = new Color(0.46f, 0.46f, 0.48f);
                    a.HairColor = new Color(0.46f, 0.46f, 0.48f);
                    a.FacialColor = new Color(0.93f, 0.90f, 0.80f);
                    a.AccessoryColor = new Color(0.70f, 0.12f, 0.10f);
                    break;
                default:
                    a.Skin = new Color(0.85f, 0.65f, 0.52f);
                    a.HairColor = new Color(0.25f, 0.15f, 0.08f);
                    a.FacialColor = new Color(0.25f, 0.15f, 0.08f);
                    a.AccessoryColor = new Color(0.12f, 0.12f, 0.14f);
                    break;
            }
            return a;
        }

        static string Slug(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        IEnumerable<Job> Jobs()
        {
            // HUMAN
            for (int i = 0; i < Cosmetics.Hair.Count; i++)
            {
                var a = Base(0); a.HairStyle = i;
                yield return new Job { Id = $"human_hair_{i:00}_{Slug(Cosmetics.Hair[i].Name)}", App = a, Views = HumanHead };
            }
            for (int i = 1; i < Cosmetics.Facial.Count; i++)
            {
                var a = Base(0); a.FacialStyle = i; a.HairStyle = 2; // crew cut so the head reads as a head
                yield return new Job { Id = $"human_facial_{i:00}_{Slug(Cosmetics.Facial[i].Name)}", App = a, Views = HumanHead };
            }
            for (int i = 1; i < Cosmetics.Accessories.Count; i++)
            {
                var a = Base(0); a.Accessory = i;
                a.HairStyle = Cosmetics.AccessoryIsHeadgear(i) ? 0 : 2;
                // Accessory colours that make sense per item.
                string n = Cosmetics.Accessories[i].Name;
                if (n.Contains("Earring") || n == "Nose Stud" || n == "Septum Ring" || n == "Eyebrow Piercing" || n == "Nipple Piercings" || n == "Chain Necklace" || n == "Monocle")
                    a.AccessoryColor = new Color(0.85f, 0.72f, 0.30f);
                else if (n == "Cap" || n == "Bucket Hat" || n == "Beret" || n == "Headband" || n == "Party Hat")
                    a.AccessoryColor = new Color(0.75f, 0.15f, 0.15f);
                else if (n == "Fedora" || n == "Cowboy Hat" || n == "Trapper Hat" || n == "Pipe" || n == "Cigar" || n == "Sombrero")
                    a.AccessoryColor = new Color(0.40f, 0.26f, 0.14f);
                else if (n == "Hockey Mask" || n == "Welding Mask" || n == "Ski Goggles")
                    a.AccessoryColor = new Color(0.90f, 0.90f, 0.88f);
                else if (n == "Venetian Mask")
                    a.AccessoryColor = new Color(0.85f, 0.70f, 0.25f);
                else if (n == "Wizard Hat")
                    a.AccessoryColor = new Color(0.20f, 0.15f, 0.55f);
                else if (n == "Lollipop")
                    a.AccessoryColor = new Color(0.90f, 0.20f, 0.40f);
                else if (n == "Toothpick" || n == "Vampire Fangs")
                    a.AccessoryColor = new Color(0.95f, 0.92f, 0.85f);
                yield return new Job { Id = $"human_acc_{i:00}_{Slug(n)}", App = a, Views = HumanHead };
            }
            // HORSE
            for (int i = 0; i < SpeciesCosmetics.Count(1, SlotKind.StyleA); i++)
            {
                var a = Base(1); a.HairStyle = i;
                yield return new Job { Id = $"horse_mane_{i:00}_{Slug(SpeciesCosmetics.Label(1, SlotKind.StyleA, i))}", App = a, Views = Quad };
            }
            for (int i = 1; i < SpeciesCosmetics.Count(1, SlotKind.StyleB); i++)
            {
                var a = Base(1); a.FacialStyle = i; a.HairStyle = 0;
                yield return new Job { Id = $"horse_mark_{i:00}_{Slug(SpeciesCosmetics.Label(1, SlotKind.StyleB, i))}", App = a, Views = Quad };
            }
            for (int i = 1; i < SpeciesCosmetics.Count(1, SlotKind.StyleC); i++)
            {
                var a = Base(1); a.Accessory = i; a.HairStyle = 0;
                yield return new Job { Id = $"horse_tack_{i:00}_{Slug(SpeciesCosmetics.Label(1, SlotKind.StyleC, i))}", App = a, Views = Quad };
            }
            // ELEPHANT
            for (int i = 0; i < SpeciesCosmetics.Count(2, SlotKind.StyleA); i++)
            {
                var a = Base(2); a.HairStyle = i; a.FacialStyle = 2;
                yield return new Job { Id = $"eleph_ears_{i:00}_{Slug(SpeciesCosmetics.Label(2, SlotKind.StyleA, i))}", App = a, Views = Quad };
            }
            for (int i = 0; i < SpeciesCosmetics.Count(2, SlotKind.StyleB); i++)
            {
                var a = Base(2); a.FacialStyle = i;
                yield return new Job { Id = $"eleph_tusk_{i:00}_{Slug(SpeciesCosmetics.Label(2, SlotKind.StyleB, i))}", App = a, Views = Quad };
            }
            for (int i = 1; i < SpeciesCosmetics.Count(2, SlotKind.StyleC); i++)
            {
                var a = Base(2); a.Accessory = i; a.FacialStyle = 2;
                yield return new Job { Id = $"eleph_tack_{i:00}_{Slug(SpeciesCosmetics.Label(2, SlotKind.StyleC, i))}", App = a, Views = QuadBody };
            }
        }

        bool Wanted(string id)
        {
            if (_filter.Length == 0) return true;
            foreach (var f in _filter) if (id.StartsWith(f.Trim())) return true;
            return false;
        }

        IEnumerator Run()
        {
            Directory.CreateDirectory(_outDir);

            BuildRig();

            int n = 0;
            foreach (var job in Jobs())
            {
                if (!Wanted(job.Id)) continue;
                n++;
                Status = $"{n}: {job.Id}";

                var root = new GameObject("GalleryBody");
                root.transform.SetParent(transform, false);
                var rag = root.AddComponent<ActiveRagdoll>();
                var torso = Make.Mat(new Color(0.55f, 0.58f, 0.66f));
                var limbs = Make.Mat(new Color(0.5f, 0.5f, 0.5f));
                var facing = Quaternion.identity;
                try
                {
                    rag.Build(Stage, facing, torso, limbs, withGloves: false, appearance: job.App);
                }
                catch (System.Exception e)
                {
                    // One broken builder must not kill the whole run: log it and shoot what exists.
                    Debug.LogError("CosmeticGallery: " + job.Id + " threw " + e);
                }
                rag.BecomeDisplayBody();
                rag.DisplaySnap(Stage, facing);

                // Let dynamic hair settle under gravity (about a second of physics).
                for (int f = 0; f < 55; f++)
                {
                    rag.DisplaySnap(Stage, facing);
                    yield return new WaitForFixedUpdate();
                }
                WarmShaders(root);
                // A variant only starts compiling when it is first RENDERED: shoot once into the
                // RenderTexture to kick every material, then wait until nothing is compiling.
                _cam.transform.position = Stage + new Vector3(0f, 1.5f, 2.5f);
                _cam.transform.LookAt(Stage + Vector3.up * 1.5f);
                _cam.targetTexture = _rt; _cam.Render(); _cam.targetTexture = null;
                for (int w = 0; w < 240 && UnityEditor.ShaderUtil.anythingCompiling; w++) yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();

                var head = rag.Phys(Bone.Head);
                var torsoT = rag.Phys(Bone.Torso);
                foreach (var v in job.Views)
                {
                    Vector3 target = (v.Head && head != null) ? head.position : (torsoT != null ? torsoT.position : Stage + Vector3.up);
                    target.y += v.LiftY;
                    // Body faces +Z; "front" (yaw 0) places the camera on +Z looking back.
                    var rot = Quaternion.Euler(v.Pitch, 180f + v.Yaw, 0f);
                    var dir = rot * Vector3.forward;                 // camera forward
                    _cam.transform.position = target - dir * v.Dist;
                    _cam.transform.rotation = rot;
                    Capture(Path.Combine(_outDir, job.Id + "_" + v.Name + ".png"));
                }

                Destroy(root);
                Destroy(torso); Destroy(limbs);
                yield return null;
            }
            Status = $"done ({n} items, {Written} images)";
            Done = true;
        }

        // Force every material pass on the object to compile NOW. Even with async compilation
        // off, a variant first used by an off-screen RenderTexture camera can still come back as
        // the cyan placeholder for a frame or two; compiling the passes synchronously is the only
        // guarantee a capture shows the real shader.
        static void WarmShaders(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    for (int p = 0; p < m.passCount; p++)
                        if (!UnityEditor.ShaderUtil.IsPassCompiled(m, p)) UnityEditor.ShaderUtil.CompilePass(m, p, true);
                }
        }

        void Capture(string path)
        {
            _cam.targetTexture = _rt;
            _cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _tex.Apply();
            RenderTexture.active = prev;
            _cam.targetTexture = null;
            File.WriteAllBytes(path, _tex.EncodeToPNG());
            Written++;
        }

        void OnDestroy()
        {
            if (_rt != null) Destroy(_rt);
            if (_tex != null) Destroy(_tex);
        }
    }
}
#endif
