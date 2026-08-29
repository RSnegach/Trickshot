using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Generated distant landforms: rolling hills, low islands, and - if one is ever wanted again -
    /// mountains with snowlines. One faceted cone per landform, built as a mesh rather than assembled
    /// from primitives.
    ///
    /// THE SNOWLINE PATH IS CURRENTLY UNUSED. A far mountain range was built on it and then removed on
    /// request; hills and islands both pass a snow altitude above their own peak, which takes the
    /// single-submesh branch. The snow code is kept because it is the non-obvious part of this file and
    /// re-deriving it would be the expensive half of putting a range back.
    ///
    /// WHY THIS IS GENERATED AND NOT DOWNLOADED, given every other prop in the surroundings is a
    /// Kenney CC0 model. The nature kit does ship rock_tallA..J, rock_largeA..F and a full cliff set,
    /// and those were the obvious candidates - but a rock scaled to 150 m reads as a rock scaled to
    /// 150 m. Mountain silhouette is the whole content of a distant range: a long shallow base, a
    /// defined ridge, a snowline that cuts horizontally across every peak at the same altitude
    /// regardless of peak height. None of that survives uniform-scaling a boulder, and none of the CC0
    /// kits carry a distant-range asset at this art style. Eight triangles of generated cone does.
    ///
    /// SNOWLINE IS THE PART THAT MAKES IT READ. It is a HORIZONTAL WORLD ALTITUDE, not a fraction of
    /// each peak's height, which is what real snowlines are: a tall peak therefore wears a lot of snow
    /// and a short one wears none at all, and the caps line up across the range. Cutting at a constant
    /// fraction instead gives every peak an identical hat and looks like a toy.
    ///
    /// The cut is baked into the mesh as two SUBMESHES, rock below and snow above, rather than as a
    /// second cone sitting on the first. A second cone would be a coincident surface and would
    /// z-fight along the whole snowline.
    ///
    /// Flat-shaded on purpose: triangles do not share vertices, so RecalculateNormals gives each facet
    /// one hard normal. That matches the primitive art the rest of the game is built from, and it is
    /// why 8 facets is enough - a smooth-shaded cone would need many more to stop looking like a cone.
    ///
    /// Everything here is collider-free and marked static, so the caller's StaticBatchingUtility.Combine
    /// folds it in with the rest of the surroundings.
    /// </summary>
    public static class Landform
    {
        /// <summary>
        /// A faceted cone: base ring on y = 0, apex at y = <paramref name="height"/>, split into a rock
        /// submesh below <paramref name="snowAltitude"/> and a snow submesh above it. Pass a snow
        /// altitude at or above the height for a bare hill (one submesh, no snow).
        ///
        /// <paramref name="wobble"/> irregularises the base radius per facet so a ridge of these does
        /// not read as a row of identical traffic cones. <paramref name="lean"/> shifts the apex off
        /// centre, which is what stops every peak being symmetrical.
        /// </summary>
        public static Mesh Cone(int sides, float radius, float height, float snowAltitude,
                                float wobble, Vector2 lean, uint seed)
        {
            sides = Mathf.Clamp(sides, 3, 24);
            // Local deterministic LCG: callers build many peaks and must get the same range every run.
            uint s = seed == 0u ? 1u : seed;
            System.Func<float> rnd = () => { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216f; };

            Vector3 apex = new Vector3(lean.x * radius, height, lean.y * radius);

            // Base ring, with a per-facet radius wobble. The ring closes, so index i+1 wraps.
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                float r = radius * (1f - wobble * rnd());
                ring[i] = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            }

            // Snow cut. t is where the snow altitude falls up each edge, 0 at the base and 1 at the
            // apex; clamped, so a peak lower than the snowline simply never enters the snow branch.
            float t = height <= 1e-4f ? 1f : Mathf.Clamp01(snowAltitude / height);
            bool hasSnow = t < 0.999f;

            var verts = new System.Collections.Generic.List<Vector3>(sides * 9);
            var rock = new System.Collections.Generic.List<int>(sides * 6);
            var snow = new System.Collections.Generic.List<int>(sides * 3);

            for (int i = 0; i < sides; i++)
            {
                Vector3 b0 = ring[i];
                Vector3 b1 = ring[(i + 1) % sides];
                if (!hasSnow)
                {
                    // Bare peak: one triangle per facet, base edge up to the apex.
                    int v = verts.Count;
                    verts.Add(b0); verts.Add(b1); verts.Add(apex);
                    rock.Add(v); rock.Add(v + 1); rock.Add(v + 2);
                    continue;
                }
                // Split facet: a quad from the base up to the snowline, then a triangle to the apex.
                Vector3 m0 = Vector3.Lerp(b0, apex, t);
                Vector3 m1 = Vector3.Lerp(b1, apex, t);
                int q = verts.Count;
                verts.Add(b0); verts.Add(b1); verts.Add(m1); verts.Add(m0);
                rock.Add(q); rock.Add(q + 1); rock.Add(q + 2);
                rock.Add(q); rock.Add(q + 2); rock.Add(q + 3);
                int u = verts.Count;
                verts.Add(m0); verts.Add(m1); verts.Add(apex);
                snow.Add(u); snow.Add(u + 1); snow.Add(u + 2);
            }

            var mesh = new Mesh { name = hasSnow ? "Peak" : "Hill" };
            mesh.SetVertices(verts);
            mesh.subMeshCount = hasSnow ? 2 : 1;
            mesh.SetTriangles(rock, 0);
            if (hasSnow) mesh.SetTriangles(snow, 1);
            mesh.RecalculateNormals();     // flat, because no vertex is shared between facets
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Place one generated landform. Collider-free and static, so it joins the caller's batch.
        /// Pass one material for a bare hill, two (rock, snow) for a snow-capped peak.
        /// </summary>
        public static GameObject Place(Transform parent, string name, Vector3 groundPos, Mesh mesh,
                                       Material lower, Material upper, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = groundPos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mesh.subMeshCount > 1 && upper != null
                               ? new[] { lower, upper }
                               : new[] { lower };
            // Distant scenery casting or receiving shadows buys nothing and costs shadow-map area at
            // exactly the range where the cascade is coarsest.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.isStatic = true;
            return go;
        }
    }
}
