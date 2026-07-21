using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Runtime construction helpers. Every visible object in the prototype is a
    /// Unity primitive with a Standard-shader material tinted greybox colours.
    /// Nothing here touches the AssetDatabase, so it all works at runtime.
    /// </summary>
    public static class Make
    {
        static Shader s_Standard;
        static Shader Standard
        {
            get
            {
                if (s_Standard == null) s_Standard = Shader.Find("Standard");
                return s_Standard;
            }
        }

        public static Material Mat(Color c, float smoothness = 0.1f, float metallic = 0f)
        {
            var m = new Material(Standard);
            m.color = c;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>Standard material with a main texture (e.g. the painted jersey). Tint
        /// is white so the texture shows as painted.</summary>
        public static Material MatTex(Texture2D tex, float smoothness = 0.1f)
        {
            var m = new Material(Standard);
            m.color = Color.white;
            m.mainTexture = tex;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", 0f);
            return m;
        }

        static Shader s_Unlit;
        /// <summary>Flat unlit colour: always shows the same regardless of light angle.
        /// Used for net strings so they never shade to black and read as see-through.</summary>
        public static Material Unlit(Color c)
        {
            if (s_Unlit == null) s_Unlit = Shader.Find("Unlit/Color");
            var m = s_Unlit != null ? new Material(s_Unlit) : new Material(Standard);
            m.color = c;                 // Unlit/Color uses _Color
            return m;
        }

        /// <summary>
        /// A cylinder visual with a CapsuleCollider (rounded, gives clean bounces).
        /// axis: 0 = X, 1 = Y, 2 = Z. length spans that axis; radius is the tube radius.
        /// </summary>
        public static GameObject Cylinder(string name, float radius, float length, Vector3 pos,
                                          int axis, Material mat, Transform parent = null, PhysicsMaterial phys = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            // Unity cylinder is 2 units tall on Y, 0.5 radius, at scale 1.
            var s = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            Quaternion rot = Quaternion.identity;
            if (axis == 0) rot = Quaternion.Euler(0f, 0f, 90f);   // lay along X
            else if (axis == 2) rot = Quaternion.Euler(90f, 0f, 0f); // lay along Z
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = s;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;

            // Replace whatever collider the primitive shipped with a CapsuleCollider.
            var old = go.GetComponent<Collider>();
            if (old != null) Object.Destroy(old);
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;          // local Y (the cylinder's long axis before rotation)
            // Collider dims are in LOCAL space and multiplied by localScale, so use the
            // unit-primitive values (radius 0.5, height 2). With scale (r*2, len*0.5, r*2)
            // the world size becomes radius=r, height=len - matching the visual.
            cap.radius = 0.5f;
            cap.height = 2f;
            if (phys != null) cap.material = phys;
            return go;
        }

        /// <summary>Solid unlit-ish emissive material so gizmo-like objects pop (reticle, trails).</summary>
        public static Material Glow(Color c)
        {
            var m = new Material(Standard);
            m.color = c;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * 1.6f);
            return m;
        }

        public static GameObject Box(string name, Vector3 size, Vector3 pos, Material mat,
                                     Transform parent = null, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// A torso box whose UVs map the jersey ATLAS correctly onto the body instead of
        /// the stock cube's identical-0..1-on-every-face layout (which duplicated the design
        /// on all six faces and flipped the back). The atlas (see JerseyDesigns) stacks two
        /// 256x256 regions: BACK (bottom) and FRONT (above), plus a small plain band on top.
        ///   +Z face (chest, character faces +Z)  -> samples the FRONT region, upright.
        ///   -Z face (back)                        -> samples the BACK region, upright + not mirrored.
        ///   all other faces (sides/top/bottom)    -> collapse to one texel in the plain band
        ///                                            so they show solid jersey base colour.
        /// Uses a fresh mesh instance (mf.mesh), which Unity frees with the GameObject.
        /// </summary>
        public static GameObject JerseyBox(string name, Vector3 size, Vector3 pos, Material mat,
                                           Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());   // visual only; the bone holds the collider
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;

            var mf = go.GetComponent<MeshFilter>();
            var mesh = mf.mesh;                 // instantiates a per-object mesh copy (freed with the GO)
            Vector3[] verts = mesh.vertices;    // unit cube, coords in [-0.5, 0.5]
            Vector3[] norms = mesh.normals;
            var uv = new Vector2[verts.Length];

            // Atlas V ranges (normalized) for each stacked region of the 256x520 atlas.
            float atlasH = JerseyDesigns.AtlasH;
            float backV0 = JerseyDesigns.BackY0 / atlasH;                       // 0
            float frontV0 = JerseyDesigns.FrontY0 / atlasH;                     // 256/520
            float regV = JerseyDesigns.RegionH / atlasH;                        // 256/520
            // A single texel dead-centre of the plain band -> solid base colour on side faces.
            var plainUV = new Vector2(0.5f, (JerseyDesigns.PlainY0 + 4f) / atlasH);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = norms[i];
                Vector3 v = verts[i];   // local, [-0.5, 0.5]
                if (n.z > 0.5f)
                {
                    // FRONT (+Z, chest). Looking at the chest from outside (down -Z), local +X
                    // is the character's LEFT and appears on the viewer's right; flip u so the
                    // texture's left maps to the chest's left. v up = texture up (upright).
                    float u = 0.5f - v.x;
                    float t = 0.5f + v.y;
                    uv[i] = new Vector2(u, frontV0 + t * regV);
                }
                else if (n.z < -0.5f)
                {
                    // BACK (-Z). Looking at the back from outside (down +Z), local +X is on the
                    // viewer's right; do NOT flip u so name/number read left-to-right. v up =
                    // texture up, so the baked (upright) identity reads upright (fixes the flip).
                    float u = 0.5f + v.x;
                    float t = 0.5f + v.y;
                    uv[i] = new Vector2(u, backV0 + t * regV);
                }
                else
                {
                    uv[i] = plainUV;   // sides, top, bottom: plain base colour, no art
                }
            }

            mesh.uv = uv;
            return go;
        }

        public static GameObject Sphere(string name, float diameter, Vector3 pos, Material mat, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * diameter;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        /// <summary>
        /// A capsule-shaped visual/collider whose local +Y spans the given length.
        /// Unity capsules are 2 units tall at scale 1 with radius 0.5; we scale so
        /// the capsule is 'length' tall and 'radius*2' wide.
        /// </summary>
        public static GameObject Capsule(string name, float radius, float length, Vector3 pos,
                                         Material mat, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        public static GameObject Empty(string name, Vector3 pos, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            return go;
        }

        public static PhysicsMaterial PhysMat(string name, float bounce, float dynFric, float statFric,
                                             PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum,
                                             PhysicsMaterialCombine frictionCombine = PhysicsMaterialCombine.Average)
        {
            var pm = new PhysicsMaterial(name)
            {
                bounciness = bounce,
                dynamicFriction = dynFric,
                staticFriction = statFric,
                bounceCombine = bounceCombine,
                frictionCombine = frictionCombine
            };
            return pm;
        }
    }
}
