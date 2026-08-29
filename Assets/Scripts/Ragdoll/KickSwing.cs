using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The cosmetic kick animation, shared by every AI striker in the game: the auto crosser
    /// (striker / freeplay / time trial), the set-piece taker (free kick, penalty, accuracy and the
    /// networked set-piece match) and the shooter in the main-menu backdrop reel.
    ///
    /// THIRD VERSION, and the verdict on the second was that "the body stays largely static and just
    /// leans to one side and then recovers". That was an accurate description of what the code did, for
    /// four reasons, all fixed here.
    ///
    ///   1. ONE SHARED CLOCK. The thigh and the knee were lerped over the same interval with the same
    ///      curve, which is a swinging plank, not a kick. A kick is a DOUBLE PENDULUM: the hip fires
    ///      first while the knee stays folded, decelerates, and hands its momentum to the shank, which
    ///      whips through late. That is now two overlapping sub-clocks with opposite easings.
    ///   2. NOTHING TURNED. Every kicking-leg override was Vector3(x, 0, 0), so the leg was locked in
    ///      the sagittal plane and physically could not cross the body, and the only thing that moved
    ///      laterally was a torso lean - which is exactly why a lean was all anyone could see. The body
    ///      now turns THROUGH the delivery line and the leg finishes across the midline.
    ///   3. THE TRUNK STOPPED AT CONTACT. Torso yaw unwound to zero on contact and then held zero for
    ///      both later phases, so the shoulders were still at the most violent instant of the action.
    ///      It now passes through square AT contact and keeps rotating past it.
    ///   4. THE TOE POINTED THE WRONG WAY. See SimConfig.KickToePoint: the sign was inverted, so the
    ///      ankle was dorsiflexed 26 degrees - foot pulled back, heel leading - for the whole strike.
    ///
    /// DRIVE LAG IS PART OF THE DESIGN, not an afterthought. The joint drives are a first-order tracker
    /// with tau = JointDamper/JointSpring = 23 ms, so a moving target is reached late by tau x rate: the
    /// pose that lands on the contact frame is the one commanded 23 ms earlier. Both sub-clocks therefore
    /// peak EARLY by that much, converted from seconds using the caller's own swing duration.
    ///
    /// WHAT THE RIG WILL NOT DO. The pelvis cannot be posed - ActiveRagdoll skips it when building joints
    /// and reads every drive target as child-local, so SetPoseOverride(Bone.Pelvis, ...) is a no-op twice
    /// over. Hip rotation is substituted by body yaw (see YawOffset), with the plant foot counter-rotating
    /// so its toes stay on the delivery line while the body turns through it. And UprightLock freezes
    /// pelvis pitch and roll outright while grounded, so the grounded lean is honestly chest-only; real
    /// pelvic tilt happens only in the airborne follow-through.
    ///
    /// Purely cosmetic. The ball is launched by code at contact in every one of these modes, so the pose
    /// never decides where it goes and a mistimed frame cannot cost a goal.
    /// </summary>
    public static class KickSwing
    {
        /// <summary>
        /// Body yaw offset, in degrees, that the CALLER adds to its facing. This is the pelvis
        /// substitute: the taker addresses the ball off the delivery line and turns through it as the hip
        /// fires, which is what a hip rotation looks like from outside while both feet are committed.
        /// Separate from Pose because FacingRotation belongs to the driver, not to a pose table.
        /// </summary>
        public static float YawOffset(float t, bool leftFooted, float swingSeconds = 0.45f)
        {
            if (t <= 0f) return 0f;
            float side = leftFooted ? -1f : 1f;
            float h = HipClock(t, swingSeconds);
            // Address angle, unwinding to a small residual as the body turns through the line.
            return (SimConfig.KickAddressAngle - SimConfig.KickYawThrough * h) * side;
        }

        /// <summary>Pose the whole body for a kick at clock position <paramref name="t"/> (contact at 1).
        /// Call after ClearPoseOverrides, every frame the swing is live. <paramref name="swingSeconds"/> is
        /// the caller's own 0..1 duration, used to convert the drive lag into clock units.</summary>
        public static void Pose(ActiveRagdoll rag, float t, bool leftFooted, float swingSeconds = 0.45f)
        {
            if (rag == null || t <= 0f) return;

            Bone thigh      = leftFooted ? Bone.ThighL    : Bone.ThighR;
            Bone calf       = leftFooted ? Bone.CalfL     : Bone.CalfR;
            Bone foot       = leftFooted ? Bone.FootL     : Bone.FootR;
            Bone plantThigh = leftFooted ? Bone.ThighR    : Bone.ThighL;
            Bone plantCalf  = leftFooted ? Bone.CalfR     : Bone.CalfL;
            Bone plantFoot  = leftFooted ? Bone.FootR     : Bone.FootL;
            Bone leadArm    = leftFooted ? Bone.UpperArmR : Bone.UpperArmL;
            Bone trailArm   = leftFooted ? Bone.UpperArmL : Bone.UpperArmR;
            Bone leadFore   = leftFooted ? Bone.ForearmR  : Bone.ForearmL;
            Bone trailFore  = leftFooted ? Bone.ForearmL  : Bone.ForearmR;
            // +1 when the kicking leg is the right one. Multiplies every lateral and yaw term.
            float side = leftFooted ? -1f : 1f;

            float wind = SimConfig.KickWindupEnd;
            float thru = SimConfig.KickThroughEnd;
            float end  = SimConfig.KickRecoverEnd;

            // The two overlapping sub-clocks. h eases OUT (fast at the start, decelerating into contact
            // but never dead-stopped); k eases IN (still at the start, peak rate at the ball).
            float h = HipClock(t, swingSeconds);
            float k = KneeClock(t, swingSeconds);

            float thighX, thighZ, kneeX, kneeZ, footX, footY;
            float lean, twist, tilt;
            float plantX, plantZ, plantKnee, plantToe, plantRoll;
            float armLead, armTrail, spread;

            if (t < wind)
            {
                // ADDRESS + COCK. Weight shifts onto the plant foot, which lands WIDE of the ball and
                // slightly ahead of the hips with the toes turned back onto the delivery line. The
                // kicking leg cocks back AND out, which is what makes the swing plane diagonal rather
                // than sagittal. The trunk arches back and opens away from the line to load the hip.
                float u = Smooth(t / wind);
                thighX    =  SimConfig.KickCockThigh * u;
                thighZ    =  SimConfig.KickSwingOut * u * side;
                kneeX     =  SimConfig.KickCockKnee * u;
                kneeZ     = 0f;
                footX     = 0f;
                footY     = 0f;
                lean      = -SimConfig.KickTorsoExtend * u;      // arches BACK, not forward
                twist     =  SimConfig.KickTorsoTwist * u;
                tilt      =  SimConfig.KickTorsoTilt * 0.6f * u;
                plantX    = -SimConfig.KickPlantStep * u;
                plantZ    = -SimConfig.KickPlantSplay * u * side;
                plantKnee =  SimConfig.KickPlantFlex * u;
                plantRoll =  SimConfig.KickPlantSplay * u * side;   // levels the sole against the abduction
                armLead   =  SimConfig.KickArmSwing * 0.45f * u;
                armTrail  = -SimConfig.KickArmSwing * 0.30f * u;
                spread    = 0f;
            }
            else if (t < 1f)
            {
                // STRIKE. The hip drives through on h while the knee is still folded, then the shin whips
                // out on k. The leg adducts from out to slightly IN, so at contact the boot's velocity is
                // straight down the delivery line even though the body is still 8 degrees off it - that
                // cancellation is the whole point of an angled address.
                thighX    = Mathf.Lerp( SimConfig.KickCockThigh, -SimConfig.KickStrikeThigh, h);
                thighZ    = Mathf.Lerp( SimConfig.KickSwingOut * side, -SimConfig.KickContactCross * side, h);
                kneeX     = Mathf.Lerp( SimConfig.KickCockKnee, SimConfig.KickStrikeKnee, k);
                kneeZ     = 0f;
                footX     =  SimConfig.KickToePoint * k;            // POSITIVE = plantarflexed instep
                footY     = 0f;
                lean      = Mathf.Lerp(-SimConfig.KickTorsoExtend, SimConfig.KickTorsoLean, h);
                // Passes through square AT contact on its OWN clock, not the hip's, so the shoulders are
                // still moving on the contact frame instead of having stopped early with the thigh.
                twist     = Mathf.Lerp(SimConfig.KickTorsoTwist, 0f, Smooth(Mathf.InverseLerp(wind, 1f, t)));
                tilt      = Mathf.Lerp(SimConfig.KickTorsoTilt * 0.6f, SimConfig.KickTorsoTilt, h);
                plantX    = -SimConfig.KickPlantStep;
                plantZ    = -SimConfig.KickPlantSplay * side;
                // The support knee EXTENDS through the strike, bracing. A real plant leg straightens from
                // mid-swing into contact rather than staying bent.
                plantKnee = Mathf.Lerp(SimConfig.KickPlantFlex, SimConfig.KickPlantBrace, h);
                plantRoll =  SimConfig.KickPlantSplay * side;
                armLead   = Mathf.Lerp( SimConfig.KickArmSwing * 0.45f, -SimConfig.KickArmSwing, h);
                armTrail  = Mathf.Lerp(-SimConfig.KickArmSwing * 0.30f,  SimConfig.KickArmSwing * 0.55f, h);
                spread    = 0f;
            }
            else if (t < thru)
            {
                // FOLLOW-THROUGH. Airborne, and the leg finishes high and ACROSS the midline while the
                // trunk keeps rotating past the line and arches back. The knee carries on extending for
                // the first part of this rather than stopping on the contact frame.
                float f = Smooth((t - 1f) / (thru - 1f));
                float kf = Mathf.Clamp01((t - 1f) / ((thru - 1f) * 0.35f));   // knee finishes early in f
                thighX    = Mathf.Lerp(-SimConfig.KickStrikeThigh, -SimConfig.KickFollowThigh, f);
                thighZ    = Mathf.Lerp(-SimConfig.KickContactCross * side, -SimConfig.KickFollowCross * side, f);
                kneeX     = Mathf.Lerp( SimConfig.KickStrikeKnee, SimConfig.KickPostKnee, kf);
                kneeZ     = -SimConfig.KickCalfCross * f * side;
                footX     =  SimConfig.KickToePoint;
                footY     = 0f;
                lean      = Mathf.Lerp(SimConfig.KickTorsoLean, -SimConfig.KickTorsoArch, f);
                twist     = -SimConfig.KickTorsoThrough * f;       // PAST square, still turning
                tilt      = Mathf.Lerp(SimConfig.KickTorsoTilt, SimConfig.KickTorsoTilt * 0.3f, f);
                plantX    = Mathf.Lerp(-SimConfig.KickPlantStep, SimConfig.KickTrailThigh, f);   // trails behind
                plantZ    = -SimConfig.KickPlantSplay * (1f - f) * side;
                plantKnee = Mathf.Lerp(SimConfig.KickPlantBrace, SimConfig.KickPlantFlex, f);
                plantRoll =  SimConfig.KickPlantSplay * (1f - f) * side;
                armLead   = Mathf.Lerp(-SimConfig.KickArmSwing, -SimConfig.KickArmSwing * 0.4f, f);
                armTrail  = Mathf.Lerp( SimConfig.KickArmSwing * 0.55f, SimConfig.KickArmSwing * 0.3f, f);
                spread    = SimConfig.KickArmSpread * f;
            }
            else
            {
                // REBALANCE. He lands on the KICKING foot, both knees give to absorb it, the trunk squares
                // up and everything decays to a neutral stand.
                float r = Smooth(Mathf.Clamp01((t - thru) / (end - thru)));
                float absorb = Mathf.Sin(r * Mathf.PI);
                thighX    = Mathf.Lerp(-SimConfig.KickFollowThigh, 0f, r);
                thighZ    = -SimConfig.KickFollowCross * (1f - r) * side;
                kneeX     = Mathf.Lerp(SimConfig.KickPostKnee, 0f, r) + SimConfig.KickPlantFlex * 0.9f * absorb;
                kneeZ     = -SimConfig.KickCalfCross * (1f - r) * side;
                footX     =  SimConfig.KickToePoint * (1f - r);
                footY     = 0f;
                lean      = -SimConfig.KickTorsoArch * (1f - r);
                twist     = -SimConfig.KickTorsoThrough * (1f - r);
                tilt      =  SimConfig.KickTorsoTilt * 0.3f * (1f - r);
                plantX    =  SimConfig.KickTrailThigh * (1f - r);
                plantZ    = 0f;
                plantKnee =  SimConfig.KickPlantFlex * (1f - r) + SimConfig.KickPlantFlex * 0.6f * absorb;
                plantRoll = 0f;
                armLead   = -SimConfig.KickArmSwing * 0.4f * (1f - r);
                armTrail  =  SimConfig.KickArmSwing * 0.3f * (1f - r);
                spread    =  SimConfig.KickArmSpread * (1f - r);
            }

            // Plant toe yaw counter-rotates the BODY yaw on the same clock, so the toes stay pointed down
            // the delivery line while the body turns through it. Authored as a constant offset it would
            // rotate rigidly with the parent and sweep the same angle the body does - and the foot
            // collider is frictionless, so it would visibly pivot and skid.
            plantToe = -YawOffset(t, leftFooted, swingSeconds);

            rag.SetPoseOverride(thigh,      new Vector3(thighX, 0f, thighZ));
            rag.SetPoseOverride(calf,       new Vector3(kneeX,  0f, kneeZ));
            rag.SetPoseOverride(foot,       new Vector3(footX,  footY, 0f));
            rag.SetPoseOverride(plantThigh, new Vector3(plantX, 0f, plantZ));
            rag.SetPoseOverride(plantCalf,  new Vector3(plantKnee, 0f, 0f));
            rag.SetPoseOverride(plantFoot,  new Vector3(0f, plantToe, plantRoll));
            rag.SetPoseOverride(Bone.Torso, new Vector3(lean, twist * side, -tilt * side));
            rag.SetPoseOverride(Bone.Head,  new Vector3(lean * 0.3f, 0f, tilt * side * 0.4f));
            rag.SetPoseOverride(leadArm,   new Vector3(armLead,  0f, spread * -side));
            rag.SetPoseOverride(trailArm,  new Vector3(armTrail, 0f, spread *  side));
            rag.SetPoseOverride(leadFore,  new Vector3(-SimConfig.KickElbowBend, 0f, 0f));
            rag.SetPoseOverride(trailFore, new Vector3(-SimConfig.KickElbowBend * 0.6f, 0f, 0f));

            rag.SetPose(RagdollPose.Stand, 5f);
        }

        // The hip's sub-clock. Ease OUT so the peak angular rate is at the START and the thigh is
        // decelerating into contact - but with a nonzero derivative at the end, so it is still carrying
        // rate rather than stopping dead and freezing every track slaved to it.
        static float HipClock(float t, float swingSeconds)
        {
            float u = Span(t, SimConfig.KickHipStart, SimConfig.KickHipEnd, swingSeconds);
            return 1f - Mathf.Pow(1f - u, 1.6f);
        }

        // The knee's sub-clock. Ease IN, so the shin is still while the hip drives and whips at the ball.
        static float KneeClock(float t, float swingSeconds)
        {
            float u = Span(t, SimConfig.KickKneeStart, SimConfig.KickKneeEnd, swingSeconds);
            return u * u;
        }

        // Normalise t across a sub-window, with the END pulled forward by the joint drives' tracking lag
        // so the ACHIEVED pose - not the commanded one - lands on the contact frame.
        static float Span(float t, float from, float to, float swingSeconds)
        {
            float lead = SimConfig.KickDriveLagSeconds / Mathf.Max(0.05f, swingSeconds);
            float end = Mathf.Max(from + 0.02f, to - lead);
            return Mathf.Clamp01((t - from) / (end - from));
        }

        /// <summary>
        /// Pop the body off its plant leg at contact, so the strike carries him off the ground and down
        /// onto the kicking foot. Call ONCE, on the frame the ball is struck, with the direction he is
        /// kicking and which foot he kicked with.
        ///
        /// UprightLock has to come off first: it pins the pelvis outright, which would swallow the hop
        /// entirely. Clearing it is also the ONLY window in which the pelvis gets any pitch or roll at
        /// all, because UprightLock freezes both with rigidbody constraints while grounded.
        /// </summary>
        public static void Hop(ActiveRagdoll rag, Vector3 forward, bool leftFooted)
        {
            if (rag == null) return;
            rag.UprightLock = false;
            Vector3 f = forward; f.y = 0f;
            Vector3 v = Vector3.up * SimConfig.KickHopVel;
            if (f.sqrMagnitude > 1e-4f)
            {
                f.Normalize();
                Vector3 lateral = Vector3.Cross(Vector3.up, f) * (leftFooted ? 1f : -1f);
                v += f * SimConfig.KickHopDrift + lateral * SimConfig.KickHopSide;
            }
            rag.AddVelocityToAll(v);
        }

        /// <summary>True once the clock has run past the rebalance, i.e. the body is standing again
        /// and the caller should stop posing.</summary>
        public static bool Finished(float t) => t >= SimConfig.KickRecoverEnd;

        /// <summary>Which foot the LOCAL player kicks with. Callers animating a remote player pass
        /// their own value instead - footedness is not on the wire yet.</summary>
        public static bool LocalFoot => PlayerProfile.LeftFooted;

        // Ease in and out: used where a motion starts and stops.
        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }
    }
}
