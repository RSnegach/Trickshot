using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A live 3D preview of the customized player, shown on the Customize screen. It
    /// spawns a REAL ActiveRagdoll (same builder the match uses) far from the arena, held
    /// upright and slowly turning, and renders it with a dedicated camera into a viewport
    /// rect the UI supplies. Rebuild() re-creates the model from the current PlayerProfile
    /// so height, weight and the jersey update exactly as they will look in game.
    /// </summary>
    public class PlayerPreview : MonoBehaviour
    {
        // Staging area well away from the real pitch so the preview never collides with it.
        static readonly Vector3 Stage = new Vector3(1000f, 0f, 1000f);

        Camera _cam;
        Light _light;
        GameObject _floor;
        ActiveRagdoll _ragdoll;
        GameObject _modelRoot;
        float _yaw;

        // Viewport rect in pixels (top-left origin, like IMGUI); converted to the camera's
        // bottom-left normalized rect each frame so it tracks the panel.
        public Rect ViewportPx;

        // When true the model spins on its own; when false the caller drives the yaw (the
        // jersey stage lets the player click-drag to spin it manually).
        public bool AutoRotate = true;
        public void AddYaw(float deg) => _yaw += deg;   // manual drag from the UI

        // Snap the orbit to face the chest (front) or the back of the model, so the preview
        // shows the side currently being drawn. The model faces -Z, so yaw 0 looks at the
        // chest; a half-turn shows the back. Callers turn AutoRotate off first.
        public void FaceSide(bool back) => _yaw = back ? 180f : 0f;

        public void Setup()
        {
            // Dedicated camera: renders only the staging area, transparent-ish backdrop.
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 30f;
            _cam.depth = 5;                 // draw over the main camera
            _cam.fieldOfView = 42f;

            // A soft key light so the model reads.
            var lgo = new GameObject("PreviewLight");
            lgo.transform.SetParent(transform, false);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.1f;
            _light.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
            _light.cullingMask = ~0;

            // A small ground pad so the model isn't floating in void.
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "PreviewFloor";
            _floor.transform.SetParent(transform, false);
            _floor.transform.position = Stage + new Vector3(0f, -0.5f, 0f);
            _floor.transform.localScale = new Vector3(4f, 1f, 4f);
            _floor.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.18f, 0.30f, 0.18f), 0.05f);

            Rebuild();
        }

        // (Re)create the model from the current profile. Called on Setup and whenever the
        // player changes height/weight/jersey so the preview matches the in-game build.
        Material _torsoMat, _limbMat;
        Texture2D _liveJersey;   // if set, the torso uses this live canvas (updates as painted)

        // Point the torso at a live canvas texture. Because it is the SAME Texture2D the
        // paint code SetPixels32/Apply-s, strokes appear on the 3D model in real time with
        // no rebuild. Applies immediately to the current model too.
        public void SetLiveJersey(Texture2D tex)
        {
            _liveJersey = tex;
            if (_torsoMat != null && tex != null)
            {
                _torsoMat.mainTexture = tex;
                _torsoMat.color = Color.white;   // show the texture true, not tinted by the base
            }
        }

        public void Rebuild()
        {
            if (_modelRoot != null) Destroy(_modelRoot);
            // Destroying the GameObjects does NOT free the Materials, so free the previous
            // pair explicitly to avoid leaking one set per rebuild.
            if (_torsoMat != null) Destroy(_torsoMat);
            if (_limbMat != null) Destroy(_limbMat);

            _modelRoot = new GameObject("PreviewModel");
            _modelRoot.transform.SetParent(transform, false);
            _ragdoll = _modelRoot.AddComponent<ActiveRagdoll>();

            // Prefer the live canvas (jersey stage) so painting shows immediately; else the
            // committed jersey; else plain base colour.
            Texture2D jt = _liveJersey != null ? _liveJersey : PlayerProfile.JerseyTex;
            Material torso = jt != null ? Make.MatTex(jt) : Make.Mat(PlayerProfile.JerseyBase);
            Material limbs = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            _torsoMat = torso; _limbMat = limbs;

            var facing = Quaternion.LookRotation(Vector3.back, Vector3.up); // face the camera (-Z)
            // Pass the player's appearance so the preview shows skin tone + head cosmetics; Build
            // tints `limbs` to the skin colour (overriding the placeholder above) and attaches the
            // hair/facial/accessory visuals.
            _ragdoll.BuildScaled(Stage, facing, torso, limbs,
                                 PlayerProfile.HeightScale, PlayerProfile.GirthScale, PlayerProfile.MassMul,
                                 withGloves: false, appearance: PlayerProfile.Appearance);
            // Hold it upright and still (a calm mannequin, not a live ragdoll).
            _ragdoll.UprightLock = true;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
        }

        void LateUpdate()
        {
            if (_cam == null) return;

            // Track the UI viewport rect (convert top-left px to camera bottom-left norm).
            if (ViewportPx.width > 1f && Screen.height > 0)
            {
                float nx = ViewportPx.x / Screen.width;
                float ny = 1f - (ViewportPx.y + ViewportPx.height) / Screen.height;
                float nw = ViewportPx.width / Screen.width;
                float nh = ViewportPx.height / Screen.height;
                _cam.rect = new Rect(nx, ny, nw, nh);
            }

            // Frame the model: camera in front (-Z of the stage since the model faces -Z),
            // orbiting so the player sees front + back (name/number + jersey art). Auto in
            // the body/name stages; the jersey stage turns it off and drives yaw by drag.
            if (AutoRotate) _yaw += Time.unscaledDeltaTime * 35f;
            Vector3 pivot = Stage + new Vector3(0f, 1.0f * PlayerProfile.HeightScale, 0f);
            Quaternion rot = Quaternion.Euler(6f, _yaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -3.2f);
            _cam.transform.position = pivot + offset + Vector3.up * 0.2f;
            _cam.transform.LookAt(pivot);
        }

        public void Teardown()
        {
            if (this != null) Destroy(gameObject);
        }
    }
}
