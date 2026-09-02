using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Loads and places imported decorative models (trees, stadium furniture, skyline) from
    /// Assets/Resources/Props. Everything here is COSMETIC: nothing it produces has a collider, nothing
    /// it produces has an Update, and every instance is marked static so the caller can fold the lot
    /// into one combined mesh with StaticBatchingUtility.Combine.
    ///
    /// The models are Kenney CC0 kits (nature-kit, racing-kit, city kits, car-kit). They are flat
    /// coloured with NO textures at all, which is why they sit well beside this project's primitives:
    /// same shader (Standard), same absence of texture detail, just far better silhouettes.
    ///
    /// THREE PROPERTIES OF THESE ASSETS FORCED THE DESIGN HERE, all measured rather than assumed:
    ///
    ///   1. IMPORT SCALE IS NOT CONSISTENT ACROSS PACKS. Every nature-kit tree imports 10.00 units tall,
    ///      but racing-kit's grandStandCovered imports 1.00 tall. A caller must therefore never assume a
    ///      size: scale is derived from the prefab's MEASURED bounds, and callers ask in metres.
    ///   2. MATERIAL SLOT ORDER VARIES BETWEEN MODELS. tree_oak is [leafsGreen, woodBark] but
    ///      tree_default is [woodBark, leafsGreen]. Recolouring by slot INDEX would paint bark colour
    ///      onto the foliage of roughly half the set, so slots are matched by NAME substring instead.
    ///   3. THE PIVOT IS NOT AT THE BASE. A tree's mesh bounds centre sits at y = 5 of its 10 units, so
    ///      placing by transform position alone buries half the trunk. Placement subtracts the measured
    ///      base offset, so a caller passes a GROUND point and gets a prop standing on it.
    ///
    /// Verified live in the editor before this shipped: 20 instances asked for 6 m came out at exactly
    /// 6.00 m with base y = 0.000, and StaticBatchingUtility.Combine batched 20/20 even though the
    /// imported meshes report isReadable = false (which was the open question - it turns out not to
    /// block runtime batching).
    ///
    /// MATERIALS BELONG TO THE CALLER, AND MUST BE SHARED. Pass one material per colour role for the
    /// whole venue, never one per prop: sixty trees sharing two materials collapse into a couple of draw
    /// calls, whereas a material per instance defeats the batch and costs a draw call each. This class
    /// deliberately creates no materials, so it never owns their lifetime either - whichever builder made
    /// them frees them.
    /// </summary>
    public static class PropKit
    {
        // Loaded prefabs by Resources path. Cleared on a domain reload like any static, which is fine:
        // Resources.Load is cheap on a repeat call and this exists only to avoid sixty lookups while
        // building a single ring.
        static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();
        // Paths already known to be absent. Without this, a missing asset costs a failed Resources.Load
        // per prop and logs the identical warning sixty times while building one venue.
        static readonly HashSet<string> _absent = new HashSet<string>();
        // Measured local-space bounds per prefab, so the corner walk below runs once per model, not once
        // per instance.
        static readonly Dictionary<string, Bounds> _bounds = new Dictionary<string, Bounds>();

        /// <summary>A colour role: any material slot whose name contains <paramref name="match"/>
        /// (case-insensitive) is rebound to <paramref name="mat"/>. First match wins, so order the array
        /// most-specific first.</summary>
        public struct Paint
        {
            public string match;
            public Material mat;
            public Paint(string match, Material mat) { this.match = match; this.mat = mat; }
        }

        /// <summary>The prefab at a Resources path under Props, or null when it is not in this build.
        /// Never throws, and never logs more than once per missing path.</summary>
        public static GameObject Load(string path)
        {
            if (string.IsNullOrEmpty(path) || _absent.Contains(path)) return null;
            GameObject go;
            if (_cache.TryGetValue(path, out go)) return go;
            go = Resources.Load<GameObject>(path);
            if (go == null)
            {
                _absent.Add(path);
                Debug.LogWarning("PropKit: no model at Resources/" + path + " - falling back to primitives.");
                return null;
            }
            _cache[path] = go;
            return go;
        }

        /// <summary>True when the model is present. Lets a builder choose model-or-primitive BEFORE it
        /// starts consuming random numbers, so a missing asset cannot shift a deterministic layout.</summary>
        public static bool Has(string path) { return Load(path) != null; }

        /// <summary>
        /// Place a model standing on <paramref name="ground"/>, scaled so its total height is
        /// <paramref name="height"/> metres, turned <paramref name="yaw"/> degrees about Y.
        /// Returns null when the model is absent, so the caller can fall back to a primitive.
        /// </summary>
        public static GameObject Place(string path, Vector3 ground, float height, float yaw,
                                       Transform parent, Paint[] palette)
        {
            GameObject prefab = Load(path);
            if (prefab == null) return null;

            Bounds lb;
            if (!MeasuredBounds(path, prefab, out lb) || lb.size.y <= 1e-4f) return null;

            // Uniform scale from the measured height. Uniform matters twice over: static batching rejects
            // meshes carrying a non-uniform or mirrored scale, and a stretched tree looks wrong anyway.
            float k = height / lb.size.y;

            GameObject go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = prefab.name;
            go.transform.localScale = new Vector3(k, k, k);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // Drop by the scaled base offset so the caller's point is the GROUND rather than the mesh
            // centre. Only valid because the scale above is uniform.
            go.transform.position = ground - new Vector3(0f, (lb.center.y - lb.extents.y) * k, 0f);

            Recolor(go, palette);

            // Decoration must never be collidable. The imported prefabs measured zero colliders, so this
            // is purely a guard against an importer setting changing under us later.
            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) UnityEngine.Object.Destroy(cols[i]);

            // Marked static for the caller's Combine, which must run only AFTER every instance is placed:
            // batching bakes the transform, so anything moved afterwards will not appear to move.
            go.isStatic = true;
            return go;
        }

        /// <summary>Convenience for the common two-tone case (foliage plus trunk). The two substrings
        /// cover every nature-kit slot name in the imported set: leafsGreen / leafsDark / leafsFall for
        /// foliage, and woodBark / woodBarkDark / woodBirch for trunks.</summary>
        public static GameObject PlaceTree(string path, Vector3 ground, float height, float yaw,
                                           Transform parent, Material foliage, Material trunk)
        {
            return Place(path, ground, height, yaw, parent, new Paint[]
            {
                new Paint("leaf", foliage),
                new Paint("wood", trunk),
            });
        }

        // Rebind material slots by NAME. Anything unmatched keeps the model's own imported material,
        // which is deliberate: a grandstand carries slots like glass and road that a two-colour palette
        // has no opinion about, and Kenney's own colour for those is usually the right answer.
        public static void Recolor(GameObject go, Paint[] palette)
        {
            if (palette == null || palette.Length == 0) return;
            MeshRenderer[] rends = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < rends.Length; r++)
            {
                // sharedMaterials on an INSTANCE, not on the prefab. This rebinds this renderer's slots
                // and leaves the imported asset on disk untouched; assigning to the asset's own materials
                // would leak one venue's palette into every other venue and dirty the .fbx.
                Material[] slots = rends[r].sharedMaterials;
                bool touched = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    string n = slots[i] == null ? string.Empty : slots[i].name.ToLowerInvariant();
                    for (int p = 0; p < palette.Length; p++)
                    {
                        if (palette[p].mat == null || string.IsNullOrEmpty(palette[p].match)) continue;
                        if (n.IndexOf(palette[p].match.ToLowerInvariant()) < 0) continue;
                        slots[i] = palette[p].mat;
                        touched = true;
                        break;
                    }
                }
                if (touched) rends[r].sharedMaterials = slots;
            }
        }

        // Bounds of every mesh in the prefab, expressed in the PREFAB ROOT's local space. Walking the
        // eight corners of each child's mesh bounds through that child's transform is the only approach
        // that stays correct for a prefab whose parts hang off offset or rotated children: reading
        // mesh.bounds off the first MeshFilter would measure one part of a multi-part model and scale
        // everything else by the wrong factor.
        /// <summary>Public wrapper for the bounds measurement below, for CosmeticMesh.</summary>
        public static bool TryMeasure(string key, GameObject prefab, out Bounds b) => MeasuredBounds(key, prefab, out b);

        static bool MeasuredBounds(string key, GameObject prefab, out Bounds b)
        {
            if (_bounds.TryGetValue(key, out b)) return true;
            bool any = false;
            b = new Bounds();
            Transform root = prefab.transform;
            MeshFilter[] mfs = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int f = 0; f < mfs.Length; f++)
            {
                Mesh mesh = mfs[f].sharedMesh;
                if (mesh == null) continue;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * mb.extents.x,
                        ((i & 2) == 0 ? -1f : 1f) * mb.extents.y,
                        ((i & 4) == 0 ? -1f : 1f) * mb.extents.z);
                    Vector3 lp = root.InverseTransformPoint(mfs[f].transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                    else b.Encapsulate(lp);
                }
            }
            if (any) _bounds[key] = b;
            return any;
        }
    }
}
