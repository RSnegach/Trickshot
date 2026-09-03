using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// STRIKER: the player's own body jumps, bicycle-kicks a ball away and lands limp on his back.
    ///
    /// Just the figure and the ball - no feeder, no goal, no ground. The ball is launched by code
    /// from off-camera so it meets the raised boot at the top of the flip; nothing else is in frame.
    ///
    /// The flip is not posed by hand. A ScriptedInput drives the REAL Striker controller down the
    /// same path a player takes: a standing jump arms the wheel, a few scroll steps lean the air
    /// pitch target back past BicycleArmPitch (which latches the bicycle window), and one held leg
    /// snaps the kicking leg up. The ball meeting that leg while the window is live is what makes
    /// it a bicycle. The landing is physics too - he arrives tipped past TumbleUpness, so
    /// AirPitchControl starts a tumble and he goes down limp instead of popping upright.
    /// </summary>
    public class StrikerScene : MenuScene
    {
        // Turned a little further off-centre than a straight three-quarter, so the flip is read
        // across his chest rather than as a flat silhouette or head-on.
        static readonly Vector3 FaceDir = new Vector3(0.72f, 0f, -0.69f);

        // Served from in front of him and low, so it RISES into the boot: a ball dropping onto a
        // raised foot never reads as a strike.
        // Very close in: the ball only has to travel a metre or so, which keeps the boot
        // prediction honest (see PredictedBoot) and keeps it rising into the foot.
        static readonly Vector3 ServeFrom = new Vector3(-1.05f, 1.15f, 0.55f);
        // WHERE THE BOOT ACTUALLY IS at peak lean, measured off a real run rather than guessed:
        // local (0.03, 1.50, -0.31) from his standing spot. The old target was 0.45 m too high and
        // half a metre the wrong side of him, so the ball crossed in front and fell away - which is
        // why contact kept classifying as a header instead of a bicycle.
        static readonly Vector3 ContactAt = new Vector3(0.05f, 1.52f, -0.28f);
        // Flight time to that point. Tuned against the same run: the flip reaches peak lean about
        // a third of a second after take-off, and the ball has to be there then, not before.
        const float ServeTime = 0.14f;
        const float ServeLift = 0.35f;   // how high the steered serve arcs on its way to the boot

        // The beat, in seconds from Thaw. He goes up IMMEDIATELY on hover: a menu panel gets one
        // glance, and half a second of a man standing still spends most of it.
        const float TJump = 0.02f;
        // Serve the instant he tips past this (1 = upright, 0 = horizontal, negative = past it).
        // Late, because the leg is only properly over once he is near horizontal - serving while
        // he was still nearly upright put the ball past his hip before the boot arrived.
        const float ServeAtUp = 0.18f;
        const float THold = 4.0f;

        ActiveRagdoll _rag;
        Striker _striker;
        ScriptedInput _input;

        Vector3 _spot, _ballHome, _kickTarget;
        Quaternion _facing;
        bool _jumped, _served;
        int _scrolls, _airFrames;
        // The steered serve (see TickHoming): where it started and how far along it is.
        bool _homing; float _homeT; Vector3 _homeFrom;

        /// <summary>What the last run actually did, for tuning: the highest the pelvis got, how
        /// far he leaned (the most upside-down he was), the closest the ball came to the kicking
        /// boot, and whether the landing tumbled. Read after a run rather than trying to catch a
        /// 0.7 s flip mid-air.</summary>
        public float PeakY, MinUp = 1f, MinFootBall = 999f;
        public bool Tumbled;
        /// <summary>Where the kicking boot was at peak lean, and the ball at that moment: the two
        /// numbers the serve has to be aimed between.</summary>
        public Vector3 BootAtPeak, BallAtPeak;

        public override void Build()
        {
            _spot = Origin;
            _facing = Quaternion.LookRotation(FaceDir.normalized, Vector3.up);
            _ballHome = _spot + ServeFrom;
            _kickTarget = _spot + ContactAt;

            // Wide enough that the struck ball lands ON it rather than sailing off the edge and
            // falling forever - it leaves the boot at match pace, and nothing here is a goal net.
            BuildFloor(60f, 60f, _spot);
            BuildBall(_ballHome);

            _rag = BuildPlayerBody("MsStriker", _spot, _facing, gloves: false);
            _input = new ScriptedInput();
            _striker = _rag.gameObject.AddComponent<Striker>();
            _striker.Init(_input, _rag);
            _striker.SetBall(Ball);
            // The payoff of this panel is the landing. An Acrobat player would otherwise get the
            // other branch of AirPitchControl - a wider flip clamp and a snap back to his feet -
            // and never see the back-landing the scene exists to show.
            _striker.IgnoreAcrobat = true;
            // Without detectors a boot meeting the ball is plain physics: the bicycle classification
            // and its pace bonus both live in KickDetector.
            var strike = _rag.StrikeBones;
            for (int i = 0; i < strike.Length; i++)
            {
                var rb = _rag.Rb(strike[i]);
                if (rb == null) continue;
                rb.gameObject.AddComponent<KickDetector>().Init(_striker, _rag, Ball);
            }
            // No Dribble on this body, and NoCarry so a ball passing his feet is never trapped -
            // this scene wants it to fly through to the raised boot.
            Ball.NoCarry = true;
        }

        public override void Reset()
        {
            _striker.ForceRecover();
            _rag.ResetTo(_spot, _facing);
            Ball.ResetTo(_ballHome);
            _input.Clear();
            _jumped = false; _served = false; _scrolls = 0; _airFrames = 0;
            _homing = false; _homeT = 0f;
            if (Ball != null) Ball.Rb.isKinematic = false;
            PeakY = 0f; MinUp = 1f; MinFootBall = 999f; Tumbled = false;
            BootAtPeak = Vector3.zero; BallAtPeak = Vector3.zero;
            Clock = 0f;
            Done = false;
        }

        public override void Tick(float dt)
        {
            Clock += dt;

            // THE BALL IS FLOWN ONTO THE BOOT, not launched at a guess.
            //
            // Every ballistic serve tried here missed, and the reason is structural rather than a
            // matter of tuning: LaunchTo solves an arc THROUGH a point chosen when the ball leaves,
            // while the boot is being swung by a whole-body torque whose rate depends on the
            // player's AirFlipMul and on the frame rate. Predicting where a bone under those forces
            // will be a fifth of a second later is not reliable, and a miss of 20 cm is a miss.
            //
            // So the ball is steered instead: held kinematic and driven along an arc that ENDS on
            // the boot's live position, re-read every frame. It reads as a served ball rising to
            // meet the kick, and it cannot miss. The instant it arrives the ball is handed back to
            // physics with the pace it had, and the real strike path takes over from there - the
            // KickDetector bonus, the shot classification and the pace all still come from the
            // engine, which is what makes it a bicycle kick and not an animation.
            if (!_served && _jumped && !_rag.IsGrounded
                && Vector3.Dot(_rag.Pelvis.transform.up, Vector3.up) < ServeAtUp)
            {
                _served = true;
                _homing = true;
                _homeT = 0f;
                _homeFrom = _ballHome;
                Ball.Rb.isKinematic = true;
                Ball.ResetTo(_ballHome);
            }

            if (_homing) TickHoming(dt);

            if (!_jumped && Clock >= TJump)
            {
                _input.Jump = true;    // one frame of press: Striker jumps on the edge
                _jumped = true;
            }
            else if (_jumped)
            {
                _input.Jump = false;
                // Lean back, then raise the boot. Both are gated on the frames AFTER the jump
                // rather than on IsGrounded: the ground probe is a pelvis sphere-cast that keeps
                // reading grounded for the first stretch of the rise (it sees turf up to about a
                // metre below him), so waiting for it to clear would spend most of the airborne
                // window standing still. Scroll is only consumed while the wheel is armed, which
                // the jump does and the landing undoes, so extra frames here are harmless.
                _airFrames++;
                // SPEND THE SCROLL ONLY WHILE GENUINELY AIRBORNE. Striker.AirPitchControl reads
                // Scroll in its airborne branch alone; on a grounded frame it takes the landing
                // branch and the step is simply discarded. The pelvis probe keeps reporting
                // grounded for the first part of the rise, so scroll sent on those frames is
                // thrown away - which is exactly why the lean used to end at zero and he came
                // down on his feet. Waiting for !IsGrounded costs a few frames of the window and
                // buys the entire flip.
                //
                // Each accepted frame is one AirPitchStep (30 deg), clamped at AirPitchLimit 115.
                // Four clears the 55 deg that arms the bicycle window; the rest carry him past
                // horizontal, which is what makes the landing a tumble rather than a hop.
                if (!_rag.IsGrounded && _scrolls < 5) { _input.ScrollWish = -1f; _scrolls++; }
                // ONE leg, held from take-off: it snaps up at BicycleLegEase and has to be there
                // when the ball arrives. Both held airborne is the header pose, and carried
                // through touchdown that resolves to a sit or a dive, not the tumble.
                _input.LegR = true;
            }

            _input.Commit();
            _striker.Tick();

            // Run record (see the fields): sampled every tick while the beat plays.
            if (_rag.Pelvis != null)
            {
                PeakY = Mathf.Max(PeakY, _rag.Pelvis.position.y);
                float up = Vector3.Dot(_rag.Pelvis.transform.up, Vector3.up);
                var boot = _rag.Rb(Bone.FootR);
                if (up < MinUp)
                {
                    MinUp = up;
                    if (boot != null) BootAtPeak = boot.position;
                    if (Ball != null) BallAtPeak = Ball.transform.position;
                }
                if (boot != null && Ball != null)
                    MinFootBall = Mathf.Min(MinFootBall, Vector3.Distance(boot.position, Ball.transform.position));
            }
            if (_striker.IsTumbling) Tumbled = true;

            if (Clock >= THold) Done = true;
        }

        /// <summary>
        /// Fly the ball from where it was served onto the kicking boot, re-reading the boot every
        /// frame, and release it into physics on arrival with the pace it was carrying.
        ///
        /// The arc is a straight interpolation with a lift term, so it looks served rather than
        /// dragged. On arrival the ball is handed back with a velocity derived from its last step,
        /// which is what the strike path then amplifies - the contact itself, the bicycle
        /// classification and the pace bonus all still come from KickDetector and BallController.
        /// </summary>
        void TickHoming(float dt)
        {
            var boot = _rag.Rb(Bone.FootR);
            if (boot == null) { Release(Vector3.zero); return; }

            _homeT += dt / Mathf.Max(0.01f, ServeTime);
            Vector3 target = boot.position;
            Vector3 prev = Ball.Rb.position;

            if (_homeT >= 1f)
            {
                // Arrived. Hand it back moving the way it was travelling, so the boot meets a ball
                // with real pace rather than one sitting still in the air.
                Vector3 v = dt > 1e-5f ? (target - prev) / dt : Vector3.zero;
                Ball.Rb.position = target;
                Ball.transform.position = target;
                Release(Vector3.ClampMagnitude(v, 12f));
                return;
            }

            // Straight line plus a lift, so it rises into the foot instead of sliding along.
            Vector3 p = Vector3.Lerp(_homeFrom, target, _homeT);
            p.y += Mathf.Sin(_homeT * Mathf.PI) * ServeLift;
            Ball.Rb.position = p;
            Ball.transform.position = p;
        }

        void Release(Vector3 velocity)
        {
            _homing = false;
            Ball.Rb.isKinematic = false;
            Ball.Rb.linearVelocity = velocity;
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Side on, slightly above head height: the flip reads as a silhouette turning over.
            // Framed on the flip, not the stage: the panel is wide and short and the fov is
            // VERTICAL, so the camera sits close and low with the body filling the height.
            // Side on, fitted to the box the whole beat happens in: he starts standing, rises
            // about 1.3 m, turns over and lands. Solved rather than hand-placed so the shot stays
            // correct if the geometry moves.
            // Centred on HIM in x, with the box tall enough to hold the jump and the landing.
            // The camera sits level with his chest so the whole turn happens in frame.
            fov = 42f;
            FitCamera(_spot + new Vector3(0f, 1.25f, 0f), new Vector3(1.35f, 1.45f, 0.4f),
                      new Vector3(1f, 0.16f, -0.62f), fov, PanelAspect, out camPos, out lookAt);
        }
    }
}
