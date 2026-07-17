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
