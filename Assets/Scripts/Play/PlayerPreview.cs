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
        float _yaw;   // the MODEL's turn angle (deg) on top of its rest facing. The CAMERA is
                      // fixed; dragging turns the model in place so the view can never drift or
                      // misalign the way an orbiting camera did over many/sharp turns.

        // Model rest facing: built facing -Z (yaw 180) so it looks into the fixed front camera.
        // Euler(0,180,0) == LookRotation(Vector3.back), so _yaw 0 shows the chest.
        const float FrontYaw = 180f;
        Quaternion ModelFacing() => Quaternion.Euler(0f, FrontYaw + _yaw, 0f);

        // Viewport rect in pixels (top-left origin, like IMGUI); converted to the camera's
        // bottom-left normalized rect each frame so it tracks the panel.
        public Rect ViewportPx;

        // ---- shared preview-column geometry ----
        //
        // SpeciesSelectUI and CustomizeUI both lay out a preview column on the left and a control
        // panel on the right, and the two MUST agree or the model jumps across the screen on Next.
        // They used to agree by both hardcoding 300 / 16 / 560 / 600, which is exactly the kind of
        // agreement that rots. It lives here now, next to the camera that has to frame whatever
        // width comes out.
        public const float ColumnGap = 16f;    // between the preview column and the panel
        public const float PanelW    = 560f;   // the control panel
        public const float PanelH    = 600f;   // both are this tall

        // The column was a flat 300 px, which is an 0.5 aspect against PanelH. At fov 42 that gives
        // a horizontal half-extent of only 0.192*d, so a horse or an elephant, which is long rather
        // than tall, could not fit side-on however far back the camera went without shrinking to
        // nothing. Widen it with the display instead.
        //
        // The bound is CustomizeUI.SkillPresetButtons, which hangs a QUICK BUILDS column in the
        // MARGIN left of the preview while the whole block stays centred. So every pixel the preview
        // gains costs two: one from each margin. SideMargin is what stays reserved (24 edge +
        // 130 buttons + 12 gap), which does shrink those buttons from 200 px on a 1080p display, and
        // is the price of the wider preview. Resolved widths: 300 at 1280x1024, 372 at 720p, 443 at
        // 1080p and 1440p, 560 at 4K. Never below the old 300, so nothing regresses on a small
        // display; capped at 560 because past that the animal is framed by its vertical fit anyway
        // and the extra width is empty turf.
        const float MinColumnW = 300f, MaxColumnW = 560f, SideMargin = 166f;

        public static float ColumnWidth => Mathf.Clamp(
            MenuScale.Width - 2f * SideMargin - ColumnGap - PanelW, MinColumnW, MaxColumnW);

        // When true the model spins on its own; when false the caller drives the yaw. The
        // customize screen keeps this off in every stage and lets the player click-drag to
        // turn the model manually.
        public bool AutoRotate = true;
        // Manual drag from the UI. Turns the MODEL (the camera is fixed). Negated so a drag
        // spins the model the same on-screen direction it used to appear to spin back when the
        // camera orbited by +deg, keeping the existing drag feel.
        public void AddYaw(float deg) => _yaw -= deg;

        // Snap the model to show the chest (front) or the back, so the preview shows the side
        // currently being drawn. The camera is fixed on the front; turning the model a half-turn
        // shows the back. Callers turn AutoRotate off first.
        public void FaceSide(bool back) => _yaw = back ? 180f : 0f;

        public void Setup()
        {
            // Dedicated camera: renders only the staging area, transparent-ish backdrop.
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            // Lifted off near-black. The old 0.12/0.13/0.16 made the whole column read as a hole in
            // the screen and dragged the model's apparent brightness down with it, because the eye
            // judges the body against whatever it is sitting on.
            _cam.backgroundColor = new Color(0.20f, 0.23f, 0.30f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 30f;
            _cam.depth = 5;                 // draw over the main camera
            _cam.fieldOfView = 42f;

            // KEY + FILL. One directional at 1.1 was the whole of it, which left every surface facing
            // away from it on ambient alone - and the ambient here is whatever the menu happened to set
            // (SkyDome.ApplyMenu), so the model came out dim and half of it came out nearly black.
            // Raised, and swung round more toward the camera so it lights the front of the body rather
            // than raking it.
            var lgo = new GameObject("PreviewLight");
            lgo.transform.SetParent(transform, false);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.intensity = 1.75f;
            _light.transform.rotation = Quaternion.Euler(28f, 148f, 0f);
            _light.cullingMask = ~0;

            // Fill from the opposite side, sky-blue because that is the colour of bounce light. At 0.35
            // against the key's 1.75 this is a 20% ratio - a fill, not a second key, so it shapes the
            // shadow side that PreviewAmbient has already lifted. (The pair that over-lit the main menu
            // were both 1.15 and 175 degrees apart, cancelling instead of shaping; see MenuBackground.)
            // No shadows: it only has to raise the floor of the lighting.
            // These are full-mask, like the key above, so they also reach the scene behind the panel -
            // the preview camera draws over that area anyway, so it only shows at the screen edges.
            var fgo = new GameObject("PreviewFill");
            fgo.transform.SetParent(transform, false);
            var fill = fgo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.80f, 0.87f, 1f);
            fill.intensity = 0.35f;
            fill.transform.rotation = Quaternion.Euler(18f, -35f, 0f);
            fill.shadows = LightShadows.None;
            fill.cullingMask = ~0;

            RaiseAmbient();

            // A small ground pad so the model isn't floating in void.
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "PreviewFloor";
            _floor.transform.SetParent(transform, false);
            _floor.transform.position = Stage + new Vector3(0f, -0.5f, 0f);
            _floor.transform.localScale = new Vector3(4f, 1f, 4f);
            // Brighter than the old 0.18/0.30/0.18: the pad is most of what is behind the legs, so a
            // dark one made the lower half of every model look unlit.
            _floor.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.28f, 0.44f, 0.28f), 0.05f);

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

            var facing = ModelFacing(); // face the fixed camera, offset by the current drag turn
            // Pass the player's appearance so the preview shows skin tone + head cosmetics; Build
            // tints `limbs` to the skin colour (overriding the placeholder above) and attaches the
            // hair/facial/accessory visuals.
            _ragdoll.BuildScaled(Stage, facing, torso, limbs,
                                 PlayerProfile.HeightScale, PlayerProfile.GirthScale, PlayerProfile.MassMul,
                                 withGloves: false, appearance: PlayerProfile.Appearance);
            // A calm mannequin: make it a KINEMATIC display body and pose it by transform every
            // frame (DisplaySnap), instead of a live dynamic ragdoll. This kills two preview bugs:
            //   - the respawn JERK/DRIFT (a dynamic body settles under gravity/joints and, with a
            //     fixed camera, slides out of frame - UprightLock only froze rotation, not position);
            //   - torso/pelvis TWISTING APART on a fast drag (a joint-driven torso lags the pelvis
            //     yaw). Kinematic bones don't fall and have no joint spring, so the body stays rigid
            //     and pinned exactly at Stage. The loose jointed body is still used in gameplay.
            _ragdoll.BecomeDisplayBody();
            _ragdoll.DisplaySnap(Stage, facing);
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

            // The MODEL turns; the CAMERA is FIXED. _yaw is the model's turn angle: on the
            // customize screen AutoRotate is off and dragging drives _yaw, while auto-rotate
            // (menu background) spins the model on its own. Turning the model instead of orbiting
            // the camera means the view can never drift or misalign over many/sharp drags.
            if (AutoRotate) _yaw += Time.unscaledDeltaTime * 35f;

            // Pose the KINEMATIC display body rigidly at the fixed Stage with the current facing.
            // Snapping every bone by transform (no physics, no joint spring) keeps it pinned in
            // frame and keeps the torso/pelvis locked together no matter how fast the drag turns.
            if (_ragdoll != null) _ragdoll.DisplaySnap(Stage, ModelFacing());

            // Fixed front camera: parked on -Z looking at the model's mid-height, never orbits.
            // The framing is per species (SpeciesDef.PreviewDist/PreviewHeight/PreviewZ) because a
            // quadruped is long rather than tall: it needs the camera further back, the pivot lower,
            // and the pivot slid forward to the middle of its length so a side-on drag keeps the
            // muzzle in the viewport. Human is 3.2 / 1.0 / 0, the original framing.
            //
            // EVERY term scales with the build height, DISTANCE INCLUDED, and that last part is the
            // fix for a horse whose muzzle used to be cropped off even at default settings. Body
            // extents scale by the build's height scale while the camera distance did not, so no
            // single authored PreviewDist could hold across the Size slider: the animal outgrew the
            // frustum as the slider went up. Scaling the distance by the same factor makes the
            // required framing scale-INDEPENDENT, because ps then cancels on both sides.
            var sp = Species.Current;
            float ps = PlayerProfile.HeightScale;

            // Pull back far enough for the WIDEST visible axis to fit horizontally as well. The
            // preview column's width now varies with the display (see ColumnWidth), so a fixed
            // distance cannot be right at every aspect: fieldOfView is the VERTICAL fov, so the
            // horizontal half-extent is tan(fov/2) * aspect, and a portrait column makes that term
            // small. Author PreviewDist for the VERTICAL fit and let this raise it when the column
            // is narrow. Human's authored 3.2 dwarfs its wide term, so the human preview keeps the
            // framing it always had.
            float tanV   = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = ViewportPx.height > 1f ? ViewportPx.width / ViewportPx.height : 1f;
            float dWide  = sp.PreviewHalfW / Mathf.Max(0.05f, tanV * aspect);
            float dist   = Mathf.Max(sp.PreviewDist, dWide) * ps;

            Vector3 pivot = Stage + new Vector3(0f, sp.PreviewHeight * ps, sp.PreviewZ * ps);
            _cam.transform.position = pivot + new Vector3(0f, 0.2f, -dist);
            _cam.transform.LookAt(pivot);
        }

        /// <summary>
        /// Stop rendering and posing THIS INSTANT, without waiting for the deferred Destroy in
        /// Teardown. Needed because a screen handing off to another screen does both inside one
        /// frame, while Destroy (and so OnDestroy -> Teardown) does not run until the end of it.
        /// Species -> Customize is the case that forced this: both screens own a preview, so for the
        /// tail of that frame two cameras at the same depth render two models in the same staging
        /// spot. Call this before invoking the hand-off callback; Teardown still does the cleanup.
        /// </summary>
        public void Hide()
        {
            if (_cam != null) _cam.enabled = false;
            gameObject.SetActive(false);
        }

        public void Teardown()
        {
            if (this != null) Destroy(gameObject);
        }

        // ---- scene ambient, raised while a preview is up ----
        // REFCOUNTED, and it has to be. Species -> Customize keeps TWO previews alive for the tail of
        // one frame (see Hide), so a plain save/restore pair would have the second preview save the
        // ALREADY-RAISED value and then restore it on the way out - leaking a bright ambient into the
        // match. The count means the first one in saves the real value and the last one out puts it
        // back, whatever order they are destroyed in.
        static int _ambRefs;
        static float _ambSaved;

        void RaiseAmbient()
        {
            if (_ambRefs++ == 0) _ambSaved = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = SimConfig.PreviewAmbient;
            DynamicGI.UpdateEnvironment();
        }

        void OnDestroy()
        {
            if (_ambRefs > 0 && --_ambRefs == 0)
            {
                RenderSettings.ambientIntensity = _ambSaved;
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
