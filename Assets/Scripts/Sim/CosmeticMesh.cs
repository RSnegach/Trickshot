using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Mounts an IMPORTED model (Resources/Cosmetics/...) as a head cosmetic: the downloaded hats,
    /// glasses, masks and props that replaced the old sphere-and-box assemblies. Same rules as
    /// <see cref="PropKit"/>, which this leans on: scale from the model's MEASURED bounds (the
    /// converter normalises every model to a 1-unit largest extent, but never trust that), never a
    /// collider, and material slots rebound by NAME so the player's accessory colour lands on the
    /// dominant material while lenses, bands and metal keep their own.
    ///
    /// Converted OBJ materials are named "<source>_<rrggbb>" by tools/glb2obj.py, so a paint rule can
    /// match either the source name ("lens") or the baked colour ("_0f0f0f").
    /// </summary>
    public static class CosmeticMesh
    {
        /// <summary>Where the model's measured bounds sit relative to the mount point.</summary>
        public enum Anchor { Centre, Bottom, Top }

        /// <summary>Which measured extent <c>size</c> applies to.</summary>
        public enum Axis { Largest, X, Y, Z }

        /// <summary>
        /// Instantiate Resources/<paramref name="path"/> under <paramref name="parent"/> (the head bone),
        /// uniformly scaled so the chosen extent is <paramref name="size"/> metres, with the anchor point
        /// at <paramref name="localPos"/> and the model turned by <paramref name="euler"/>. The first
        /// paint rule whose substring matches a slot name wins; unmatched slots keep the imported
        /// material. Returns null (and logs once) when the model is missing so the caller can fall back.
        /// </summary>
        public static GameObject Mount(Transform parent, string path, float size, Axis axis, Anchor anchor,
                                       Vector3 localPos, Vector3 euler, PropKit.Paint[] paints)
        {
            var prefab = PropKit.Load(path);
            if (prefab == null) return null;
            Bounds lb;
            if (!PropKit.TryMeasure(path, prefab, out lb)) return null;
            float extent;
            switch (axis)
            {
                case Axis.X: extent = lb.size.x; break;
                case Axis.Y: extent = lb.size.y; break;
                case Axis.Z: extent = lb.size.z; break;
                default: extent = Mathf.Max(lb.size.x, Mathf.Max(lb.size.y, lb.size.z)); break;
            }
            if (extent <= 1e-5f) return null;
            float k = size / extent;

            // A pivot object carries the pose; the model hangs under it offset so the requested
            // anchor of its bounds lands exactly on the pivot. That keeps the catalog numbers about
            // where the object SITS (brim on the crown, bridge on the nose) rather than about
            // whatever origin the source artist happened to use.
            var pivot = new GameObject("cz_" + prefab.name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;
            pivot.transform.localRotation = Quaternion.Euler(euler);
            pivot.transform.localScale = Vector3.one;

            var go = Object.Instantiate(prefab, pivot.transform);
            go.name = prefab.name;
            Vector3 anchorLocal = lb.center;
            if (anchor == Anchor.Bottom) anchorLocal.y = lb.min.y;
            else if (anchor == Anchor.Top) anchorLocal.y = lb.max.y;
            go.transform.localScale = Vector3.one * k;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localPosition = -anchorLocal * k;

            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Object.Destroy(cols[i]);
            var rends = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                rends[i].receiveShadows = true;
            }
            PropKit.Recolor(go, paints);
            return pivot;
        }

        /// <summary>The common case: the whole model takes one tint except slots matching the keep
        /// list, which stay as imported.</summary>
        public static GameObject Mount(Transform parent, string path, float size, Axis axis, Anchor anchor,
                                       Vector3 localPos, Vector3 euler, Material tint, params string[] keep)
        {
            var prefab = PropKit.Load(path);
            if (prefab == null) return null;
            // Build a paint list that maps every slot NOT in keep to the tint. PropKit.Recolor matches by
            // substring, so enumerate the prefab's real slot names and emit an exact rule for each.
            var rends = prefab.GetComponentsInChildren<MeshRenderer>(true);
            var rules = new System.Collections.Generic.List<PropKit.Paint>();
            for (int r = 0; r < rends.Length; r++)
            {
                var slots = rends[r].sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string n = slots[i].name;
                    bool kept = false;
                    if (keep != null)
                        for (int k = 0; k < keep.Length; k++)
                            if (!string.IsNullOrEmpty(keep[k]) && n.IndexOf(keep[k], System.StringComparison.OrdinalIgnoreCase) >= 0) { kept = true; break; }
                    if (!kept) rules.Add(new PropKit.Paint(n, tint));
                }
            }
            return Mount(parent, path, size, axis, anchor, localPos, euler, rules.ToArray());
        }
    }
}
