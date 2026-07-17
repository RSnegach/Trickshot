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
                                             PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum)
        {
            var pm = new PhysicsMaterial(name)
            {
                bounciness = bounce,
                dynamicFriction = dynFric,
                staticFriction = statFric,
                bounceCombine = bounceCombine,
                frictionCombine = PhysicsMaterialCombine.Average
            };
            return pm;
        }
    }
}
