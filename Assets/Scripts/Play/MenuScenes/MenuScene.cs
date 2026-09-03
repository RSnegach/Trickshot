using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One mode's live vignette, built on its own sub-stage and rendered into a menu panel.
    ///
    /// Lifecycle, driven by MenuSceneStage: Build once -> Freeze (kinematic, the frozen first
    /// frame every unhovered panel shows) -> on hover Thaw + Tick until Done -> on mouse-off
    /// Reset + Freeze, which puts the scene back exactly as it was built so the next hover plays
    /// the same beat again.
    ///
    /// Only ONE scene is ever live: physics here is the global PhysX world (no per-scene
    /// simulation), so a frozen scene means every body kinematic and every prop parked. That is
    /// also why the sub-stages are far apart and far from the pitch.
    /// </summary>
    public abstract class MenuScene
    {
        protected Transform Root;          // everything built hangs under here; one Destroy cascades
        protected Vector3 Origin;          // the sub-stage's world origin (feet level, y = 0)
        protected MenuSceneStage Stage;

        readonly List<Material> _mats = new List<Material>();
        readonly List<ActiveRagdoll> _bodies = new List<ActiveRagdoll>();
        protected BallController Ball;

        /// <summary>Seconds the choreography has been running since Thaw.</summary>
        protected float Clock;

        /// <summary>True once the beat has played out. The stage stops ticking, the bodies settle
        /// where they landed, and nothing restarts until the mouse leaves and comes back.</summary>
        public bool Done { get; protected set; }

        /// <summary>Where this scene's goal mouth sits, so the stage can point
        /// SimConfig.AttackGoalCenter at it while the scene is live.</summary>
        public Vector3 GoalCenter => Origin + new Vector3(0f, 0f, GoalOutZ);

        /// <summary>Distance from the sub-stage origin to its goal line. Scenes place their actors
        /// between the two, so the camera can look down +Z at the goal.</summary>
        protected const float GoalOutZ = 20f;

        public void Init(MenuSceneStage stage, Transform root, Vector3 origin)
        {
            Stage = stage; Root = root; Origin = origin;
        }

        /// <summary>Build the stage dressing and the actors. Called once.</summary>
        public abstract void Build();

        /// <summary>Advance the choreography. Only called while this scene is the live one and
        /// not yet Done. dt is scaled time, so the whole scene slows with the menu's slow-mo.</summary>
        public abstract void Tick(float dt);

        /// <summary>Put every actor and prop back to its built state. Must leave the scene looking
        /// exactly as it did on the first frame after Build.</summary>
        public abstract void Reset();

        /// <summary>Where the camera watches this scene from, in world space.</summary>
        public abstract void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov);

        /// <summary>
        /// Freeze the scene into a still picture that costs nothing.
        ///
        /// Kinematic bones alone are NOT free: ActiveRagdoll.FixedUpdate has no freeze gate, so it
        /// keeps writing 13 joint targets and sphere-casting for the ground every physics step
        /// whether or not the bones can move, and HairSim and AnatomySim integrate their own
        /// particles regardless. Four idle panels doing that on a menu is real cost for a picture
        /// nobody is looking at, so the components themselves are switched off.
        /// </summary>
        public virtual void Freeze()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rag = _bodies[i];
                if (rag == null) continue;
                rag.BecomeDisplayBody();
                rag.enabled = false;
                SetSims(rag, false);
            }
            if (Ball != null)
            {
                Ball.Rb.linearVelocity = Vector3.zero;
                Ball.Rb.angularVelocity = Vector3.zero;
                Ball.Rb.isKinematic = true;
                Ball.enabled = false;
            }
        }

        /// <summary>Hand the bodies back to physics and start the beat from zero. Reset() has
        /// already re-placed everything, so this only restores simulation.</summary>
        public virtual void Thaw()
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var rag = _bodies[i];
                if (rag == null) continue;
                rag.enabled = true;
                SetSims(rag, true);
                rag.BecomeLiveBody();
            }
            if (Ball != null) { Ball.enabled = true; Ball.Rb.isKinematic = false; }
            Clock = 0f;
            Done = false;
        }

        /// <summary>The cosmetic sims that run off their own FixedUpdate and would keep moving on
        /// a frozen body (hair especially, which has no teleport handling and would drift).</summary>
        static void SetSims(ActiveRagdoll rag, bool on)
        {
            var hair = rag.GetComponentsInChildren<HairSim>(true);
            for (int i = 0; i < hair.Length; i++) hair[i].enabled = on;
            var anat = rag.GetComponentsInChildren<AnatomySim>(true);
            for (int i = 0; i < anat.Length; i++) anat[i].enabled = on;
        }

        public virtual void Destroy()
        {
            for (int i = 0; i < _mats.Count; i++)
                if (_mats[i] != null) Object.Destroy(_mats[i]);
            _mats.Clear();
            _bodies.Clear();
        }

        // ---- shared build helpers -------------------------------------------------------------

        /// <summary>A material this scene owns and frees on teardown. Make.* materials belong to
        /// nobody, so an untracked one leaks every time the menu is reopened.</summary>
        protected Material M(Color c, float smoothness = 0.1f, float metallic = 0f)
        {
            var m = Make.Mat(c, smoothness, metallic);
            _mats.Add(m);
            return m;
        }

        protected Material Own(Material m) { if (m != null) _mats.Add(m); return m; }

        /// <summary>Register a body so Freeze/Thaw reach it and it is forgotten on teardown.</summary>
        protected ActiveRagdoll Track(ActiveRagdoll rag) { if (rag != null) _bodies.Add(rag); return rag; }

        /// <summary>
        /// The floor every sub-stage stands on: INVISIBLE, because these panels show the figures
        /// alone against the menu with no pitch, no goal and no sky. The collider still has to
        /// exist - the ragdoll's ground probe is a pelvis sphere-cast that accepts any non-trigger
        /// upward-facing collider, balance and locomotion are gated on it, and FloorRescue assumes
        /// a floor near y = 0 - so this is a real slab with its Renderer stripped and turf friction
        /// kept, which is what makes a body stand, run and slide the way it does in a match.
        /// </summary>
        protected void BuildFloor(float sizeX, float sizeZ, Vector3 center)
        {
            var go = Make.Box("MsFloor", new Vector3(sizeX, 1f, sizeZ), center + new Vector3(0f, -0.5f, 0f), null, Root);
            if (go.TryGetComponent<Renderer>(out var r)) Object.Destroy(r);
            if (go.TryGetComponent<Collider>(out var col))
                col.material = Make.PhysMat("MsTurf", 0.15f, 0.25f, 0.25f);
        }

        /// <summary>
        /// An invisible backstop so a struck ball is caught instead of sailing across the stage
        /// forever (there is no net here to stop it). Zero bounce on a Minimum combine, so the ball
        /// dies where it lands rather than pinging back into frame.
        /// </summary>
        protected void BuildCatcher(Vector3 size, Vector3 pos)
        {
            var go = Make.Box("MsCatch", size, pos, null, Root, collider: true);
            if (go.TryGetComponent<Renderer>(out var r) && r != null) Object.Destroy(r);
            if (go.TryGetComponent<Collider>(out var c))
                c.material = Make.PhysMat("MsCatch", 0f, 0.95f, 0.95f, PhysicsMaterialCombine.Minimum);
            go.AddComponent<NetBackstop>();   // deadens the ball's rebound on contact
        }

        /// <summary>The dynamic match ball, same build every mode uses.</summary>
        protected BallController BuildBall(Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "MsBall";
            go.transform.SetParent(Root, true);
            go.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = M(new Color(0.95f, 0.95f, 0.97f), 0.3f);
            go.AddComponent<Rigidbody>();
            Ball = go.AddComponent<BallController>();
            Ball.ResetTo(at);
            return Ball;
        }

        /// <summary>
        /// The protagonist: the local player's own look. The species is forced to Human because
        /// every choreography here (bicycle, slide, keeper dive) is authored for the biped pose
        /// tables, and a horse or elephant profile would build a quadruped that reads them
        /// differently. Everything else - height, girth, jersey, skin, hair, facial - is the
        /// player's own, which is the point of showing their body in the menu.
        /// </summary>
        protected ActiveRagdoll BuildPlayerBody(string name, Vector3 feet, Quaternion facing, bool gloves)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, true);
            var rag = go.AddComponent<ActiveRagdoll>();
            var look = PlayerProfile.Appearance;
            look.SpeciesId = Species.HumanId;
            Material torso = PlayerProfile.JerseyTex != null
                ? Own(Make.MatTex(PlayerProfile.JerseyTex))
                : M(PlayerProfile.JerseyBase);
            Material limbs = M(look.Skin);
            // Human scales only: PlayerProfile.HeightScale folds in Species.Current.VisualScale,
            // which would size the body for a species we just refused to build.
            // Human-relative scales ONLY. PlayerProfile.HeightScale and GirthScale both multiply in
            // Species.Current's visual scale, which is still the horse or elephant the player last
            // picked even though the appearance above is forced Human - and a Human skeleton built
            // at elephant size would blow every hand-authored distance in the choreography.
            rag.BuildScaled(feet, facing, torso, limbs,
                            PlayerProfile.BodyHeightScale, HumanGirth(), PlayerProfile.MassMul,
                            withGloves: gloves, appearance: look);
            return Track(rag);
        }

        /// <summary>PlayerProfile.GirthScale with the species factor left out (see BuildPlayerBody).</summary>
        static float HumanGirth()
        {
            var s = Species.Current;
            float g = s != null && s.VisualGirth > 0.001f ? PlayerProfile.GirthScale / s.VisualGirth
                                                          : PlayerProfile.GirthScale;
            return Mathf.Clamp(g, 0.5f, 2f);
        }

        /// <summary>A plain AI body in flat kit colours (no cosmetics, no profile scaling).</summary>
        protected ActiveRagdoll BuildAiBody(string name, Vector3 feet, Quaternion facing,
                                            Color torso, Color limb, bool gloves = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Root, true);
            var rag = go.AddComponent<ActiveRagdoll>();
            rag.Build(feet, facing, M(torso), M(limb), withGloves: gloves);
            return Track(rag);
        }

        /// <summary>
        /// The cosmetic run gait, same shape MenuBackground and Footballer use: pose overrides on
        /// top of live locomotion so the legs visibly stride. Call AFTER any driver Tick in the
        /// same frame - every driver clears pose overrides at the top of its own tick.
        /// </summary>
        protected static void RunGait(ActiveRagdoll rag, ref float phase, float dt, float amount = 1f)
        {
            if (rag == null) return;
            rag.ClearPoseOverrides();
            if (amount < 0.05f) { phase = 0f; return; }
            phase += dt * SimConfig.StrideRateMax * amount;
            float s = Mathf.Sin(phase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            rag.SetPoseOverride(Bone.ThighL, new Vector3(-s * SimConfig.GaitThighSwing - liftL * SimConfig.GaitThighLift, 0f, 0f));
            rag.SetPoseOverride(Bone.CalfL, new Vector3(liftL * SimConfig.GaitKneeBend, 0f, 0f));
            rag.SetPoseOverride(Bone.ThighR, new Vector3(s * SimConfig.GaitThighSwing - liftR * SimConfig.GaitThighLift, 0f, 0f));
            rag.SetPoseOverride(Bone.CalfR, new Vector3(liftR * SimConfig.GaitKneeBend, 0f, 0f));
            rag.SetPoseOverride(Bone.UpperArmR, new Vector3(s * SimConfig.ArmPumpSwing, 0f, 0f));
            rag.SetPoseOverride(Bone.ForearmR, new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            rag.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * SimConfig.ArmPumpSwing, 0f, 0f));
            rag.SetPoseOverride(Bone.ForearmL, new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
        }

        /// <summary>Steer a body toward a flat target under its own locomotion. Returns the
        /// remaining flat distance so the caller can decide when it has arrived.</summary>
        protected static float Jog(ActiveRagdoll rag, Vector3 target, float speed, ref float phase, float dt)
        {
            Vector3 me = rag.Pelvis != null ? rag.Pelvis.position : target;
            Vector3 to = new Vector3(target.x - me.x, 0f, target.z - me.z);
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.forward;
            rag.UprightLock = true;
            rag.LocomotionEnabled = true;
            rag.MoveInput = dir * speed;
            rag.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
            RunGait(rag, ref phase, dt);
            return dist;
        }

        /// <summary>
        /// Solve a camera that FITS a world-space box, for a viewport of the given aspect. The
        /// alternative is hand-placing five cameras and re-checking them whenever a scene's
        /// geometry moves, which is how figures end up cropped or off frame.
        ///
        /// `centre` is what the shot is about, `extents` how far the action reaches from it
        /// (x across the view, y vertical, z along the view), `dir` the direction the camera looks
        /// FROM (normalised, flat or tilted as the scene wants), and `fov` the vertical field of
        /// view. The distance is solved for BOTH axes and the larger wins, so nothing is cropped.
        /// </summary>
        protected static void FitCamera(Vector3 centre, Vector3 extents, Vector3 dir, float fov,
                                        float aspect, out Vector3 camPos, out Vector3 lookAt)
        {
            lookAt = centre;
            float halfV = fov * 0.5f * Mathf.Deg2Rad;
            // Vertical fit, plus the horizontal fit converted through the aspect.
            float distV = extents.y / Mathf.Max(0.05f, Mathf.Tan(halfV));
            float halfH = Mathf.Atan(Mathf.Tan(halfV) * Mathf.Max(0.1f, aspect));
            float distH = extents.x / Mathf.Max(0.05f, Mathf.Tan(halfH));
            // Depth of the box pushes the camera back too, or the near half fills the frame.
            float dist = Mathf.Max(distV, distH) + extents.z;
            camPos = centre + dir.normalized * dist;
        }

        /// <summary>The aspect the panels are drawn at, so FitCamera solves for the real shape.
        /// Wide and short: the grid gives a panel about 1.35 : 1 at most.</summary>
        protected const float PanelAspect = 1.35f;

        /// <summary>Stop a jog cleanly: no steering, no stale stride on the bones.</summary>
        protected static void StopJog(ActiveRagdoll rag, Quaternion facing, ref float phase)
        {
            rag.MoveInput = Vector3.zero;
            rag.FacingRotation = facing;
            rag.ClearPoseOverrides();
            phase = 0f;
        }
    }
}
