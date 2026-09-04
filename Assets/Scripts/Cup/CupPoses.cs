using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The cup's static poses and the two body helpers its choreography and its ceremony share
    /// (design 7.3, 7.6, 8.1): the arms-round-shoulders lineup set, the three looking-down podium
    /// loser poses, a dejected idle, the held arms of the fallen shooter, and a walking gait plus a
    /// steer for "walk this body to that mark".
    ///
    /// Every pose is a per-bone Euler offset in RagdollPose's format (13 entries, index = Bone),
    /// layered on the Stand rest pose: SetPose(Stand) + SetPoseOverride(bone, euler) on a live
    /// body, or DisplayPose(..., pose) on a kinematic puppet (the podium losers).
    ///
    /// SIGN CONVENTIONS - derived, not copied, because the tree disagrees with itself. The target
    /// skeleton's rest local rotation is IDENTITY for every biped bone (ActiveRagdoll.Build:
    /// "Identity for every biped bone"), so a bone's local axes ARE the body's: +X right, +Y up,
    /// +Z forward, and a pose Euler is applied in that frame (targetLocal = rest * Euler(e); the
    /// joints then follow the target skeleton faithfully through JointMath.SetTargetRotationLocal).
    /// Unity's positive rotation about +Z takes +X toward +Y, so it takes a hanging limb (-Y)
    /// toward +X. Hence:
    ///
    ///   Z (lateral): +Z swings a hanging arm or leg toward the character's RIGHT, -Z toward his
    ///                LEFT. OUTWARD is therefore -Z on a LEFT limb and +Z on a RIGHT limb. This is
    ///                KeeperPose's rule (its header records the on-screen bug that proved it: a
    ///                keeper whose arms crossed his chest). EmotePose's header states the opposite
    ///                and its symmetric poses are authored to it; when a cup pose looks mirrored,
    ///                trust this block.
    ///   X (fore/aft): +X pitches a limb's lower end BACKWARD (a knee folds with +X on the calf,
    ///                a thigh swings forward with -X); on an upper arm -X raises it FORWARD; on a
    ///                forearm -X bends the elbow so the hand comes forward and up. Torso +X leans
    ///                forward; Head +X looks down.
    ///   Y (twist):   about the vertical. On an upper arm already swung out to the side, a Y
    ///                rotation sweeps it fore/aft: +Y takes a LEFT arm forward and a RIGHT arm
    ///                back (Euler order is Z, then X, then Y).
    ///
    /// Arrays are shared statics: read them, never write into them.
    /// </summary>
    public static class CupPoses
    {
        // ==========================================================================================
        // Arms round shoulders (design 7.3)
        // ==========================================================================================

        // Two neighbours each drape an arm behind the other's neck, and both arms cross the SAME
        // gap: with a single elevation the two upper arms overlapped there (seen in play mode:
        // PhysX folded both arms straight up, hands meeting above the heads, or shoved the pair
        // apart by 0.3-0.6 m). So the two arms of a pair take different heights, by a fixed rule:
        // the arm going toward the RIGHT neighbour goes HIGH (over his shoulder, elbow 0.17 m above
        // shoulder height, behind his neck), the arm toward the LEFT neighbour goes LOW (round his
        // back, elbow 0.05 m below shoulder height, hand at his chest height). Every pair then has
        // one of each, 0.22 m apart. Measured on two live bodies 0.62 m apart with the neighbour
        // collisions off (CupChoreo does that for the line): high elbow at (+0.15, +0.17, -0.23)
        // from its shoulder, hand 0.10 m short of the neighbour's centre at shoulder-blade height,
        // 0.4 m behind his back; low elbow at (-0.25, -0.05, -0.21), hand at the neighbour's centre,
        // chest height, 0.3 m behind his back.
        /// <summary>The HIGH drape (toward the right neighbour): upper arm 30 deg above the shoulder line.</summary>
        public const float DrapeOutHigh = 120f;
        /// <summary>The HIGH drape sweeps further back so the raised arm clears the neighbour's head sphere.</summary>
        public const float DrapeBackHigh = 55f;
        /// <summary>The HIGH drape's forearm folds well down, landing the hand behind the neighbour's shoulder blade.</summary>
        public const float DrapeDownHigh = 70f;
        /// <summary>The LOW drape (toward the left neighbour): upper arm 10 deg below the shoulder line, round his back.</summary>
        public const float DrapeOutLow = 80f;
        /// <summary>
        /// How far the LOW arm sweeps BACK (deg): the neighbour's torso back face is at z = -0.11,
        /// and 40 deg puts the elbow at z = -0.21 behind it.
        /// </summary>
        public const float DrapeBackLow = 40f;
        /// <summary>The LOW drape's forearm bends a little down and across the neighbour's back.</summary>
        public const float DrapeDownLow = 40f;

        /// <summary>Both arms draped: a body with a neighbour on each side (left arm LOW, right arm HIGH).</summary>
        public static readonly Vector3[] ArmsRoundBoth = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(0f, -DrapeBackLow, -DrapeOutLow)),    // out to HIS left, a touch down, swept back
            (Bone.ForearmL,  new Vector3(0f, 0f, DrapeDownLow)),                // bends down (opposite sign to the upper arm's Z)
            (Bone.UpperArmR, new Vector3(0f, DrapeBackHigh, DrapeOutHigh)),    // out to HIS right and up, swept well back
            (Bone.ForearmR,  new Vector3(0f, 0f, -DrapeDownHigh)),
            (Bone.Torso,     new Vector3(2f, 0f, 0f)),
        });

        /// <summary>The LEFT arm draped (a neighbour on the left only): the right end of a line, LOW.</summary>
        public static readonly Vector3[] ArmsRoundLeft = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(0f, -DrapeBackLow, -DrapeOutLow)),
            (Bone.ForearmL,  new Vector3(0f, 0f, DrapeDownLow)),
            (Bone.UpperArmR, new Vector3(4f, 0f, 6f)),                 // the free arm hangs, a touch out
            (Bone.Torso,     new Vector3(2f, 0f, -3f)),                 // leans a little into the line
        });

        /// <summary>The RIGHT arm draped (a neighbour on the right only): the left end of a line, HIGH.</summary>
        public static readonly Vector3[] ArmsRoundRight = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmR, new Vector3(0f, DrapeBackHigh, DrapeOutHigh)),
            (Bone.ForearmR,  new Vector3(0f, 0f, -DrapeDownHigh)),
            (Bone.UpperArmL, new Vector3(4f, 0f, -6f)),
            (Bone.Torso,     new Vector3(2f, 0f, 3f)),
        });

        /// <summary>
        /// A body alone in its lineup (the common case: the AI's spare body watching from x = +6,
        /// and a Co-op team of one shooter): hands on the hips, chin up, watching the kick.
        /// </summary>
        public static readonly Vector3[] LoneWatch = Set(New(), new (Bone, Vector3)[]
        {
            // Hands on the hips: the elbow OUT (left limb: -Z) and a little back, the forearm bent
            // and swung IN (+Z on the left) so the hand lands on the hip - (-0.23, 1.08, 0.05)
            // for a hip at about (-0.19, 1.02, 0.06).
            (Bone.UpperArmL, new Vector3(10f, 0f, -30f)),
            (Bone.UpperArmR, new Vector3(10f, 0f, 30f)),
            (Bone.ForearmL,  new Vector3(-65f, 0f, 60f)),
            (Bone.ForearmR,  new Vector3(-65f, 0f, -60f)),
            (Bone.Head,      new Vector3(-4f, 0f, 0f)),
        });

        /// <summary>The lineup pose for a body with the given neighbours.</summary>
        public static Vector3[] ArmsRound(bool leftNeighbour, bool rightNeighbour)
        {
            if (leftNeighbour && rightNeighbour) return ArmsRoundBoth;
            if (leftNeighbour) return ArmsRoundLeft;
            if (rightNeighbour) return ArmsRoundRight;
            return LoneWatch;
        }

        // ==========================================================================================
        // Watching from the scatter (Free Kicks: no lineup, a loose group behind the taker)
        // ==========================================================================================
        // Three casual stands built ONLY from arm numbers the editor pass measured on live bodies
        // (LoneWatch's hands-on-hips, LoserHandsBehindBack's clasp), never from unverified FK:
        // hands on the hips, hands clasped behind the back, one hand on a hip. Heads level (the
        // choreography turns them to the ball), a touch of torso lean so no two stand alike.

        /// <summary>Hands on the hips, chin up: LoneWatch.</summary>
        public static readonly Vector3[] WatchHandsOnHips = LoneWatch;

        /// <summary>Hands clasped behind the back (the podium loser's clasp with the head level), watching.</summary>
        public static readonly Vector3[] WatchHandsBehindBack = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(35f, 0f, -4f)),
            (Bone.UpperArmR, new Vector3(35f, 0f, 4f)),
            (Bone.ForearmL,  new Vector3(-40f, 0f, 45f)),
            (Bone.ForearmR,  new Vector3(-40f, 0f, -45f)),
            (Bone.Torso,     new Vector3(3f, 0f, 0f)),
            (Bone.Head,      new Vector3(-3f, 0f, 0f)),
        });

        /// <summary>The left hand on the hip, the right arm hanging a touch out, weight on the left leg.</summary>
        public static readonly Vector3[] WatchOneHandOnHip = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(10f, 0f, -30f)),
            (Bone.ForearmL,  new Vector3(-65f, 0f, 60f)),
            (Bone.UpperArmR, new Vector3(4f, 0f, 6f)),
            (Bone.Torso,     new Vector3(2f, 0f, -2f)),
            (Bone.Head,      new Vector3(-3f, 0f, 0f)),
        });

        /// <summary>One of the three watching stands by a seeded variant (0..2; anything else wraps).</summary>
        public static Vector3[] WatchPose(int variant)
        {
            switch (((variant % 3) + 3) % 3)
            {
                case 0: return WatchHandsOnHips;
                case 1: return WatchHandsBehindBack;
                default: return WatchOneHandOnHip;
            }
        }

        // ==========================================================================================
        // Podium losers (design 8.1): static display bodies, each looking down
        // ==========================================================================================

        public static readonly Vector3[] LoserHandsOnHips = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(10f, 0f, -30f)),   // the LoneWatch arms (hands land on the hips)
            (Bone.UpperArmR, new Vector3(10f, 0f, 30f)),
            (Bone.ForearmL,  new Vector3(-65f, 0f, 60f)),
            (Bone.ForearmR,  new Vector3(-65f, 0f, -60f)),
            (Bone.Torso,     new Vector3(6f, 0f, 0f)),
            (Bone.Head,      new Vector3(32f, 0f, 0f)),
        });

        public static readonly Vector3[] LoserHandsOnHead = Set(New(), new (Bone, Vector3)[]
        {
            // Upper arms 20 deg short of vertical with the elbows flared out. With the arm raised
            // sideways the ELBOW BENDS ABOUT Z (the frontal plane), not X: a Z fold of 105 brings
            // the hand to the crown at (-0.06, 1.91, 0); an X fold would send it forward past the
            // face. Head bowed under the hands.
            (Bone.UpperArmL, new Vector3(0f, 0f, -160f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 160f)),
            (Bone.ForearmL,  new Vector3(0f, 0f, -105f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, 105f)),
            (Bone.Torso,     new Vector3(8f, 0f, 0f)),
            (Bone.Head,      new Vector3(28f, 0f, 0f)),
        });

        public static readonly Vector3[] LoserHandsBehindBack = Set(New(), new (Bone, Vector3)[]
        {
            // Upper arms swung back 35, forearms bent and swung in so the hands meet at the small
            // of the back: (-0.04, 1.08, -0.17), behind the torso's back face at -0.11.
            (Bone.UpperArmL, new Vector3(35f, 0f, -4f)),
            (Bone.UpperArmR, new Vector3(35f, 0f, 4f)),
            (Bone.ForearmL,  new Vector3(-40f, 0f, 45f)),
            (Bone.ForearmR,  new Vector3(-40f, 0f, -45f)),
            (Bone.Torso,     new Vector3(5f, 0f, 0f)),
            (Bone.Head,      new Vector3(30f, 0f, 0f)),
        });

        /// <summary>One of the three loser poses by a seeded variant (0..2; anything else wraps).</summary>
        public static Vector3[] LoserPose(int variant)
        {
            switch (((variant % 3) + 3) % 3)
            {
                case 0: return LoserHandsOnHips;
                case 1: return LoserHandsOnHead;
                default: return LoserHandsBehindBack;
            }
        }

        // ==========================================================================================
        // Dejection (design 7.6)
        // ==========================================================================================

        /// <summary>A slumped stand: shoulders forward, arms hanging a little ahead, head down. The losers between beats.</summary>
        public static readonly Vector3[] DejectedIdle = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,     new Vector3(10f, 0f, 0f)),
            (Bone.Head,      new Vector3(25f, 0f, 0f)),
            (Bone.UpperArmL, new Vector3(-6f, 0f, -4f)),
            (Bone.UpperArmR, new Vector3(-6f, 0f, 4f)),
            (Bone.ForearmL,  new Vector3(-12f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-12f, 0f, 0f)),
        });

        /// <summary>
        /// The arms-on-head hold of the fallen shooter (design 7.6 #3), the same shape the
        /// DejectFall emote reaches at p = 0.35: kept on the body by the choreography after the
        /// emote ends, so he lies on his back with his hands still clasped behind his head.
        /// </summary>
        public static readonly Vector3[] DejectFallArms = Set(New(), new (Bone, Vector3)[]
        {
            // Elbows flared, forearms folded about Z (see LoserHandsOnHead) with a touch of X so
            // the hands clasp at the back of the head: (-0.07, 1.77, -0.07).
            (Bone.UpperArmL, new Vector3(0f, 0f, -165f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 165f)),
            (Bone.ForearmL,  new Vector3(-20f, 0f, -130f)),
            (Bone.ForearmR,  new Vector3(-20f, 0f, 130f)),
            (Bone.Head,      new Vector3(-14f, 0f, 0f)),
        });

        /// <summary>The emote a dejection variant plays (0 knees + face in hands, 1 hands on hips, 2 arms on head + fall).</summary>
        public static Celebration.Emote DejectEmote(int variant)
        {
            switch (((variant % 3) + 3) % 3)
            {
                case 0: return Celebration.Emote.DejectKnees;
                case 1: return Celebration.Emote.DejectHips;
                default: return Celebration.Emote.DejectFall;
            }
        }

        /// <summary>The variant whose body has to be freed to fall (CupChoreo drops balance at CupTuning.DejectionFallHold).</summary>
        public const int FallVariant = 2;

        // ==========================================================================================
        // Celebration picks for AI bodies (a human picks from the wheel)
        // ==========================================================================================

        /// <summary>Standing cheers an AI lineup cycles through on a won round.</summary>
        public static readonly Celebration.Emote[] WinEmotes =
        {
            Celebration.Emote.Cheer, Celebration.Emote.HandsUp, Celebration.Emote.FistPump,
            Celebration.Emote.Wave2, Celebration.Emote.Clap, Celebration.Emote.MuscleFlex,
        };

        /// <summary>What an AI scorer does with his five seconds (KneeSlide is the one physics emote worth the risk on flat turf).</summary>
        public static readonly Celebration.Emote[] ScorerEmotes =
        {
            Celebration.Emote.FistPump, Celebration.Emote.KneeSlide, Celebration.Emote.Cheer,
            Celebration.Emote.Point, Celebration.Emote.HandsUp, Celebration.Emote.Wave2,
        };

        // ==========================================================================================
        // Applying poses
        // ==========================================================================================

        /// <summary>Set the pose's non-zero bones as overrides (the caller cleared or owns the rest).</summary>
        public static void Apply(ActiveRagdoll rag, Vector3[] pose)
        {
            if (rag == null || pose == null) return;
            int n = Mathf.Min(pose.Length, (int)Bone.Count);
            for (int i = 0; i < n; i++)
                if (pose[i] != Vector3.zero) rag.SetPoseOverride((Bone)i, pose[i]);
        }

        /// <summary>Apply a pose scaled by k (0 = rest, 1 = the pose): an ease-in for a pose that starts from a stand.</summary>
        public static void ApplyBlend(ActiveRagdoll rag, Vector3[] pose, float k)
        {
            if (rag == null || pose == null) return;
            k = Mathf.Clamp01(k);
            int n = Mathf.Min(pose.Length, (int)Bone.Count);
            for (int i = 0; i < n; i++)
                if (pose[i] != Vector3.zero) rag.SetPoseOverride((Bone)i, pose[i] * k);
        }

        /// <summary>
        /// A slow breath on top of whatever is applied: the torso pitch and the head nod by a
        /// couple of degrees. `t` is a clock, `phase` a per-body offset so a line never breathes
        /// in unison; `amount` scales it (1 = the lineup's).
        /// </summary>
        public static void Breathe(ActiveRagdoll rag, float t, float phase, float amount)
        {
            if (rag == null || amount <= 0f) return;
            float s = Mathf.Sin(t * 1.15f + phase);
            rag.AddPoseOverride(Bone.Torso, new Vector3(1.6f * s * amount, 0f, 0f));
            rag.AddPoseOverride(Bone.Head, new Vector3(-1.2f * s * amount, 0f, 0f));
        }

        /// <summary>
        /// Turn the head toward a world point within a cone: yaw up to `maxYaw`, pitch up to
        /// `maxPitch` (deg), the rest of the body untouched. A point behind the body is ignored
        /// (the head cannot look over the shoulder; the cone clamps to its edge instead).
        /// Additive, so it layers on a pose that already posed the head.
        /// </summary>
        public static void LookAt(ActiveRagdoll rag, Vector3 point, float maxYaw, float maxPitch)
        {
            if (rag == null || rag.Pelvis == null) return;
            Vector3 eye = rag.Pelvis.position + Vector3.up * 0.75f;
            Vector3 to = point - eye;
            Vector3 local = Quaternion.Inverse(rag.FacingRotation) * to;
            float flat = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            if (flat < 0.05f) return;
            float yaw = Mathf.Atan2(local.x, Mathf.Max(0.01f, local.z)) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg;   // +X on the head looks DOWN
            yaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
            pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
            rag.AddPoseOverride(Bone.Head, new Vector3(pitch, yaw, 0f));
            // A little of the turn comes from the shoulders, as it does on a real neck.
            rag.AddPoseOverride(Bone.Torso, new Vector3(0f, yaw * 0.18f, 0f));
        }

        // ==========================================================================================
        // Walking
        // ==========================================================================================

        /// <summary>
        /// The cosmetic alternating-leg gait (MenuScene.RunGait's shape, CupReferee's walking
        /// amounts): a stride whose rate and lift scale with `amount` (0.35 reads as a walk, 1 as
        /// a run). Sets overrides on the legs and arms; call it AFTER anything that clears them.
        /// </summary>
        public static void WalkGait(ActiveRagdoll rag, ref float phase, float dt, float amount)
        {
            if (rag == null) return;
            if (amount < 0.05f) { phase = 0f; return; }
            amount = Mathf.Clamp01(amount);
            // Cadence is only mildly speed-dependent (a walk is ~2 steps/s, a sprint ~3): the
            // first pass scaled the rate straight by the amount, which at a walk gave one stride
            // every 1.3 s - feet sliding under a slow high-knee jog (seen in play mode).
            phase += dt * SimConfig.StrideRateMax * Mathf.Lerp(0.6f, 1f, amount);
            float s = Mathf.Sin(phase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            // A walk swings less and lifts MUCH less than a sprint: at the walk end (amount ~0.3)
            // the thigh swings 22 deg, the knee lifts 19 and folds 68 - a stride, not a high-knee
            // run; the amounts climb to the run gait at 1.
            float swing = SimConfig.GaitThighSwing * Mathf.Lerp(0.55f, 1f, amount);
            float lift = SimConfig.GaitThighLift * Mathf.Lerp(0.15f, 1f, amount);
            float knee = SimConfig.GaitKneeBend * Mathf.Lerp(0.25f, 1f, amount);
            float pump = SimConfig.ArmPumpSwing * Mathf.Lerp(0.4f, 1f, amount);
            float elbow = SimConfig.ArmPumpElbow * Mathf.Lerp(0.35f, 1f, amount);
            rag.SetPoseOverride(Bone.ThighL, new Vector3(-s * swing - liftL * lift, 0f, 0f));
            rag.SetPoseOverride(Bone.CalfL, new Vector3(liftL * knee, 0f, 0f));
            rag.SetPoseOverride(Bone.ThighR, new Vector3(s * swing - liftR * lift, 0f, 0f));
            rag.SetPoseOverride(Bone.CalfR, new Vector3(liftR * knee, 0f, 0f));
            rag.SetPoseOverride(Bone.UpperArmR, new Vector3(s * pump, 0f, 0f));
            rag.SetPoseOverride(Bone.ForearmR, new Vector3(-elbow, 0f, 0f));
            rag.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * pump, 0f, 0f));
            rag.SetPoseOverride(Bone.ForearmL, new Vector3(-elbow, 0f, 0f));
        }

        /// <summary>
        /// The gait amount a speed reads as: 0.29 for the 1.6 m/s walk-back, 0.39 for the AI
        /// walk-in, 1 at the run-up speed. The first pass added 0.25 to everything, which put a
        /// walk halfway to a sprint's lift and knee.
        /// </summary>
        public static float GaitAmount(float speed) => Mathf.Clamp01(speed / SimConfig.SetPieceRunupSpeed);

        /// <summary>
        /// Steer a live body toward a flat target under its own locomotion: velocity along the
        /// flat direction, the facing turned toward it at `turnRate` deg/s (a body turning on the
        /// spot before it sets off reads as a person, a snapped facing as a puppet), slowing over
        /// the last stretch so it does not overshoot the mark. Returns the remaining flat distance.
        /// </summary>
        public static float Steer(ActiveRagdoll rag, Vector3 target, float speed, float turnRate, float dt)
        {
            if (rag == null || rag.Pelvis == null) return 0f;
            Vector3 me = rag.Pelvis.position;
            Vector3 to = new Vector3(target.x - me.x, 0f, target.z - me.z);
            float dist = to.magnitude;
            if (dist < 0.02f) { rag.MoveInput = Vector3.zero; return dist; }
            Vector3 dir = to / dist;
            var want = Quaternion.LookRotation(dir, Vector3.up);
            rag.UprightLock = true;
            rag.BalanceEnabled = true;
            rag.LocomotionEnabled = true;
            rag.FacingRotation = Quaternion.RotateTowards(rag.FacingRotation, want, turnRate * dt);
            // Walk only where the body is looking: a full stride at 90 deg off reads as a slide.
            float facingDot = Mathf.Clamp01(Vector3.Dot(rag.FacingRotation * Vector3.forward, dir));
            float ease = Mathf.Clamp(dist / 0.7f, 0.3f, 1f);
            rag.MoveInput = dir * speed * ease * Mathf.Lerp(0.15f, 1f, facingDot);
            return dist;
        }

        /// <summary>End a walk: no steering, no stale stride, standing square to `facing`.</summary>
        public static void Stop(ActiveRagdoll rag, Quaternion facing)
        {
            if (rag == null) return;
            rag.MoveInput = Vector3.zero;
            rag.FacingRotation = facing;
            rag.ClearPoseOverrides();
            rag.SetPose(RagdollPose.Stand, 5f);
        }

        // ---- table helpers (RagdollPose's) ----------------------------------------------------
        static Vector3[] New() => new Vector3[(int)Bone.Count];

        static Vector3[] Set(Vector3[] arr, (Bone bone, Vector3 euler)[] entries)
        {
            foreach (var e in entries) arr[(int)e.bone] = e.euler;
            return arr;
        }
    }
}
