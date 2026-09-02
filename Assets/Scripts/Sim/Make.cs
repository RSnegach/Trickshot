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
                if (s_Standard != null) return s_Standard;
                // Standard can be STRIPPED from a player build: nothing references it at build time
                // (every material here is made at runtime via Shader.Find), so Unity drops it and
                // Shader.Find returns null in the build -> new Material(null) throws and blanks the
                // whole game. Fall back to shaders that ship by default (Legacy Diffuse + Sprites/
                // Default are in the Always Included Shaders list) so it always renders something.
                s_Standard = Shader.Find("Standard")
                             ?? Shader.Find("Legacy Shaders/Diffuse")
                             ?? Shader.Find("Sprites/Default");
                if (s_Standard == null)
                    Debug.LogError("Make: no usable shader found (Standard stripped from build?). Add "
                                   + "'Standard' to Project Settings > Graphics > Always Included Shaders.");
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

        static Shader s_Hair;
        static Texture2D s_HairAtlas;
        /// <summary>Hair-CARD material for HairSim's textured quad ribbons: an alpha-cutout
        /// (Kajiya-Kay lit) shader sampling a shared grayscale hair atlas, tinted to the player's
        /// hair colour. The wispy strand edges come from the atlas opacity mask, not geometry.
        /// Loaded from Resources (no scene wiring); falls back to flat Unlit if the card shader is
        /// somehow absent from the build.</summary>
        public static Material Hair(Color c)
        {
            if (s_Hair == null) s_Hair = Resources.Load<Shader>("Shaders/HairCard");
            if (s_HairAtlas == null) s_HairAtlas = Resources.Load<Texture2D>("Hair/HairAtlas");
            if (s_Hair == null) return Unlit(c);        // graceful fallback: still visible
            var m = new Material(s_Hair);
            m.SetColor("_Color", c);
            if (s_HairAtlas != null) { s_HairAtlas.wrapMode = TextureWrapMode.Clamp; m.SetTexture("_MainTex", s_HairAtlas); }
            return m;
        }

        /// <summary>Hair material for a SOLID surface (the crown patch / skullcap). Same HairCard
        /// shader as the strands - so the cap shares the hair's Kajiya-Kay tint + anisotropic sheen
        /// instead of reading as flat plastic - but with the alpha cutoff at 0 so it never clips
        /// (a solid cap must stay opaque to hide the scalp; the strand atlas only drives opacity,
        /// which we don't want here). Tint the cap's vertex tangents along the hair flow for a
        /// hair-like highlight. Falls back to a flat lit material if the shader is absent.</summary>
        public static Material HairCap(Color c)
        {
            if (s_Hair == null) s_Hair = Resources.Load<Shader>("Shaders/HairCard");
            if (s_Hair == null) return Mat(c, 0.15f);   // graceful fallback: flat lit cap
            var m = new Material(s_Hair);
            m.SetColor("_Color", c);
            m.SetFloat("_Cutoff", 0f);                  // never clip -> fully opaque dome
            // A shell fills mesh.tangents with its geometric normal; half Lambert on that gives the
            // dome FORM shading (a cap lit by Kajiya tangents alone reads as a flat sheet).
            m.SetFloat("_NormalWeight", 0.55f);
            return m;
        }

        static Texture2D s_TuftAtlas;
        /// <summary>Hair-card material for SHORT tufts: the HairCard shader on the TUFT atlas
        /// (Resources/Hair/TuftAtlas.png - four tapered clumps that converge to a point, roots at
        /// the bottom) with a slightly lower cutoff so the wispy tips survive. The Long atlas stays
        /// on the long styles untouched.</summary>
        public static Material HairTuftCards(Color c)
        {
            if (s_Hair == null) s_Hair = Resources.Load<Shader>("Shaders/HairCard");
            if (s_TuftAtlas == null) s_TuftAtlas = Resources.Load<Texture2D>("Hair/TuftAtlas");
            if (s_Hair == null) return Unlit(c);
            var m = new Material(s_Hair);
            m.SetColor("_Color", c);
            m.SetFloat("_Cutoff", 0.35f);
            if (s_TuftAtlas != null) { s_TuftAtlas.wrapMode = TextureWrapMode.Clamp; m.SetTexture("_MainTex", s_TuftAtlas); }
            return m;
        }

        static Shader s_Decal;
        static Texture2D s_Stipple;
        /// <summary>Stipple mask for drawn-on scalp/stubble decals (Resources/Hair/Stipple.png,
        /// tileable grayscale). A static singleton, never destroyed.</summary>
        public static Texture2D Stipple
        {
            get { if (s_Stipple == null) s_Stipple = Resources.Load<Texture2D>("Hair/Stipple"); return s_Stipple; }
        }
        /// <summary>Alpha-blended decal material (Trickshot/HeadDecal): tint with alpha = opacity,
        /// masked by a grayscale texture tiled `tile` times. Falls back to a flat lit material if the
        /// shader is somehow absent.</summary>
        public static Material Decal(Color tint, Texture2D mask, float tileU = 1f, float tileV = 1f)
        {
            if (s_Decal == null) s_Decal = Resources.Load<Shader>("Shaders/HeadDecal");
            if (s_Decal == null) return Mat(new Color(tint.r, tint.g, tint.b, 1f), 0.1f);
            var m = new Material(s_Decal);
            m.SetColor("_Color", tint);
            if (mask != null) { mask.wrapMode = TextureWrapMode.Repeat; m.SetTexture("_MainTex", mask); }
            m.SetTextureScale("_MainTex", new Vector2(tileU, tileV));
            return m;
        }

        /// <summary>Hair material for a facial-hair BIB (beards). Same HairCard shader + strand
        /// atlas as scalp hair, so a UV'd beard mesh shows real strand texture + sheen, but with a
        /// LOW cutoff (not 0 like the cap): the strand alpha reads at the wispy bib edges without
        /// punching big holes through the middle. Tinted to the facial colour. Falls back to a flat
        /// lit material if the shader/atlas is absent.</summary>
        public static Material HairTuft(Color c)
        {
            if (s_Hair == null) s_Hair = Resources.Load<Shader>("Shaders/HairCard");
            if (s_HairAtlas == null) s_HairAtlas = Resources.Load<Texture2D>("Hair/HairAtlas");
            if (s_Hair == null) return Mat(c, 0.2f);    // graceful fallback: flat lit bib
            var m = new Material(s_Hair);
            m.SetColor("_Color", c);
            m.SetFloat("_Cutoff", 0.22f);               // strand alpha reads at the edges, solid core
            if (s_HairAtlas != null) { s_HairAtlas.wrapMode = TextureWrapMode.Clamp; m.SetTexture("_MainTex", s_HairAtlas); }
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
        /// Which faces of a jersey box carry the painted art. The atlas is authored for an UPRIGHT
        /// torso, so a barrel that rests pitched 90 deg needs a different set of faces entirely.
        /// </summary>
        public enum JerseyFaces
        {
            /// <summary>Biped chest/back: art on local +Z and -Z. The upright torso case.</summary>
            Chest,
            /// <summary>
            /// Quadruped barrel: art on local +/-X (the FLANKS) and local -Z (the SPINE). The barrel
            /// rests at RestEuler (90,0,0), which sends local +Y to world +Z and local +Z to world
            /// -Y. So on a quadruped local +Z points at the BELLY and local -Z at the SPINE, and the
            /// Chest mapping paints the whole design where nobody can see it while the flanks - the
            /// only view of a horse that matters - collapse to the plain band.
            /// </summary>
            Flank,
        }

        /// <summary>
        /// A torso box whose UVs map the jersey ATLAS correctly onto the body instead of
        /// the stock cube's identical-0..1-on-every-face layout (which duplicated the design
        /// on all six faces and flipped the back). The atlas (see JerseyDesigns) stacks two
        /// 256x256 regions: BACK (bottom) and FRONT (above), plus a small plain band on top.
        ///
        /// JerseyFaces.Chest (biped):
        ///   +Z face (chest, character faces +Z)  -> samples the FRONT region, upright.
        ///   -Z face (back)                        -> samples the BACK region, upright + not mirrored.
        ///   all other faces (sides/top/bottom)    -> collapse to one texel in the plain band
        ///                                            so they show solid jersey base colour.
        ///
        /// JerseyFaces.Flank (quadruped barrel, rest-pitched 90 deg):
        ///   +/-X faces (flanks)                   -> sample the BACK region, upright in the WORLD.
        ///   -Z face (spine, world up)             -> samples the FRONT region.
        ///   +Z (belly) and +/-Y (rump, shoulders) -> plain band.
        /// Both flanks take BACK on purpose. JerseyDesigns runs the same paint delegate over both
        /// regions, so they carry an identical design and only BACK additionally carries the baked
        /// name and number - which is exactly how a numbered saddle cloth works.
        ///
        /// The flank face is 0.84 long by 0.34 deep, so a square atlas region stretches about 2.5x
        /// along the body. That is deliberate: the alternative is cropping to a centred square, which
        /// would leave most of the animal plain and hide the design the player just picked. A rug
        /// covering the whole barrel is the real-world shape, stripes and bands survive it fine, and
        /// only the baked number reads noticeably wide.
        ///
        /// Uses a fresh mesh instance (mf.mesh), which Unity frees with the GameObject.
        /// </summary>
        public static GameObject JerseyBox(string name, Vector3 size, Vector3 pos, Material mat,
                                           Transform parent = null, JerseyFaces faces = JerseyFaces.Chest)
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

            // Atlas V ranges (normalized) for each stacked region of the atlas (AtlasW x AtlasH texels).
            float atlasH = JerseyDesigns.AtlasH;
            float backV0 = JerseyDesigns.BackY0 / atlasH;                       // 0
            float frontV0 = JerseyDesigns.FrontY0 / atlasH;                     // 512/1032
            float regV = JerseyDesigns.AtlasRegionH / atlasH;                   // 512/1032
            // A single texel dead-centre of the plain band -> solid base colour on side faces.
            var plainUV = new Vector2(0.5f, (JerseyDesigns.PlainY0 + 4f) / atlasH);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = norms[i];
                Vector3 v = verts[i];   // local, [-0.5, 0.5]
                if (faces == JerseyFaces.Flank)
                {
                    // The orientation rule, validated against the two working biped faces below:
                    // for a face that is upright and NOT mirrored when viewed from outside,
                    // Cross(u_direction, t_direction) == -normal. On the rest-pitched barrel world
                    // UP is local -Z and world FORWARD is local +Y, which is why t runs off -v.z.
                    if (n.x > 0.5f)
                        // RIGHT flank. From world +X the animal's nose sits on the viewer's right,
                        // and that is local +Y, so u tracks +Y and the design is not mirrored.
                        uv[i] = new Vector2(0.5f + v.y, backV0 + (0.5f - v.z) * regV);
                    else if (n.x < -0.5f)
                        // LEFT flank: the nose is now on the viewer's LEFT, so u tracks -Y instead.
                        uv[i] = new Vector2(0.5f - v.y, backV0 + (0.5f - v.z) * regV);
                    else if (n.z < -0.5f)
                        // SPINE (local -Z = world up). Design "up" points at the head (+Y) so a
                        // chase camera behind the animal reads it upright.
                        uv[i] = new Vector2(0.5f + v.x, frontV0 + (0.5f + v.y) * regV);
                    else
                        // Belly (+Z), rump (-Y), shoulders (+Y): plain base colour, no art.
                        uv[i] = plainUV;
                    continue;
                }
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
