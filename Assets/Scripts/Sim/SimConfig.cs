using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Central tuning + shared constants for the whole prototype.
    /// Everything that another script might want to reference (layer names,
    /// physics numbers, arena dimensions) lives here so there is a single
    /// source of truth. No scene wiring needed: the bootstrap reads this.
    /// </summary>
    public static class SimConfig
    {
        // No custom layers/tags: layers can't be created reliably at runtime, so
        // the sim uses Physics.IgnoreCollision for ragdoll self-collision and
        // component lookups (BallController, Goal) instead of tags/layermasks.

        // ---- Arena dimensions (metres) ----
        // The playable box is a training-ground slice, not a full pitch.
        public const float FieldLength = 34f;   // along Z, toward goal
        public const float FieldWidth  = 24f;   // along X
        // Goal size is set from the pre-match screen, so these are mutable (not const).
        public static float GoalWidth  = GoalWidthBase;   // regulation-ish
        public static float GoalHeight = GoalHeightBase;
        public static float GoalDepth  = 3.0f;    // deeper goal box

        // ---- Net (position-based-dynamics cloth) ----
        public const int NetCols = 24;             // grid resolution across the width
        public const int NetRows = 12;             // grid resolution down the height
        public const int NetConstraintIters = 2;   // PBD distance-constraint passes/frame (fewer = looser, stretchier)
        public const float NetReturn = 0.45f;      // slow drift back to rest -> pocket lingers (looser feel)
        public const float NetDamping = 0.99f;     // velocity retained per step (0..1); higher = flowier/looser
        public const float NetMaxStretch = 4.8f;   // max node displacement from rest (m); deeper billow
        // Ball push field: nodes within this of the ball centre get shoved to its
        // surface. Must exceed the gap the backstop leaves (~one ball radius) or the
        // net never billows. Bigger = wider, deeper pocket.
        public const float NetBallReach = 0.85f;

        // ---- Hair (soft dynamic strands, same Verlet model as the net) ----
        // Root-pinned strands that fall, swing with head motion, collide off the head, and are
        // pulled toward their styled rest by per-style stiffness. Runs on every body with hair.
        public const float HairGravity = -6.5f;        // world-down accel on free nodes (softer than real g so it wafts)
        public const float HairDamping = 0.92f;        // velocity retained per step (0..1); lower = settles faster, less flyaway
        public const int   HairConstraintIters = 3;    // length-constraint passes/frame (more = stiffer, holds length better)
        public const float HairStiffnessK = 30f;       // gain on the per-style pull toward the styled rest shape (scaled by def.stiffness*dt)
        public const float HairHeadPad = 0.01f;        // keep nodes this far off the head sphere so strands don't sink into the skull
        // Visual thickness: each SIMULATED strand is drawn as this many parallel sub-lines fanned
        // around the strand axis (a bundle), so a strand reads as a rope/lock with width instead of
        // a 1px wire (MeshTopology.Lines ignores line width). Only the sim nodes are matrix-
        // transformed each tick; the bundle copies are cheap fixed offsets, so thickness barely
        // adds cost. 1 = single wire (no bundle). def.thickness sets the bundle radius (metres).
        public const int   HairStrandLines = 3;

        public const float PenaltyBoxDepth = 16.5f;
        public const float PenaltyBoxWidth = 20f; // slightly narrower than field

        // Goal sits at +Z end. Crosser starts near a wing at -Z / +X corner.
        public static readonly Vector3 GoalCenter    = new Vector3(0f, 0f, FieldLength * 0.5f);
        // The goal the human striker is AIMING AT. Aim assist, dribble shots, and the auto
        // ball-cam steer toward this. Defaults to the training goal; scrimmage repoints it
        // to the actual attacked goal (at the pitch half-length) so shots aren't aimed short.
        public static Vector3 AttackGoalCenter = new Vector3(0f, 0f, FieldLength * 0.5f);
        public static readonly Vector3 CrosserStart   = new Vector3(9.5f, 0f, FieldLength * 0.5f - 5.5f);
        public static readonly Vector3 StrikerStart   = new Vector3(-1.5f, 0f, FieldLength * 0.5f - 8.5f);
        public static readonly Vector3 KeeperStart    = new Vector3(0f, 0f, FieldLength * 0.5f - 0.6f);
        // A penalty is taken with the keeper ON his goal line, not 0.6 m off it the way he stands in
        // open play - standing off the line gives him a head start at closing the angle that the laws
        // do not allow, and it looks wrong from behind the ball. Held a hair in front of GoalCenter.z
        // so he is in the goal MOUTH rather than buried in the netting behind it.
        public static readonly Vector3 KeeperPenaltyStart = new Vector3(0f, 0f, FieldLength * 0.5f - 0.08f);
        public static readonly Vector3 ReticleStart   = new Vector3(0f, 0.02f, FieldLength * 0.5f - 8.5f);

        // ---- Goalkeeper (player-controlled keeper mode) ----
        // Keeper stands on the line facing OUT toward the pitch (-Z).
        public static readonly Vector3 KeeperFaceDir = new Vector3(0f, 0f, -1f);
        public static float KeeperStrafeSpeed = KeeperStrafeSpeedBase;  // A/D strafe + W/S move speed (pre-match slider)
        public const float KeeperStrafeXLimit = 4.2f;  // how far off centre he can shuffle

        // Keeper look cone: the camera pans within this yaw and the body turns to match,
        // so he faces where the mouse points within a limited cone.
        public const float KeeperLookYawLimit = 40f;   // max deg left/right of straight-forward

        // Keeper run gait: alternating steps while moving on his line (body glides).
        public const float KeeperShuffleRate = 13f;    // step cadence
        public const float KeeperShuffleLift = 55f;    // thigh lift per step (pronounced foot pickup)
        public const float KeeperShuffleKnee = 120f;   // knee fold on the lifted leg

        // LMB/RMB reflex save: one-time sideways lunge, arm+leg out. He STAYS DOWN in
        // the save pose for as long as the button(s) are held, then gets up on release.
        // Sideways velocity of the keeper's LMB/RMB reflex save (KeeperController.BeginSave). Cut 20%
        // from 7: the lunge carried him far enough sideways that a save near one post left him past the
        // other, and the reach was covering ground the dive is supposed to be for.
        public const float KeeperSaveLunge = 5.6f;
        public const float KeeperSaveReleaseTime = 0.12f; // brief settle after release before standing

        // Upward dive (A/D + Space): reach/height scale with prior speed. More hang time
        // so there is a real apex where he is laid out flat.
        public const float KeeperDiveHorizBase = 3.58f;  // horizontal reach at standstill (~80% of full-speed reach)
        public const float KeeperDiveHorizPerV = 0.163f; // extra horizontal per m/s of prior speed
        public const float KeeperDiveUpBase = 3.98f;     // upward pop at standstill (~80% of full-speed height)
        public const float KeeperDiveUpPerV = 0.181f;    // extra height per m/s of prior speed

        // Double-tap A/D: explosive low sideways dive; legs leave the ground as he lays out.
        public const float KeeperDashDive = 7.7f;        // horizontal speed of the low dash dive
        public const float KeeperDashUp = 2.2f;          // lift so his legs come off the ground
        public const float KeeperDoubleTapWindow = 0.3f; // seconds between taps to count as a double-tap

        // Dive lay-out: the pelvis is actively driven to a rolled (horizontal) target and
        // HELD there, so he reliably reaches that tilt by the apex regardless of airtime.
        // 90 = fully parallel to the ground.
        public const float KeeperDiveLayoutLow = 84f;    // low dash dive: nearly flat
        public const float KeeperDiveLayoutHigh = 90f;   // high dive: fully parallel at the apex
        public const float KeeperDiveRoll = 90f;         // strong initial roll kick -> lays out near-instantly
        public const float KeeperDiveLeadKnee = 130f;    // leading leg folds up hard
        public const float KeeperDiveBackKnee = 95f;     // back leg also bends a good amount
        public const float KeeperDiveMinAir = 0.25f;     // min airborne time before we check for landing
        public const float KeeperDiveSettle = 0.25f;     // time on the ground after landing before getting up
        public const float KeeperDiveMaxTime = 2.5f;     // hard safety cap so a dive can never get stuck
        // EPIC SAVE ball-speed gate (m/s at the moment of contact). A save on a shot struck at least
        // this hard is EPIC; so is any save made in a high dive (see KeeperController.IsHighDive).
        // Those are the only two epic criteria. Set above a firm shot but below a rocket.
        public const float KeeperEpicSaveSpeed = 22f;

        // ---- Human keeper: getting up, catching, distributing ----
        // Coming down from a dive costs a moment. The AI keeper's version of that beat is emergent
        // (it walks back to its guard spot while balance re-engages) but the human keeper snapped
        // straight from prone to Ready, which read as weightless. This is the same beat, authored:
        // a short push-up-and-drift he can cancel out of by pressing a save.
        public const float KeeperStumbleTime = 0.55f;  // scaled by PlayerProfile.RecoveryTimeMul
        public const float KeeperStumbleStep = 2.2f;   // m/s of leftover drift the instant he is up
        // Catching. The AI gates gathering on its difficulty slider; a human has no such slider, so
        // his hands come off Control instead - and the floor sits well above zero, because a player
        // who can never hold a ball is a bug, not a stat.
        public const float KeeperHumanHandsRaw     = 0.25f; // hands at zero Control
        public const float KeeperHumanHandsSkilled = 1.00f; // hands at full Control
        public const float KeeperHumanHoldMax      = 4.0f;  // held this long -> he punts it himself
        public static float KeeperJumpVel = KeeperJumpVelBase;  // straight-up jump (Space); pre-match slider
        public const float KeeperJumpVelBase = 6.5f;     // 1.0x reference for jump/dive-height scaling
        // Keeper camera slight mouse look (clamped, stays a behind-view). Yaw is carried
        // by the keeper's body facing now, so the camera only pitches.
        public const float KeeperCamLookPitch = 12f;      // max deg up/down
        public const float KeeperCamLookSpeed = 0.06f;    // deg per mouse-delta unit

        // ---- Goalkeeping (AI keeper positioning, sweeping, handling, distribution) ----
        // POSITION ON THE ANGLE. He stands on the ball-to-goal-centre line, off his line in
        // proportion to how far out the ball is. Covering the near post from wide covers the far
        // post too; a keeper welded to the middle of his line is beaten by geometry every time.
        public const float KeeperAngleFrac   = 0.16f; // metres off the line per metre the ball is out
        public const float KeeperLineOffset  = 0.55f; // never flat on the goal line
        public const float KeeperMaxOffLine  = 5.0f;  // furthest he'll advance to narrow the angle
        public const float KeeperDrillOffLine = 1.4f; // furthest in the single-goal drills (see Goalkeeper.Sweeper)
        public const float KeeperWideAllow   = 1.1f;  // how far outside the posts he may track a wide ball
        public const float KeeperGuardBand   = 1.0f;  // proportional band: full speed once this far off his spot
        public const float KeeperRunGaitSpeed = 3.2f; // above this he runs properly instead of shuffling
        // Sweeping: a loose SLOW ball near his goal is his. A ball in someone's feet is not -
        // that needs a challenge, not a keeper vacuuming it off a dribbler.
        public const float KeeperRushZone     = 12f;  // loose ball this near the goal -> come and get it
        public const float KeeperRushMaxSpeed = 9f;   // ...but only if it isn't flying (that's a dive)
        public const float KeeperRushSpeedMul = 1.6f; // he moves off his line quicker than he tracks on it
        // Handling. A CATCH IS THE EXCEPTION. Gathering demands a nearly dead ball, at his hands,
        // in FRONT of him, and not already running away from him. Everything else is PARRIED with
        // a real impulse (KeeperHands.TryParry) instead of bobbling off his capsules.
        public const float KeeperClaimReach      = 0.62f; // gather radius from the chest - a glove length, not a bubble
        public const float KeeperClaimMaxSpeed   = 6.6f;  // faster than this cannot be caught (ability scales it 0.55x - 1.6x)
        // ...except at his chest. That is the one height he gets his body behind rather than
        // just a glove, so it earns a higher ceiling; the same pace at his boots or over his
        // head stays a parry. Fades between the two so there is no band edge you can feel.
        public const float KeeperClaimChestSpeed = 8f;    // ceiling at chest height
        public const float KeeperClaimChestBand  = 0.20f; // within this of chest height: full chest ceiling
        public const float KeeperClaimChestFade  = 0.40f; // and back to KeeperClaimMaxSpeed this much further out
        public const float KeeperClaimZone       = 9f;    // only gathers this near his own goal
        public const float KeeperClaimCooldown   = 2.2f;  // after releasing, before he can gather again
        public const float KeeperClaimMinAbility = 0.30f; // a hopeless keeper never holds anything
        public const float KeeperClaimFrontDot   = 0.35f; // ball must be this far in FRONT of him (ability widens the cone)
        public const float KeeperClaimMaxRecede  = 1.2f;  // ball leaving his chest faster than this is gone, not gathered
        public const float KeeperHoldForward     = 0.42f; // held ball sits this far in front of the chest
        public const float KeeperHoldTime        = 1.4f;  // seconds held before he plays it (ability scales it)
        public const float KeeperHoldBreak       = 2.5f;  // ball teleported this far out from under the hold -> drop it
        // Parry - what happens INSTEAD of a catch. Fires only on a collision PhysX already logged
        // against his body, so it is never telekinesis and SaveWatch still credits the save.
        public const float KeeperParryTouchWindow = 0.15f; // a logged contact this recent counts as his touch
        public const float KeeperParryReach       = 2.0f;  // sanity cap from the chest - rejects a stale log entry only
        public const float KeeperParryCooldown    = 0.35f; // one touch per passage; also blocks gathering what he just pushed
        public const float KeeperParryKeep        = 0.40f; // fraction of impact speed kept, so a rocket flies further off
        public const float KeeperParryPush        = 4.0f;  // floor push, so a tame ball is still cleared off his line
        public const float KeeperParrySide        = 0.85f; // lateral weight - parried WIDE, not back to the shooter
        public const float KeeperParryUp          = 0.35f; // slight lift so it clears the turf
        // Distribution.
        public const float KeeperDistributeRange      = 26f;   // punt distance when there is nobody to find
        public const float KeeperDistributeScatterDeg = 14f;   // aim error at zero ability
        public const float KeeperDistributeWobble     = 0.10f; // weight error at zero ability

        // ---- Audio mix ----
        // Per-sound trims sitting on top of the user's SFX slider. The whistle is the one cue that
        // fires at full level right beside the camera, so it needed pulling down.
        public const float WhistleVolume = 0.60f;
        // The woodwork clang fires in the same instant as the touch that caused it, so at full
        // level it swamped the thud it is meant to sit on top of.
        public const float PostHitVolume = 0.75f;

        // ---- Physics ----
        public const float Gravity = -19.6f;      // 2x real gravity: snappier, arcade feel
        public const float BallMass = 0.43f;
        public const float BallRadius = 0.22f;
        public const float BallDrag = 0.02f;        // lower -> keeps pace, rolls further
        public const float BallAngularDrag = 0.02f;
        public const float BallBounciness = 0.55f;
        // ---- Rolling resistance (BallController.ApplyRollingResistance) ----
        // PhysX has no rolling friction for a sphere: once the ball rolls without slipping the
        // contact patch has zero relative velocity, so the turf material's friction cannot slow it.
        // With BallDrag/BallAngularDrag at 0.02 (about 2%/s) a loose ball trundled at a constant
        // speed forever and a keeper (StrikerMoveSpeed 3.8 base) could never close on it. These add
        // the missing term, gated so nothing in flight is touched.
        public const float BallRollDecel  = 3.2f;   // m/s^2 opposing a grounded roll (mu_r ~0.16 at this gravity)
        public const float BallRollSpeed  = 11f;    // flat speed at/below which the ball counts as LOOSE and rolling.
                                                    // Far under StrikeHorizMax 26 and DribbleShotSpeed 17, so no
                                                    // struck shot or lofted set piece is ever damped at launch.
        public const float BallRollMaxVy  = 1.2f;   // |vy| above this is a bounce, not a roll. Above DribbleTouchHop
                                                    // 0.85 so a dribble touch still counts as rolling, consistently.
        public const float BallRollStop   = 0.35f;  // below this the roll is killed dead instead of creeping

        // ---- Cross tuning ----
        // Loft flight times are kept SHORT so a lofted cross is a low, crossable arc (~3m apex to
        // a target ~8m out), not a moon ball. A longer flight time to the same target = higher arc,
        // so these directly cap the loft height for both the AI serve and the human chip.
        public const float CrossTimeLoft = 1.05f;  // loftier serve: floaty (chipped) but low-arc
        public const float CrossTimeDrive = 0.95f; // driven serve: faster, flatter (low)
        public const float MaxCurlAccel = 8f;      // lateral accel while airborne
        // Human crosser charge: hold time (0..CrossMaxCharge s) scales flight time between a
        // hard/flat serve and the type's nominal float, so a longer hold floats it more.
        public const float CrossMaxCharge = 0.6f;  // seconds of hold for max floatiness
        public const float CrossTapMaxHold = 0.18f; // held below this = a tap (driven); above = a chip
        public const float CrossChargeFlatMul = 0.8f;  // bare tap: 0.8x the type's flight time (flatter/faster)
        public const float CrossChargeFloatMul = 1.2f;  // full hold: 1.2x (a bit floatier, still a low arc)

        // ---- Human crosser AIM (CrosserControl): look direction + charge, no map click ----
        // Aim is RELATIVE to wherever the crosser is actually standing (ServeFromFeet: he walks
        // freely), not an absolute Z-plane - a plane fixed to the goal line broke the moment his own
        // position was already closer to goal than the "near" plane, which happens easily since
        // CrosserStart is only 5.5 m back and the near plane was 6 m: solving for where a FORWARD ray
        // crosses a plane BEHIND you returns a negative flight time. Verified with Temp/crossaim_check
        // .py before and Temp/crossaim_check2.py after - the plane model produced 55 bad (negative-t)
        // solves out of 120 sampled angles/charges; this one produces 0.
        //
        // Charge01 sets how far the look-ray reaches from his own feet; the result is then clamped
        // into a legal delivery box (X around the goal, Z in front of the goal line) as a safety net,
        // not as the primary solve - so ANY standing position and look angle still produces something
        // sane rather than an edge case. Matches the shot mechanic's "aim = where you look, charge =
        // power" so the two deliveries feel like the same game.
        public const float CrossAimNearReach = 8f;    // metres the ray travels from his feet at a tap
        public const float CrossAimFarReach  = 20f;   // ...and at a full hold
        public const float CrossAimHalfWidth = 11f;   // X clamp, either side of goal centre
        public const float CrossAimMinDepth  = 2f;    // Z clamp: never behind this close to the goal line
        public const float CrossAimMaxDepth  = 18f;   // Z clamp: never farther out than this
        // Curl magnitude at FULL charge (see BallController.LaunchTo's curlAccel). Which foot (LMB/RMB)
        // sets the SIGN, not which wing the crosser is standing on - a deliberate simplification, not
        // a claim about real in/outswinger technique. Shaped the same way the shot's curl is (peaks at
        // mid-charge, tapers at both ends - Sin(charge*pi)), so a tap or a full hold curls least and a
        // half-charge curls most.
        public const float CrossCurlAccMax = 3.2f;
        // Human crosser: pressing R drops a fresh ball at their feet, but only if the current ball
        // has been served away (is at least this far from the feet). Avoids yanking a ready ball.
        public const float CrosserRefillDist = 1.5f;

        // ---- Crosser (ragdoll leg-swing before a perfect launch) ----
        // He plants, plays a right-leg swing, and the ball leaves at contact - but the
        // launch is still solved perfectly by code every time (the swing is cosmetic).
        public const float CrosserWindupTime = 0.45f; // time from telegraph->contact the leg swings through
        // How far a planted AI crosser may be shoved off his spot before he is put back on it. He has
        // no locomotion controller (Crosser.Init turns it off), so nothing bleeds off a velocity he
        // picks up - without this he skates away from the wing for the rest of the match.
        public const float CrosserPlantDrift = 0.6f;   // metres

        // ---- Kick swing (KickSwing: the AI crosser, the set-piece taker, the menu shooter) ----
        // One normalised clock t: 0 opens the windup, 1 is contact, and it runs on to KickRecoverEnd
        // through the follow-through and the rebalance. Drivers scale t by their own windup duration.
        // Replaces CrosserSwingThigh/CrosserSwingCalf/CrosserPlantLean, which described a three-bone
        // linear sweep that ran the thigh the WRONG WAY (see the KickSwing class comment).
        //
        // Sign convention, from RagdollPose: NEGATIVE local X throws a limb's lower end FORWARD, so a
        // cocked-back thigh is positive and a thigh driven through the ball is negative. A positive X
        // on a calf folds the knee.
        public const float KickWindupEnd   = 0.45f;  // t at which the leg is fully cocked
        public const float KickThroughEnd  = 1.45f;  // t at the peak of the follow-through
        public const float KickRecoverEnd  = 2.10f;  // t at which he is stood up square again
        // ---- sequencing: the two OVERLAPPING sub-clocks inside the strike ----
        // A kick is a double pendulum. The HIP fires first while the knee stays folded, then decelerates
        // and hands its momentum to the shank, which whips through LATE. The old table lerped thigh and
        // knee over one shared interval with one shared curve, which is a swinging plank - and it is the
        // main reason the body read as static with a lean bolted on.
        public const float KickHipStart  = 0.50f;   // hip drive opens
        public const float KickHipEnd    = 0.97f;   // ...and eases out, still carrying residual rate
        public const float KickKneeStart = 0.80f;   // shin stays folded until here
        public const float KickKneeEnd   = 1.00f;   // ...then extends into the ball
        // JOINT DRIVE LAG. The drives are a first-order tracker with tau = JointDamper / JointSpring =
        // 150 / 6500 = 0.023 s, so a moving target is always reached LATE by tau x rate. Commanded
        // angles therefore have to peak EARLY or the pose that lands on the contact frame is the one
        // commanded 23 ms before it. Expressed in seconds and converted per driver, because the same
        // normalised lead is a different amount of time for a 0.45 s crosser swing and a 0.32 s
        // set-piece swing. (DriveScale is the only lever that would shorten tau - it scales spring and
        // not damper. DriveMul scales both and leaves tau fixed.)
        public const float KickDriveLagSeconds = 0.023f;

        public const float KickCockThigh   = 38f;    // deg the kicking thigh draws BACK in the windup
        public const float KickCockKnee    = 85f;    // deg the knee folds up behind him with it
        public const float KickStrikeThigh = 48f;    // deg the thigh has driven FORWARD by contact
        public const float KickFollowThigh = 76f;    // ...and by the top of the follow-through
        // 28, not the 8 this used to command. Two independent reasons landed on the same number. Real
        // instep contact is at 25-35 deg of knee flexion with the knee still EXTENDING through the ball,
        // not locked out on it. And the drive lag above means a commanded 8 arrives at roughly 28
        // anyway - so the old table was fighting the rig to reach a pose that was wrong regardless.
        public const float KickStrikeKnee  = 28f;    // deg of knee flexion at contact
        // The knee keeps extending AFTER contact, over the first part of the follow-through, so the
        // target decays across several physics steps instead of stopping dead on one frame.
        public const float KickPostKnee    = 8f;
        // SIGN WAS INVERTED. Rotation about a foot's local +X takes its local +Z - the toe box - toward
        // -Y, i.e. DOWN, so plantarflexing an instep is POSITIVE X. RagdollPose.Sit agrees: it authors
        // both feet at -18 for "heels down, toes up". This was being applied NEGATIVE, which dorsiflexed
        // the toe 26 deg up through the entire strike - the foot was pulled back like a heel strike for
        // the whole swing. (Bicycle's "-25 pointed" comment is the one that is wrong.)
        public const float KickToePoint    = 26f;    // deg the ankle plantarflexes on the strike
        public const float KickPlantFlex   = 22f;    // deg the standing knee gives, bracing then landing
        public const float KickTorsoLean   = 10f;    // deg trunk leans in over the plant foot
        public const float KickTorsoTwist  = 14f;    // deg the trunk opens away from the ball, then squares
        // Lateral trunk tilt toward the PLANT side, peaking at contact and unwinding after. This is
        // the weight transfer: a player tips away from the ball to swing the kicking hip past the
        // standing one. A CONSTANT side lean is what the old animation looked like and why it read as
        // a hinge rather than a person.
        public const float KickTorsoTilt   = 16f;    // deg
        // The trunk EXTENDS (arches back) at the top of the cock and again on the follow-through, and
        // only flexes forward through contact. The old table had one forward lean the whole way, which
        // is the "leans to one side and recovers" the animation was reduced to.
        public const float KickTorsoExtend = 10f;    // deg of arch-back at the top of the cock
        public const float KickTorsoArch   = 8f;     // deg of arch-back at the top of the follow-through
        // The trunk rotates THROUGH the delivery line and finishes rotated past it. It used to unwind to
        // zero at contact and then hold zero for both later phases, so the shoulders stopped moving at
        // the most violent moment of the action.
        public const float KickTorsoThrough = 18f;   // deg past square, on the follow-through

        // ---- the pelvis substitute ----
        // The pelvis CANNOT be posed: ActiveRagdoll skips it when building joints, and every drive target
        // is read as child-local relative to its parent, so SetPoseOverride(Bone.Pelvis, ...) is a silent
        // no-op twice over. Hip rotation is therefore expressed as BODY YAW through FacingRotation, which
        // UprightLock slews the pelvis toward at up to 900 deg/s - a 22 deg turn over the hip window is
        // ~110 deg/s mean, nowhere near saturating. From outside, a hip rotation and a whole-body yaw are
        // indistinguishable while both feet are committed.
        //
        // NOTE HONESTLY WHAT THIS DOES NOT COVER: UprightLock freezes pelvis pitch and roll with
        // rigidbody constraints and steers yaw only, so while grounded the hips stay level and the lean
        // is chest-only. Real pelvic tilt exists only for the airborne follow-through, via
        // BodyOrientTarget. Fixing that needs the balance lock released through the strike, which also
        // drops the carry servo, and is deliberately not done here.
        public const float KickAddressAngle = 30f;   // deg the body starts off the delivery line
        public const float KickYawThrough   = 22f;   // ...and turns through it on the hip clock
        // The plant toes must stay pointed down the delivery line while the body turns through it, so the
        // plant foot's yaw counter-rotates on the SAME clock. Authored as a constant local offset it
        // would rotate rigidly with the parent and sweep the same 22 deg the body does - and the foot
        // collider is frictionless, so it would visibly pivot and skid on the turf.
        public const float KickPlantSplay  = 14f;    // deg of hip abduction putting the plant foot wide
        public const float KickPlantStep   = 8f;     // deg putting it slightly ahead of the hips
        public const float KickPlantBrace  = 6f;     // deg the plant knee extends to brace through contact

        // ---- the follow-through crosses the body ----
        // An instep cross finishes high and ACROSS the midline. Every kicking-leg override used to be
        // Vector3(x, 0, 0), so the leg was locked in the sagittal plane and physically could not cross.
        public const float KickSwingOut    = 12f;    // deg the leg abducts out in the cock (diagonal plane)
        public const float KickContactCross = 8f;    // deg adducted at contact, so foot velocity is down the line
        public const float KickFollowCross = 26f;    // deg across the midline at the top of the follow-through
        public const float KickCalfCross   = 10f;    // ...and the shin lays across too, not a rigid pivot
        // How far the SUPPORT leg trails out behind on the follow-through. Selling that his weight has
        // come off it is most of what makes the hop read as one leg to the other.
        public const float KickTrailThigh  = 30f;    // deg
        public const float KickArmSwing    = 52f;    // deg the opposite arm swings across to counter it
        public const float KickArmSpread   = 28f;    // deg both arms open out while he is off the ground
        public const float KickElbowBend   = 34f;    // deg carried at the elbows throughout
        // The pop off the plant leg at contact. Small on purpose: it has to read as a follow-through
        // and not a jump, and the balance lock is off while he is in the air.
        public const float KickHopVel      = 1.9f;   // m/s upward at contact
        public const float KickHopDrift    = 1.1f;   // m/s forward, carried into the landing
        public const float KickHopSide     = 0.7f;   // m/s toward the plant side, so it is a hop ACROSS
        public const float KickHopGrace    = 0.30f;  // s before the upright lock may re-engage

        // ---- AI goalkeeper (striker mode): a ragdoll that shuffles + dives ----
        public const float AiKeeperReactZ = 14f;      // ball within this Z of goal -> keeper reacts
        public const float AiKeeperDiveThresh = 1.6f; // |x| offset beyond which he dives instead of shuffling
        public const float AiKeeperDiveLead = 0.9f;   // predicted ball-x lead time for the dive commit (s)
        public const float AiKeeperDiveCooldown = 1.1f; // min seconds between dives
        public const float AiKeeperDiveHoriz = 6.5f;  // dive lunge speed (scaled by ability)
        public const float AiKeeperDiveUp = 3.0f;     // dive upward pop (scaled by ability)
        // Low / grounded shots. By how far the ball is off the keeper (predicted x minus
        // his x):
        //   within AiKeeperSplayReach   -> Split (central) / SaveLeft-Right splay in place;
        //   within AiKeeperLowDiveReach -> a LOW dive (down + across to a bottom corner);
        //   beyond that                 -> shuffle a step or two toward it first, then dive.
        public const float AiKeeperLowBallHeight = 1.0f;  // predicted ball height below this = low save
        public const float AiKeeperSplitWidth = 1.2f;     // predicted crossing within this of the keeper = Split, else side splay
        public const float AiKeeperLowSaveUp = 1.2f;      // small hop on a side splay (stays low)
        public const float AiKeeperSplayReach = 1.6f;     // low ball within this of the keeper = splay/split in place
        public const float AiKeeperLowDiveReach = 4.5f;   // low ball within this = commit a low dive; beyond = step closer first
        public const float AiKeeperLowDiveUp = 1.6f;      // small upward pop on a low dive (stays low)

        // ---- Challenge modes (set from their pre-match screens) ----
        // Time Trial: round length in seconds.
        public static float TimeTrialSeconds = 60f;
        // Accuracy: round length and how many targets are up at once.
        public static float AccuracySeconds = 90f;
        public static int   AccuracyTargetCount = 4;
        // Free Kick / Penalty: where the dead ball sits and the defensive wall setup.
        public static float FreeKickDistance = 20f;    // metres out from goal for a free kick
        public static bool  PenaltyMode = false;        // true = penalty spot, no wall
        public static int   WallCount = 4;              // defenders in the wall
        public static float WallDistance = 9.15f;       // wall distance from the ball (regulation)
        public static float WallLateralOffset = 0f;     // shift the wall along the goal-parallel axis
        // Free-kick PLACEMENT picked on the pre-match map (the same SetPieceMap widget the
        // multiplayer host uses). When SetPiecePlaced is set the driver puts the ball and the wall on
        // these exact world points instead of deriving them from FreeKickDistance/WallDistance;
        // penalty mode ignores both (the spot is the spot). SetPieceRandomSpots overrides the
        // placement with a fresh legal spot every attempt, like the host's RANDOM SPOTS toggle.
        public static bool    SetPiecePlaced = false;
        public static Vector3 SetPieceBallSpot;
        public static Vector3 SetPieceWallCenter;
        public static bool    SetPieceRandomSpots = false;

        // ---- Auto serve ----
        public const float ServeFirstDelay = 1.6f; // before the first cross
        // Seconds between crosses (striker mode) - set from the pre-match screen.
        public static float ServeInterval = 3.5f;
        // Keeper mode: fixed continuous cadence, and a snappy resolve so callouts
        // don't hold up the next ball. 3s leaves room to actually get back on your line
        // and reset your feet between shots; at 2s the drill outran the keeper.
        public const float KeeperServeInterval = 3f;
        public const float KeeperResolveTime = 0.4f;

        // ---- Pre-match match settings (set from PrematchUI) ----
        // Striker mode: how good the AI keeper is (0 = does nothing, 1 = very active).
        public static float KeeperAbility = 0.5f;
        // Keeper mode: how hard the served shots are (0 = easy/slow, 1 = fast/tight).
        public static float ShotDifficulty = 0.5f;
        // Global multiplier on launched ball speed (crosses + shots). Pre-match slider.
        public static float BallSpeedMul = 1.0f;
        // Testing: leave the striker wherever it is between serves (no teleport back to
        // start). Set true to restore per-serve repositioning.
        public const bool ResetStrikerOnServe = false;
        public const float ServeTime = 1.25f;       // fixed time of flight (legacy default)
        // AI/auto crosser delivery. Crosses LOFT through the air by default (a low, crossable arc
        // that drops onto the target); GROUND is a fast, flat, low ball (only when toggled in the
        // cross map's Crosser tab). Longer flight time to the same target = higher arc, so this is
        // kept short so the lofted cross clears heads without ballooning into a moon ball.
        public const float CrossServeAirTime = 1.15f;     // lofted but LOW arc (~3m apex to an 8m target)
        public const float CrossServeGroundTime = 0.7f;   // driven low + fast
        // Distance-scaled cross flight time: t = k * sqrt(horizontalDistance), clamped. Constant
        // launch ANGLE at any range, so a near cross and a far cross both arc naturally (and, because
        // LaunchTo solves ballistically for whatever t, both still land exactly on target). The k's
        // are calibrated so the legacy fixed times are reproduced at ~8m: air 1.15/sqrt(8)~0.41,
        // ground 0.70/sqrt(8)~0.25. Clamp keeps a very short cross from being an instant bullet and a
        // half-field cross from ballooning into a moon ball.
        public const float CrossArcKAir    = 0.41f;
        public const float CrossArcKGround = 0.25f;
        public const float CrossArcMinTime = 0.5f;
        public const float CrossArcMaxTime = 1.8f;
        // Default landing spot (same every serve): centred, a bit off the goal line.
        public static readonly Vector3 ServeTarget =
            new Vector3(0f, 0.25f, GoalCenter.z - 8f);

        // ---- Freeplay delivery (set from the Freeplay pre-match screen) ----
        // How the ball comes to the player in freeplay.
        public enum Delivery { AutoCross, CornerLeft, CornerRight, AimSpot, BallAtFeet }
        public static Delivery FreeplayDelivery = Delivery.AutoCross;
        // Where an AimSpot cross lands (X across the mouth, Z off the line). Set by the
        // clickable penalty-box map. Defaults to the standard cross target.
        public static Vector3 FreeplayAimTarget = new Vector3(0f, 0.25f, GoalCenter.z - 8f);
        // Where a ball-at-feet spawns and respawns (in front of the striker's start).
        public static readonly Vector3 BallAtFeetSpot =
            new Vector3(0f, BallRadius, GoalCenter.z - 10f);

        // ---- Camera (mouse orbit / ball lock) ----
        public const float CamYawSpeed = 0.42f;    // deg per mouse-delta unit
        public const float CamPitchSpeed = 0.28f;
        public const float CamPitchMin = -6f;
        public const float CamPitchMax = 68f;
        public const float CamDistance = 6.2f;
        public const float CamLookHeight = 1.25f;

        // ---- Ragdoll drive ----
        public const float JointSpring = 6500f;     // snappier: limbs reach the pose fast
        public const float JointDamper = 150f;      // a touch more damping -> smoother, less jitter
        public const float JointMaxForce = 60000f;  // finite, but strong enough for quick swings

        // ---- Air flip control (mouse wheel, airborne only) ----
        // Scroll moves a TARGET lean angle about his right axis, clamped to +/-90 (parallel
        // with the ground). The whole body is spun toward that target and stops there, so
        // scrolling past parallel does nothing - no runaway spin. Scroll to lie flat for a
        // bicycle kick.
        public const float ScrollDeadzone = 0.0001f;     // ignore only true zero/noise
        public const float AirPitchStep = 30f;           // deg the target lean moves per scroll event (by sign)
        public const float AirPitchLimit = 115f;         // target clamp: 90 = parallel; a bit past horizontal (not a full 180 flip)
        public const float AirPitchGain = 8f;            // how hard he spins toward the target (1/s)
        public const float AirPitchMaxSpeed = 500f;      // cap on the spin speed toward the target (deg/s)
        // Acrobat capstone: the air-pitch target clamp opens to this so scrolling drives the body
        // past parallel and all the way around into full 360 forward/backward flips (chained up to
        // this many deg of headroom each way). Only used when PlayerProfile.PerkAcrobat is owned.
        public const float AcrobatFlipLimit = 720f;

        // ---- Dive header (hold Space while moving forward) ----
        // Carried run momentum is zeroed, then a modest up + forward launch tips him into
        // a belly-down header; gravity arcs him into the flop. Kept small so he doesn't
        // travel far. Pelvis yaw+roll pinned so the chest stays square.
        public const float DiveHoldTime = 0.28f;      // hold Space (moving fwd) this long -> dive; below = tap-jump
        public const float DiveUpVel = 2f;            // small upward pop (mostly horizontal dive)
        public const float DiveForwardVel = 10f;      // forward launch reach (dominant component)
        public const float DiveLayoutPitch = 90f;     // target forward pitch (deg); 90 = belly-down
        public const float DivePitchGain = 10f;       // how hard the pelvis is driven to that pitch
        public const float DiveDriveScale = 0.15f;    // limp body during the dive (spine won't hold upright)
        public const float DiveProneTime = 1.5f;      // base time prone after a dive/flop lands (Agility recovery nodes cut this)
        public const float DiveProneMinTime = 0.55f;  // floor: recovery upgrades can't drop below this
        public const float AcrobatRecoveryMul = 1.4f; // Acrobat capstone: extra divisor on prone recovery time
        public const float BalanceFrequency = 3.2f;
        public const float BalanceDamping = 0.85f;
        // Rate (1/s) the residual YAW RING of a non-biped body is bled off while it stands still.
        // 14 clears a ring in about 0.2 s, which reads as damped rather than as a snap. A biped never
        // runs this at all. See ActiveRagdoll.SettleYawRing for what it fixes and why the balance
        // constants above could not be the place to fix it.
        public const float StandYawSettleRate = 14f;

        // ---- Body preview (customize / species screens) ----
        // Ambient intensity while a PlayerPreview is on screen, restored when the last one closes.
        // The preview was lit by ONE directional at 1.1 against whatever ambient the menu sky left
        // (1.08), and raising that light did almost nothing: measured, the LIT side of the model is
        // albedo-capped at 0.60 luminance, so more key light has nowhere to go. All of the dimness was
        // on the shadow side, which is ambient-only - so ambient is the lever. At 1.95 the model's dark
        // half goes 0.235 -> 0.383 (+63%) while the lit side is unchanged, which reads as properly lit
        // rather than washed out; 2.4 lifts it further but flattens the shadow/lit ratio to 0.75 and
        // the body stops having any form.
        public const float PreviewAmbient = 1.95f;

        // ---- Scrimmage per-player match stats + ratings ----
        // Attribution windows. A "touch" is proximity-based: the sim has no ball-contact callback, and
        // the intent-bearing sites (a pass, a shot, a tackle, a keeper claim) note themselves
        // explicitly, so proximity only has to catch deflections and headers.
        public const float StatTouchRadius   = 1.25f;  // m from the ball to count as touching it
        public const float StatPassResolveWindow = 4f; // s a pass waits for someone to receive it
        // Passing.Launch TELEPORTS the ball 0.85 m off the passer's pelvis before it moves
        // (PassSpawnFromBody), so the spawn can land inside a pressing defender. Touches this soon after
        // a pass are that teleport, not a reception.
        public const float StatPassSpawnIgnore = 0.15f;
        public const float StatGoalCreditWindow = 6f;  // s back from a goal to look for who scored it
        // A keeper touch only counts as a SAVE if an opponent shot came in this recently and the ball
        // was actually moving. The shot is CONSUMED by the first save, or a ball pinballing off the
        // keeper would bank one save per parry cooldown.
        public const float StatSaveShotWindow  = 2.5f;
        public const float StatSaveMinBallSpeed = 6f;   // m/s

        // ---- Match rating (6.0 - 10.0, one decimal) ----
        // Base is 6.5, not 6.0, so the FLOOR and a neutral anonymous performance are different numbers:
        // at 6.0 both "never touched the ball" and "conceded five" would print the same rating.
        public const float RatingBase    = 6.5f;
        public const float RatingMin     = 6.0f;
        public const float RatingMax     = 10.0f;
        // Match EVENTS, not volume: never normalised.
        public const float RatingGoal    = 1.20f;
        public const float RatingAssist  = 0.70f;
        public const float RatingConcede = 0.45f;   // deliberately > RatingSave: a shelled keeper must
                                                    // not be able to save his way back to neutral
        public const float RatingCleanSheet = 0.60f;
        public const int   RatingCleanSheetMinSaves = 1;   // a keeper who faced nothing gets no bonus
        // VOLUME terms. These accumulate with match length, and match length is a PRE-MATCH OPTION
        // spanning 2 to 10 minutes (PrematchUI), with roster size spanning 3 to 11 a side on top. An
        // unnormalised sum would therefore rate a 10-minute 3v3 far above a 2-minute 11v11 for
        // identical play, which is why every one of these is divided by the match's length against
        // RatingRefSeconds before it is weighted.
        public const float RatingRefSeconds = 180f;  // the 3 min default; the weights below are tuned here
        public const float RatingShot     = 0.06f;
        public const float RatingPassDone = 0.035f;
        // Low on purpose. A human pass fires straight down the look ray with no target snap, so it
        // misplaces far more than the AI's BestTarget-aimed pass does; a heavy penalty would make
        // volume passing net negative for a human and not for a bot. An UNRESOLVED pass (nobody
        // touched it at all) is charged nothing rather than counted lost.
        public const float RatingPassLost = 0.015f;
        // Cut from a first-draft 0.14. Tackles fire off proximity plus a 0.9 s cooldown, so even with
        // the carrier gate they are the cheapest event to repeat, and at 0.14 the column dominated.
        public const float RatingTackle   = 0.05f;
        public const float RatingSave     = 0.30f;

        // ---- Scrimmage landing reticle ----
        // A disc on the turf under where an airborne ball will come down. Scrimmage only.
        // The ball must be genuinely airborne and the flight long enough to be worth telegraphing:
        // below MinHeight it is a roll, and outside the time window the disc is either a flicker or a
        // prediction nobody can use.
        public const float ScrimReticleMinHeight = 0.85f;  // m above resting height before it draws
        public const float ScrimReticleMinTime   = 0.22f;  // s of remaining flight
        public const float ScrimReticleMaxTime   = 2.6f;
        // The ring geometry itself is AimReticle's (RingRadius there); recorded here so the two
        // cannot silently disagree if either is retuned. Shrunk from 0.7 to 0.35 when the reticle
        // became a small circle + crosshair instead of a landing-zone disc.
        public const float ScrimReticleRadius    = 0.35f;  // ring radius on the turf, matches AimReticle
        // Cyan, so it reads against turf and against both kit colours without being mistaken for a
        // marking or a team indicator.
        public static readonly Color ScrimReticleTint = new Color(0.35f, 0.85f, 1f, 1f);

        // ---- Striker locomotion ----
        public static float StrikerMoveSpeed = StrikerMoveSpeedBase;  // pre-match slider. LOW base on purpose: an
                                                        // uninvested striker is sluggish; Pace nodes
                                                        // (SkillTree "move"/"sprint") swing this hard.
        public const float StrikerSprintMul = 1.8f;  // Shift-held speed multiplier
        // How hard Pace swings top speed. The trait multiplier itself (PlayerProfile.MoveSpeedMul /
        // SprintSpeedMul) runs about 0.85 uninvested to ~2.1 on a full Pace build, which at gain 1
        // put a sprint between 5.8 and 14.5 m/s. That is a real spread on paper and read as flat on
        // the pitch, because AI outfielders run a FLAT AiOutfieldSpeed (5 m/s): at the uninvested
        // end you are pinned to the pack and only the very top of the tree ever pulls away.
        // This multiplies the trait's DEVIATION ABOVE 1, so an uninvested or heavy build is untouched
        // and the invested end is amplified.
        //
        // BACK TO 1 (neutral). At 2 the top end reached ~32 m/s, not the ~22 first estimated: that
        // estimate missed the Afterburners capstone, which multiplies sprint by AfterburnerMul (1.30)
        // on top of the tree. The top is now set by SprintSpeedCeiling instead of by this gain, which
        // is the honest way round - a ceiling states the number it guarantees, where a gain only
        // implies one and goes stale the moment a Pace node is retuned. Left in place as the lever for
        // how steeply pace ramps BELOW the ceiling.
        public const float PaceSpeedGain = 1f;
        // Hard ceiling on ground speed, sprint included. Set to exactly what a maxed Pace build
        // reaches today:
        //     StrikerMoveSpeed 3.8 x StrikerSprintMul 1.8 x SprintSpeedMul 2.87 = 19.7 m/s
        // (SprintSpeedMul's own top is BodySprint 1.105 x the tree's sprint nodes 2.00 x
        // AfterburnerMul 1.30.) So at PaceSpeedGain 1 this clips NOTHING - the full tree is spendable
        // and the top of it is the fastest anyone can be. An uninvested build still sprints ~5.8, so
        // pace is worth a 3.4x spread end to end.
        //
        // It stays as a BACKSTOP rather than being deleted: it is applied last, after every
        // multiplier, so a Pace node added or retuned later cannot quietly raise the game's top speed
        // without someone changing this number on purpose.
        public const float SprintSpeedCeiling = 19.7f;  // m/s
        public const float StrikerAccel = 22f;      // applied to every bone (whole-body translation)
        public const float JumpVelocity = 7.155f;   // m/s upward on a standing jump (base). ~20% lower peak height than 8.0 (h proportional to v^2, so sqrt(0.8)*8). Trait/run/sprint muls stack on top.
        public const float RunJumpMul = 1.0f;        // running jumps now go full height (more vertical pop)
        public const float SprintJumpMul = 0.85f;    // sprinting jumps a touch lower than that
        public const float RunJumpForwardKeep = 0.5f; // fraction of run momentum kept on a moving jump (toned down)
        public const float BicycleBackSpin = 14f;   // angular impulse for backward rotation

        // ---- Run cycle (procedural gait) ----
        // The body GLIDES via velocity; the limbs are cosmetic pose overrides. Keep the
        // fore/aft thigh swing modest (too much reads as skating over the glide) but pick
        // the feet up high with a hard knee fold so it looks like a smooth high-knee run.
        public const float StrideRateMax = 9f;      // gait phase speed (rad/s) at full run
        public const float GaitThighSwing = 32f;    // deg the thighs swing fore/aft (small -> no scissor/skate)
        public const float GaitThighLift = 70f;     // deg the swing leg lifts (pronounced foot pickup)
        public const float GaitKneeBend = 145f;     // deg the knee folds to pick up the foot (high knee, heel up)
        public const float GaitFootPoint = 20f;     // deg foot dorsiflex through the stride
        // Sprint gait: knees fold harder toward the body and lift higher, faster cadence.
        public const float SprintStrideMul = 1.5f;  // faster leg cadence when sprinting
        public const float SprintThighLift = 95f;   // higher knee lift when sprinting
        public const float SprintKneeBend = 160f;   // knee folds more toward the body
        public const float GaitTorsoLean = 8f;      // deg forward lean while running
        public const float LegSwingRaise = 130f;    // deg a leg raises on LMB/RMB (knee to chest)
        public const float LegRaiseMaxDeg = 90f;     // hard cap on thigh raise: 90 = straight out (max bicycle reach)
        public const float LegRaiseEase = 8f;        // how fast a leg raise / header bend eases in-out (per sec); no snap-back
        public const float BicycleLegEase = 22f;      // single airborne leg snaps up this fast (bicycle kick)
        public const float BicycleLegRaiseMul = 1.35f; // and this much higher than a normal raise
        public const float HeaderLegRaiseMul = 0.25f; // airborne header: legs come forward only minimally
        public const float HeaderTorsoBend = 90f;    // deg the torso folds forward on an airborne header (snappy, far)
        public const float HeaderBendEase = 60f;     // how fast the torso snaps forward into the header (very fast)
        public const float HeaderGrace = 0.12f;      // sec an airborne header stays live after the click (GK-split-style)

        // Sit-down gesture: LMB+RMB pressed TOGETHER while standing drops him on his backside.
        // The window is the same idea as HeaderGrace - two clicks a few frames apart still count
        // as one gesture - but it only opens on the second button's PRESS EDGE, so pressing one,
        // swinging, then pressing the other is still two ordinary leg raises.
        public const float SitWindow    = 0.18f;  // sec the two clicks may be apart and still read as together
        public const float SitRaiseMax  = 0.5f;   // a leg already this far up is a committed strike - no sit
        public const float SitDrop      = 0.55f;  // m the hips sink to seat height (scaled by build height)
        public const float SitDropEase  = 2.2f;   // m/s the hips sink into, and rise out of, the sit
        public const float SitPoseSpeed = 4f;     // pose blend rate into Sit and back to Stand
        // Arbitration with the SLIDE TACKLE, which reads the identical both-buttons combo in
        // scrimmage (ScrimmageGame: `if (_input.LeftLegHeld && _input.RightLegHeld) TrySlideTackle();`).
        // Speed is the discriminator, measured on FLAT PELVIS VELOCITY because that is what
        // TrySlideTackle measures - the two gates have to be in the same units to be mutually
        // exclusive. Sitting needs him near-stationary; sliding needs SlideTackleMinSpeed (3.5).
        // The gap between 1.2 and 3.5 is dead ground where neither fires, which is deliberate: a
        // jogging player who mashes both buttons gets nothing rather than a coin flip.
        // LMB+RMB is one combo with TWO outcomes, and the MOVE STICK picks which: pushing forward
        // slides, pulling back sits. It used to be arbitrated by SPEED instead (sit under 1.2 m/s,
        // slide over 3.5), which meant the same intent gave different results depending on how fast he
        // happened to be travelling, and neither was reachable on purpose from a standing start. The
        // deadzone is wide enough that a neutral stick does neither.
        public const float BothButtonMoveDeadzone = 0.35f;   // |Move.y| needed to pick a side
        public const float SitMaxSpeed  = 1.2f;   // m/s: legacy speed gate, no longer arbitrates (see above)

        // ---- Sliding challenge (LMB+RMB pushed FORWARD) ----
        // A slide COMMITS: releasing the buttons does not cancel it, because a real one cannot be
        // taken back halfway. He rides it out and gets up, and cannot start another until Recover
        // has passed.
        public const float SlideDuration = 0.85f;   // s committed to the slide before he gets back up
        public const float SlideLunge    = 6.5f;    // m/s forward push launching the slide
        public const float SlideDrop     = 0.5f;    // m the hips sink (x build height), as SitDrop
        // Horizontal velocity retained per 60 Hz FRAME while down. It is applied per RENDER frame
        // (Striker.Tick is pumped from Update), so it has to be raised to Time.deltaTime*60 or the
        // slide's length becomes a function of the player's monitor. Measured with locomotion off, a
        // 6.5 m/s launch and fixedDeltaTime 0.014: as a raw per-frame multiply it carried 3.34 m at
        // 30 fps, 2.29 m at 60, 1.10 m at 144 and 0.69 m at 240 - a 4.8x spread. Raised to dt*60 the
        // same integration holds 2.24 / 2.29 / 2.31 / 2.32 m across that range.
        public const float SlideFriction = 0.96f;
        public const float SlideRecover  = 0.45f;   // s after standing up before he can slide again
        public const float SlidePoseSpeed = 7f;     // pose blend into Slide: faster than the sit, it is a lunge
        // Ceiling on the TOTAL horizontal launch speed (carried run + SlideLunge). It exists because
        // the slide now runs with LocomotionEnabled false, and with the locomotion servo out of the
        // way nothing else caps what he arrives with. Measured travel is linear in launch speed at
        // 0.354 m per m/s (2.30 m from 6.5, 4.72 m from 13.3), so:
        //     standstill        6.5  -> 2.30 m
        //     base run    3.8 + 6.5  -> 3.65 m   (under the cap; the cap changes nothing here)
        //     base sprint 6.8 + 6.5  -> 4.26 m   (capped, from 4.72)
        //     maxed Pace 19.7 + 6.5  -> 4.26 m   BRAKED to the ceiling, which is why this exists
        // Pace still buys reach; it cannot buy a slide across a third of the box. Same shape as
        // SprintSpeedCeiling - a backstop that states the number it guarantees.
        public const float SlideLaunchMax = 12f;   // m/s
        // The slide hands off to a LIMP phase rather than snapping upright, reusing the diving
        // header's mechanism (DriveScale down, upright/balance/locomotion off, one timer, EndTrick
        // restores). SlideDuration 0.85 + SlideLimpTime 0.6 = 1.45 s of total commitment, against the
        // KnockdownTime 1.4 s the man you felled spends down: landing a tackle trades about even on
        // time, so it is neither a free tempo win nor a punishment for connecting.
        public const float SlideLimpTime = 0.6f;       // s limp on the deck before he gets up
        public const float SlideLimpMinTime = 0.3f;    // floor: recovery upgrades can't drop below this
        // The same number as DiveDriveScale today, on purpose - the request was "limp like the diving
        // header". Its own knob so retuning the dive cannot silently retune the slide.
        public const float SlideLimpDriveScale = 0.15f;
        // Arm pump (both keeper + striker): upper arms swing fore/aft opposite the legs,
        // elbows held bent. Reads as a runner's arm carriage over the glide.
        public const float ArmPumpSwing = 45f;      // deg upper arm swings fore/aft
        public const float ArmPumpElbow = 65f;      // deg the elbow stays folded

        // ---- Shared gait (Gait.cs): cadence, fade, stance bend ----
        // StrideRateMax above is no longer the cadence. Cadence is now 2pi * MEASURED speed / stride
        // length, which is the fix for the skating: a key press used to take the legs to full rate
        // instantly while the body was still accelerating from rest. At full walk and full sprint
        // the new maths lands on 8.98 and 13.5 rad/s, i.e. exactly the old StrideRateMax and
        // StrideRateMax * SprintStrideMul, so the human tempo at the two ends is unchanged.
        public const float GaitRateMax    = 22f;   // safety cap on cadence (rad/s); trait muls can outrun sprint speed
        public const float GaitMinSpeed   = 0.35f; // below this the gait is fully faded out
        public const float GaitFadeSpeed  = 1.1f;  // m/s of speed over GaitMinSpeed to reach full gait
        public const float GaitFadeIn     = 9f;    // gait weight ease-in (per sec)
        public const float GaitFadeOut    = 16f;   // ...and ease-out, faster so leaving the ground drops it
        public const float GaitKneeStance = 12f;   // deg the stance knee holds bent (a locked stick reads as stilts)

        // ---- Whole-body carry servo (ActiveRagdoll) ----
        // The pelvis height was never servo'd, so the body physically hung off whichever leg the
        // COSMETIC gait happened to have planted, and every stride dropped the hips onto the next
        // one. No leg pose can fix that, because the legs carry no weight. So carry the body: drive
        // the hips to their authored standing height and assign the SAME vertical velocity to every
        // bone, which lifts the assembly rigidly instead of stretching it against its own joints.
        // Gated on grounded + upright, so a jump, dive, trick, keeper lay-out or tumble never sees it.
        // ---- absolute floor guard ----
        // Every play surface in the game is a slab whose TOP face is y = 0 (PitchBuilder's "Ground"
        // and ScrimmageArena's "ScrimGround" both centre themselves so the top lands there), so a
        // single world constant is a valid floor for every mode.
        //
        // This is a LAST-RESORT invariant, not a positioning system. It exists because bodies were
        // getting stuck in the ground and every softer mechanism had already failed: the grounding
        // probe cannot see a floor it is already beneath, the carry servo is gated off exactly then,
        // and the eight direct rb.position writes in ActiveRagdoll bypass continuous collision so
        // nothing sweeps them. The tolerance is deliberately generous - a bone CENTRE legitimately
        // sits below zero when a limb is flat on the turf and its collider radius carries it - so
        // this only fires when a body is unambiguously wrong, and then it moves the WHOLE body as one
        // rather than dragging a single bone through its joints.
        public const float BodyFloorClampY = -0.30f;   // lowest a bone centre may be before a rescue
        public const float CarryHeightGain     = 10f;   // vertical velocity per metre of height error
        public const float CarryHeightMaxSpeed = 2.4f;  // m/s cap, so a big correction is still a glide
        public const float CarryErrUp          = 0.60f; // max height error it will lift out of (m)
        public const float CarryErrDown        = 0.30f; // ...and push down out of

        // Moonwalk celebration: steady backward glide speed (m/s) while the shuffle pose plays.
        public const float MoonwalkGlideSpeed = 2.2f;

        // ---- Trick validation ----
        // A bicycle is a fast whole-body flip: the pelvis sweeps through the "reclined"
        // cone in a couple of frames, so reading the angle at the exact contact frame is
        // unreliable. Instead the Striker LATCHES a bicycle window the moment the player
        // commits (airborne + leaning back past the arm threshold, or scrolling the
        // air-pitch target back), and holds it open BicycleWindow seconds so contact,
        // camera, and assist all read a stable "yes". Arm loose, gate legal shots tight.
        public const float BicycleWindow = 0.85f;   // seconds the latched attempt stays "live"
        public const float BicycleArmUpness = 0.72f; // pelvis-up dot world-up below this (tipped ~44deg+) ARMS the window
        public const float BicycleArmPitch = 55f;    // OR: air-pitch target leaned past this many deg arms it
        public const float ValidHitBonus = 6.5f;     // extra ball speed on a clean trick

        // ---- Sniper (hidden 4th role, dormant scaffold) ----
        // A shooter perched high in the stadium that tries to hit the striker or the
        // ball. Off by default; flesh out via Sniper.cs. Perch is high above a corner
        // looking across the box.
        public static readonly Vector3 SniperPerch =
            new Vector3(FieldWidth * 0.5f + 6f, 20f, GoalCenter.z - 4f);
        public const float SniperFireInterval = 2.5f;  // seconds between shots
        public const float SniperAimTime = 0.9f;        // lead-in aim time before a shot
        public const float SniperRange = 120f;          // hitscan range
        public const float SniperLead = 0.15f;          // how much to lead a moving target (s)

        // ---- Arcade aim assist (on striker contact) ----
        // Subtle, brief curve that biases a struck ball toward the goal so more shots
        // are on target, without removing the challenge. Kept small on purpose.
        // Base aim assist is LOW so an untrained player is inaccurate; Shooting/Control
        // skill nodes raise it noticeably (ShotAccuracyMul stacks on top).
        public const float AssistSteerFrac = 0.16f;  // base goal-ward bend (0..1); low by default
        public const float AssistDuration = 0.45f;   // seconds the curve is applied after contact
        public const float AssistMinSpeed = 3.5f;    // only assist shots hit with some pace
        public const float AssistMaxAccel = 18f;     // cap on the sideways/steer accel
        // Aim assist only kicks in when the striker is actually FACING the opponents' goal.
        // Facing dir . dir-to-goal must exceed this cosine (0.34 ~= within a ~70deg half
        // cone). Turned side-on or facing his own goal -> no assist at all.
        public const float AssistFacingDot = 0.34f;

        // ---- Auto ball-cam on a shot ----
        // After a genuine SHOT (not a trap/dead touch), the camera snaps to ball-cam for
        // this long so the player can watch it fly on or off target, then reverts.
        public const float ShotCamSeconds = 2f;
        // A contact only counts as a "shot" (worth cutting to ball-cam) if the ball leaves
        // with at least this much horizontal pace toward the goal.
        public const float ShotCamMinSpeed = 8f;
        // MP Striker crowd-boo miss detection: goalward ball speed (m/s on +z) that arms a "shot"
        // whose failure to score counts as a miss. Above pass/dribble pace so a pass or a nudged
        // loose ball never arms it - only a struck attempt at goal.
        public const float MissShotSpeed = 12f;
        // The auto ball-cam now ONLY cuts for a shot taken FACING AWAY from the opponents'
        // goal - the bicycle / over-shoulder shots the striker can't watch himself. When he's
        // facing the goal (dead-ahead in the cone OR merely side-on) he can already see it, so
        // no cam. A shot counts as "facing away" when (facing . dir-to-goal) is below this
        // cosine. -0.2 ~= turned more than ~100deg off goal (clearly over the shoulder / behind).
        // Bicycles always qualify regardless (their latched trick state forces the cut).
        public const float ShotCamFaceAwayDot = -0.2f;

        // ---- Strike power (on striker contact) ----
        // Base power is modest by default; Shooting nodes + body traits multiply it up.
        public const float StrikeHorizBoost = 1.25f; // multiply horizontal velocity when struck (low base)
        public const float StrikeHorizMax = 26f;     // cap on resulting horizontal speed (m/s)

        // ---- Body-part accuracy + power (which part of the body struck the ball) ----
        // Accuracy = the fraction of AssistSteerFrac applied (how much the shot is helped
        // toward goal). Strong foot/leg is the reference (full); weak side is half; a body
        // (torso/pelvis) touch is scrappy and inaccurate. Head is handled by heading rules.
        public const float ArmHitboxScale = 1.9f;    // KEEPER base arm collider radius vs the thin visible arm (times KeeperHitboxBoost)
        public const float StrikerArmHitboxScale = 2.6f; // OUTFIELD arm collider radius: fatter than the keeper base so an arm/hand touch reliably TRAPS the ball instead of glancing off / phasing through
        public const float LegHitboxScale = 1.6f;    // keeper/striker leg collider radius vs the visible leg
        // The keeper multiplies its arm/leg/foot/glove hitboxes by this on top of the base
        // scales, so every limb is chunkier than the visible body part and saves connect off
        // any part of an arm, leg, hand, or foot. Striker keeps the base scales (boost = 1).
        public const float KeeperHitboxBoost = 1.6f;
        // Extra reach on the keeper's GLOVES (hands) on top of KeeperHitboxBoost, so a dive
        // connects on a near-miss for more dramatic saves. 1.35 * 1.6 base -> a big catch radius.
        public const float KeeperGloveReach = 1.35f;
        public const float StrongFootAccuracy = 1.0f;
        public const float WeakFootAccuracy   = 0.3f;    // weak leg/foot: much less accurate
        public const float WeakFootPowerMul   = 0.6f;    // and weaker
        public const float BodyAccuracy       = 0.1f;    // body/arms: basically no aim help
        public const float BodyPowerMul       = 0.25f;   // body/arms: super weak - traps the ball, drops it

        // ---- Set pieces (free kick / penalty) + volleys: scripted power, stat-scaled ----
        // A set-piece strike IGNORES the foot's swing speed - the ball is dead and the strike is
        // scripted, so any clean contact leaves the boot high, fast, and goalward. WHERE on the
        // ball it is struck picks the spin/bend; Shooting POWER scales the launch speed + bend;
        // Shooting ACCURACY (+ power) scales the goal-steer. Values are tuned aggressive on
        // purpose - the old set pieces were far too weak even at max stats.
        public const float SetPieceBaseSpeed   = 22f;    // goalward launch speed floor at power 1.0 (m/s)
        public const float SetPieceMaxSpeed    = 29.4f;  // hard cap on a set-piece launch (x Cannon ceiling); scaled down 30% from 42
        public const float SetPieceLoft        = 0.32f;  // up-velocity as a fraction of launch speed (gentle; the vy cap + ballistic solve own the real height)
        public const float SetPieceCurl        = 12.0f;  // base curl/bend accel (x ShotPowerMul) - pronounced
        public const float SetPieceAssistFloor = 0.08f;  // goal-steer with NO shooting investment (near zero)
        public const float SetPieceAssistMax   = 1.6f;   // goal-steer fully invested in Shooting power + accuracy (drastic)
        // ---- Guided placement (accuracy + strike location drive the shot, NOT power) ----
        // The set-piece launch blends toward a ballistic solve that REACHES a 3D goal corner,
        // by the skill-only combined stat; a hard vy cap keeps every shot near goal height so a
        // miss never skyrockets. See BallController set-piece launch block.
        public const float SetPieceCornerInset  = 0.35f; // how far inside the post/bar the corner aim sits (m)
        public const float SetPieceLowStrike     = 0.20f; // struck-height dot at/below this -> aim the TOP corner
        public const float SetPieceFlightTime    = 0.72f; // nominal flight time for the ballistic corner solve (s)
        public const float SetPieceApexMargin    = 0.55f; // most the launch apex may clear the crossbar by (m) - the hard vertical ceiling
        public const float AssistVertFrac        = 0.55f; // vertical goal-steer strength (x _accuracyMul), capped by AssistMaxAccel

        // ---- Set-piece TAKER: AI aesthetic runup + Space power meter + WASD spin ----
        // The striker's runup + swing are purely cosmetic (AI-driven). The player controls ONLY
        // an oscillating power meter (Space) and WASD spin (held silently). Release commits, runs
        // the runup, and fires a scripted LaunchSetPiece. Overcharging power or over-holding spin
        // botches the shot. See Play/SetPieceTaker.cs.
        public const float SetPieceMeterRate     = 1.6f;  // power-meter ping-pong speed (full sweeps/sec-ish)
        public const float SetPieceReleaseDebounce = 0.05f; // Space must read UP this long (s) before a release commits - rejects a single-frame input drop that used to fire the shot mid-hold
        public const float SetPieceOverchargeTime = 0.45f; // seconds pegged at max power before it OVERCHARGES (botch); Accuracy widens this
        public const float SetPieceSpinChargeRate = 1.1f;  // WASD spin charge build rate (per second held, 0..1 then over)
        public const float SetPieceSpinOverTime  = 1.35f; // seconds holding a spin dir past full before it BOTCHES; Accuracy widens this
        public const float SetPieceBotchScatterX = 3.2f;  // max horizontal target scatter (m) at full botch
        public const float SetPieceBotchScatterY = 1.1f;  // max vertical target scatter (m) at full botch (still capped by apex)
        public const float SetPieceCornerPull    = 0.85f; // how far combined pulls the aim from centre toward a corner (0..1 of the half-goal)
        // Power STAT scales the whole launch-SPEED band the on-screen bar sweeps (never height).
        // At the MIN power stat a FULL bar tops out at SetPieceMinStatSpeed (a weak lob); at the MAX
        // power stat a full bar reaches SetPieceMaxSpeed. The empty end of the bar is always this
        // fraction of the stat's ceiling, so the bar always has travel. A WIDE spread by design so an
        // uninvested striker is clearly weaker. NOTE: this path intentionally does NOT use
        // SetPieceBaseSpeed as the floor (that constant is shared with the open-play strike launch);
        // the scripted set-piece speed is rebuilt entirely from these two, see LaunchSetPiece.
        public const float SetPieceMinStatSpeed   = 10f;   // full-bar launch speed (m/s) at 0 power stat
        public const float SetPieceLaunchFloorFrac = 0.55f; // empty-bar speed as a fraction of the stat's ceiling
        // ---- Absolute CEILINGS on a scripted set-piece launch (LaunchSetPiece) ----
        // These raise the top of each range WITHOUT touching how any stat or input scales into it:
        // the power stat still picks where the speed band sits, the power bar still sweeps that band,
        // and camera pitch still owns the launch height. Only the ceiling each one runs into moves,
        // so a maxed-out shot goes further past what it used to while a weak one is unchanged.
        // Applied to the scripted launch ONLY - the physical (foot-contact) set-piece strike keeps
        // the raw constants. Shared by single player and the multiplayer host (same launch call).
        public const float SetPieceCurveMaxMul  = 1.15f;  // A/D curve bend ceiling: +15%
        public const float SetPieceApexCeilMul  = 1.25f;  // camera-aimed apex height ceiling: +25%
        public const float SetPieceSpeedCeilMul = 1.10f;  // launch-speed ceiling: +10%
        // OVER-THE-BAR LOFT (m/s of extra UPWARD velocity, on top of the clean apex cap). Driven by
        // shot POWER and INVERSE accuracy: loft = power01 * (1 - accuracyStat) * this. So a high-power
        // shot with LOW accuracy balloons well over the crossbar, spending accuracy pulls the loft
        // down, and at MAX accuracy the loft is zero (the ball caps right at the crossbar). A soft
        // overcharge bonus is added on top so over-holding the bar still sails a touch higher.
        public const float SetPieceLoftVy         = 8.0f;  // max over-bar loft (m/s) at full power + zero accuracy
        public const float SetPieceOverchargeVy   = 2.5f;  // small extra loft (m/s) at full overcharge, on top of the power/accuracy loft
        // Power-bar GATE for the over-bar loft: below this bar fraction the power-driven loft is ZERO
        // (the shot just travels slow + low), and from here to a full bar it ramps up to the SAME loft
        // it has always had at full power. Set to the HIGH-RED end of the meter so a low-accuracy
        // player only balloons the ball when they deliberately hold the bar up into the red, not on a
        // mid-bar shot. (The overcharge bonus above is unaffected - it already only accrues at max.)
        public const float SetPieceLoftGate       = 0.8f;
        // Swerve is driven PRIMARILY by the accuracy stat: curl magnitude scales from this floor
        // (a raw striker still bends a little) up to full at max accuracy. The WASD spin hold only
        // modulates within that band, so a low-accuracy striker can't buy a big banana with WASD.
        public const float SetPieceCurlAccFloor  = 0.7f;
        // ---- Look-ray aim (free kicks/penalties + net set pieces) ----
        // Minimum genuine charge before a release can commit the shot. A near-instant tap is below
        // this and commits NOTHING (the meter just keeps charging), so the FIRST press of a round can
        // never fire the shot the instant Space is hit. Space is pure hold-to-charge / release-to-shoot
        // (there is no longer a tap-dribble shot).
        public const float SetPieceMinChargeTime = 0.12f;
        // Max look-aim scatter (m) a ZERO-accuracy striker sprays, keyed to accuracy ONLY (power does
        // NOT affect it) and shrinking to 0 at full accuracy. Kept WELL under the goal half-width
        // (3.66 m): at 7 m the random offset was ~2x the half-width in every direction, so the aim
        // landed anywhere in a 14x14 m box and the player's look ray was completely swamped - shots
        // appeared to ignore where you aimed and only spin visibly bent the path. 1.5 m still makes a
        // raw striker spray noticeably without overriding the aim.
        public const float SetPieceLookScatterMax = 1.5f;
        // AIM CONE: the goal-ward look window inside which aim assist applies. If the aim ray lands
        // more than this many degrees off the ball->goal line (looking egregiously to the side), the
        // shot is FORCED off target regardless of accuracy: the goal-ward steer, the curl-return, and
        // the vertical steer are all cut so nothing can drag the ball back into a corner, and the aim
        // is shoved further wide. 45 deg = a 90 deg full cone. Inside the cone, accuracy is untouched.
        public const float SetPieceAimConeHalfAngle = 45f;  // degrees off the ball->goal line before a shot is forced wide
        public const float SetPieceOffTargetPush    = 3.0f; // extra metres the aim is shoved outward once outside the cone (guarantees it clears the post)
        public const float SetPieceRunupSpeed    = 5.5f;  // run-in speed (m/s); matches a brisk approach (the driver places the taker ~3m back)
        public const float SetPiecePlantOffset   = 0.55f; // stops the run-in this far short of the ball (plant beside it)
        // Seconds of leg swing after the plant before contact, and the rate the follow-through and
        // rebalance then run at (SetPieceTaker.TickStruck). Was 0.22, which is half the crosser's
        // windup and too quick to see a windup, a strike and a recovery inside; 0.32 reads as a kick
        // without noticeably delaying the launch. Host and client both derive their swing from this
        // constant, so changing it cannot desync a networked set piece.
        public const float SetPieceSwingTime     = 0.32f;
        public const float SetPieceSettleTime    = 0.8f;  // taker Settle hold after the strike before it goes Idle
        // Spin is chosen by WHERE the ball is struck (contact point in the shot frame):
        public const float SetPieceSideThresh  = 0.30f;  // |side dot| beyond this -> side spin (bends the SAME way struck)
        public const float SetPieceTopThresh   = 0.45f;  // struck-height dot above this -> top spin (dips)
        public const float SetPieceTopSpinMul  = 0.8f;   // downward-curl strength for top spin (x base curl)
        public const float SetPieceKnuckleVert = -0.10f; // struck at/below this height = the chip / knuckle zone
        public const float SetPieceKnuckleChance = 0.20f; // 20% base chance a bottom strike knuckles instead of chipping (rises linearly w/ power)
        public const float SetPieceKnucklePaceMul = 1.15f; // a knuckle drives flatter + faster than a chip
        public const float SetPieceKnuckleMul  = 0.9f;   // wobble strength of a knuckle (x base curl, scales LINEARLY with power) - pronounced
        // Scripted knuckle (S) AIR WIGGLE: an oscillating side-to-side lateral force applied over the
        // flight so the ball visibly snakes left-right in the air. Amplitude scales LINEARLY with shot
        // POWER (a weak knuckle barely wobbles, a full-power one snakes hard). These are SEPARATE from
        // SetPieceKnuckleMul (which the open-play strike path also uses) so tuning the wiggle can't
        // touch open play. Amplitude is an ACCELERATION (m/s^2) so it's mass-independent.
        public const float SetPieceWiggleAmp   = 48f;    // peak lateral wiggle accel (m/s^2) at full power (toned down from 70)
        public const float SetPieceWiggleFreq  = 9.0f;   // wiggle oscillations rate (radians/sec); ~1.4 full snakes per second
        public const float SetPieceChipLoft    = 0.95f;  // a bottom-strike chip scoops up high (up-vel fraction of launch)
        public const float SetPieceChipPaceMul = 0.65f;  // ...with softer forward pace than a driven shot

        // ---- Kick vs. run-into: only a SWINGING leg imparts real power ----
        // The struck bone's own speed decides how live the touch is. A kick swings the
        // foot/leg fast; just running into the ball translates the whole body at move
        // speed. Below the floor speed the touch barely nudges the ball (a trap/dribble);
        // above the full speed it strikes at full power; it lerps between.
        public const float KickSpeedFloor = 4f;      // bone speed (m/s) below this = a dead touch
        public const float KickSpeedFull  = 9f;      // bone speed at/above this = a full strike
        public const float DeadTouchPower = 0.12f;   // velocity kept on a dead (non-kicking) touch

        // ---- NO-CARRY MODES: a dead touch must never park the ball under his feet ----
        // Striker / Freeplay / Time Trial have no dribble at all, so the normal trap (keep 12%
        // of the pace) left the ball resting between his boots with nothing to hand it to, and
        // every follow-up swing was point blank. In those modes a dead touch instead PUSHES the
        // ball away from the body: more pace is kept AND a floor is enforced on the outward
        // component, so a loose ball always leaves the feet and has to be run down again.
        public const float NoCarryTouchKeep     = 0.55f; // velocity kept (vs DeadTouchPower above)
        public const float NoCarryTouchMinSpeed = 2.6f;  // m/s floor on the OUTWARD (away-from-pelvis) flat speed
        public const float NoCarryTouchSuppress = 0.18f; // s the SAME body cannot re-touch, so a trailing leg in
                                                         // the same stride cannot immediately re-glue the ball

        // ---- Volley: a FLYING ball met by a SWINGING leg launches like a free kick ----
        // A ball whose centre is above this height (m) is "flying"; a swinging leg (kick > 0)
        // that hits it fires the set-piece launch (loft + contact-point curl, stat-scaled)
        // instead of trapping. The bar is set to just off the turf: a ball resting on the
        // ground has its centre at BallRadius (0.22), so BallRadius + 0.03 means the ball
        // counts as airborne the moment its underside clears ~3cm of grass. The small buffer
        // keeps a still, jittering ball from falsely registering as a volley.
        public const float VolleyMinBallHeight = BallRadius + 0.03f;   // 0.25

        // ---- VOLLEY tuning (the PHYSICAL foot-strike volley ONLY) ----
        // Every constant in this block is read behind an `if (volley)` in
        // BallController.OnCollisionEnter. The scripted set piece (LaunchSetPiece, i.e. free
        // kick and penalty mode) shares the same launch code but never reaches these, so free
        // kicks are unchanged.
        //
        // Side contact used to bend at the full set-piece curl for AssistDuration+0.5s, which
        // accrues ~11 m/s of lateral velocity: the ball travelled sideways and stopped closing
        // on goal. Cut the bend, cut the spin, shorten the window, and put the difference into
        // goalward pace.
        // RAISED from 0.45/0.85/0.50. The earlier cut went too far: a side contact barely bent at
        // all, which is the opposite complaint. The bend is now real, but it is no longer competing
        // with the shot direction, because a human volley leaves down the look ray (see the
        // look-ray block below) rather than being aimed at the goal - so lateral velocity curls the
        // ball around the line he chose instead of dragging it off a goalward one.
        public const float VolleyCurlMul      = 0.70f; // side-contact bend, x the set-piece curl
        public const float VolleyCurlTimeMul  = 1.0f;  // side-contact curl window, x AssistDuration
        public const float VolleySpinMul      = 0.75f; // side-contact spin, x the set-piece curl
        public const float VolleySidePaceMul  = 1.15f; // ...and drive a side contact FORWARD harder
        // Bottom contact: no chip, no knuckle. It just drives forward at a modest loft.
        public const float VolleyBottomLoft    = 0.42f; // up-vel as a fraction of launch (vs 0.95 chip)
        public const float VolleyBottomPaceMul = 1.10f; // ...with MORE forward pace, not less
        // Aim window, as fractions of the LIVE goal opening (match setup scales GoalWidth /
        // GoalHeight, so this follows it). Deliberately imprecise: the aim is never the corner,
        // it is a random point inside a window that always sits UNDER the crossbar and BETWEEN
        // the posts. Scatter shrinks with Shooting+Control but never reaches zero.
        public const float VolleyAimLatFrac  = 0.62f; // most of the half-goal the struck side may pull to
        public const float VolleyAimTopFrac  = 0.74f; // highest aim, x GoalHeight (stays under the bar)
        public const float VolleyAimLowFrac  = 0.22f; // lowest aim, x GoalHeight
        public const float VolleyAimScatter  = 0.22f; // random placement scatter at zero skill
        public const float VolleyAimTighten  = 0.65f; // how much full Shooting+Control shrinks that scatter
        // ---- Look-ray volley (a HUMAN striker only; an AI body has no look source) ----
        // A human volley aims down the CAMERA RAY, not at the goal. The window above still applies,
        // but it now recentres on where he is actually looking instead of on the goal mouth, and the
        // stat blend decides how hard the ray is pulled back onto the frame.
        public const float VolleyLookSlopeMax  = 0.85f; // hard cap on tan(pitch): looking near-straight up
                                                       // would otherwise ask for an infinite vertical
        public const float VolleyLookMinLoft   = 0.10f; // floor on that slope, so a flat-aimed volley still
                                                       // leaves the turf instead of grinding along it
        public const float VolleyLookOffFrame  = 6f;    // metres past the post at which the ray is judged to
                                                       // MISS the goal entirely, so the on-frame clamp is
                                                       // skipped and he is allowed to blaze it wide.
                                                       // SetPieceTaker.LookAimPoint's away path pushes >=30 m
                                                       // laterally, so this never trips on a genuine attempt.
        public const float VolleyBarClear      = 0.28f; // m under the crossbar the clamped aim tops out
        public const float VolleyCurveStatMul  = 1.6f;  // x on side-contact bend AND spin at full Shooting+
                                                        // Control, so an invested player genuinely bends it

        // ---- BICYCLE trajectory (Shooting + Control scaled) ----
        // The bike used to leave as a high looper the keeper caught every time: the physical
        // strike kept its full upward component and the trick bonus added another 0.55 of lift.
        // Trade vertical for goalward pace, more so the more invested the player is. The whole
        // vector is re-bounded at the strike's own ceiling afterwards, so this never out-hits a
        // normal shot - it only lowers the launch angle.
        public const float BicycleVKeepRaw     = 0.58f; // vertical kept at zero Shooting+Control
        public const float BicycleVKeepSkilled = 0.36f; // vertical kept at full Shooting+Control
        public const float BicyclePaceRaw      = 1.05f; // goalward pace mul at zero skill
        public const float BicyclePaceSkilled  = 1.12f; // goalward pace mul at full skill
        public const float BicycleBonusLiftRaw     = 0.26f; // trick-bonus up component at zero skill
        public const float BicycleBonusLiftSkilled = 0.12f; // trick-bonus up component at full skill
        // Trading vertical for pace lowers the AVERAGE bike; it does not BOUND the worst one. The
        // launch is a solve plus an AddForce bonus on top, so a steep contact could still clear the
        // bar. The last word is therefore geometric rather than statistical: BallController solves
        // the flight time to the goal-line plane and caps the rise so the ball arrives at most this
        // far under the crossbar. Drag only ever lengthens the real flight, so the cap errs low. On
        // target by construction, whatever the goal was scaled to in match setup.
        // Metres under the bar the flight must arrive. Compared against the ball's CENTRE, so it has
        // to exceed BallRadius (0.22) + the crossbar's own radius (0.07) or a capped shot arrives
        // exactly on the woodwork instead of under it.
        public const float BicycleBarClear = 0.32f;

        // ---- Dribble (discrete-touch ball control) ----
        // The model every real football game uses, and NOT a magnet. The ball is always a
        // free rigidbody: it rolls, decelerates, and can be intercepted at any instant. All
        // the carrier ever does is TOUCH it - once per stride, the same way a real player's
        // foot meets it - by setting a velocity that lands the ball where the next stride
        // wants it. Between touches nobody is holding anything, which is the whole reason a
        // spring leash felt wrong: a leash has no touches, so it has no rhythm, no error,
        // and nothing for a defender to poke away.
        //
        // Three paces, exactly like the console games:
        //   walk/jog       - short touches, ball glued near the feet, quickest turns
        //   sprint         - the KNOCK-ON: the ball is pushed a long way ahead and the
        //                    player runs onto it, so top speed costs you control
        //   close control  - the modifier key: shortest touches at reduced pace, for
        //                    beating a man in a phone box
        //
        // Capture: the ball must be near the feet, LOW (a served/airborne ball is never
        // eaten), and not arriving faster than the trap can cushion.
        public const float DribbleCaptureRadius     = 0.62f; // flat distance from the feet at which the ball is taken
                                                     // (measured from the PELVIS, so this is already about a
                                                     //  boot-length of reach; larger and the ball visibly
                                                     //  snaps in from arm's length, which is not a touch)
        public const float DribbleCaptureMaxSpeed    = 12f;  // ball must be slower than this to be taken at all (m/s)
        public const float DribbleCaptureApproachMax = 9f;   // ...and closing on the feet no faster than this (m/s)
        public const float DribbleMaxBallHeight     = 2.2f;  // ball centre above this many radii = airborne, no carry
        public const float DribbleLoseRadius        = 3.5f;  // ball further than this from the feet: possession lost
                                                             // (must clear a full sprint knock-on plus its over-hit)
        public const float DribbleTrapCaptureBonus   = 0.30f; // up to +0.30m capture radius with full Control, so even
                                                      // the best close-control player reaches under a metre

        // First touch. Whatever pace the ball arrives with is cushioned on contact, and how
        // dead that touch is comes straight off the Control stat: a raw build lets it bounce
        // away from them, a Control build kills it stone dead at their feet.
        public const float DribbleFirstTouchKeepRaw     = 0.55f; // fraction of arrival speed kept, zero Control
        public const float DribbleFirstTouchKeepSkilled = 0.13f; // ...at full Control
        public const float DribbleFirstTouchSettle      = 0.14f; // pause before the first pushing touch (reads as a trap)

        // Touch distance: how far in front of the feet the ball is knocked. Walk -> sprint,
        // then the Control stat pulls the whole range in (a Control build keeps it under
        // their studs at any pace).
        public const float DribbleNearDistance     = 0.72f;  // touch distance at a stand/walk, no Control
        public const float DribbleSprintDistance   = 2.35f;  // touch distance at full sprint, no Control (knock-on)
        public const float DribbleTrapTightenMax   = 0.55f;  // up to 55% shorter touches with full Control

        // Touch cadence. Derived from the gait so a touch lands on a stride, not on a timer
        // the animation knows nothing about: interval = pi / gaitCadence * StrideFrac, i.e.
        // one touch per step. Clamped so a standstill does not stall and a sprint does not
        // machine-gun.
        public const float DribbleTouchStrideFrac  = 1.0f;   // 1 = one touch per step (0.5 = per half step)
        public const float DribbleTouchIntervalMin = 0.15f;  // floor on the gap between touches (s)
        public const float DribbleTouchIntervalMax = 0.55f;  // ceiling (a near-stationary shuffle still taps it)

        // A touch also fires EARLY, off cadence, when the ball is no longer where it should
        // be - the ball has fallen level with the feet, or drifted out to one side. This is
        // what makes the carry self-correct instead of drifting away like a spring would.
        public const float DribblePushMinAhead     = 0.12f;  // ball less than radius+this in front: push it out now
        public const float DribbleSideTolerance    = 0.60f;  // lateral drift from the facing line before a corrective touch
        public const float DribbleSideToleranceFrac = 0.30f; // ...widened by this much per metre the ball is ahead

        // Touch velocity. The push is aimed at where the next stride wants the ball, then
        // scaled up a little for what rolling friction will eat on the way.
        public const float DribbleRollLossComp     = 1.16f;  // over-hit factor to cover roll-out losses
        public const float DribbleTouchMaxSpeed    = 14f;    // hard cap on a touch (never a pass)
        public const float DribbleTouchMinSpeed    = 1.1f;   // DEADBAND: below this the ball is already where the
                                                             // next stride wants it, so it is killed dead instead
                                                             // of nudged (else a standing player walks it away)
        public const float DribbleTouchHop         = 0.85f;  // small upward m/s on a touch (~2cm skip): a kick, not a slide
        public const float DribbleSpinScale        = 2.2f;   // rolling spin visual per m/s of ball speed

        // Touch ERROR, in degrees of aim scatter. This is the cost of pace and the value of
        // the Control stat: at full Control a sprinting carrier still keeps the ball, at zero
        // Control the same run sprays it. Sharp turns scatter it further (you are dragging
        // the ball across your own body).
        public const float DribbleTouchErrorDeg      = 14f;  // base scatter at zero Control
        public const float DribbleTouchErrorSpeedDeg = 8f;   // extra scatter at full sprint
        public const float DribbleTurnErrorDeg       = 11f;  // extra scatter on a fully sharp turn

        // Turning with the ball. Past TurnTightenDeg of facing change since the last touch
        // the push is shortened toward TurnTightenMul, so the ball is dragged around the body
        // instead of squirting off at the old angle.
        public const float DribbleTurnTightenDeg = 55f;      // facing change (deg) at which tightening is full
        public const float DribbleTurnTightenMul = 0.42f;    // touch-distance factor on a fully sharp turn

        // Close control (the modifier key): shortest touches, more of them, less pace, but
        // a much quicker turn. The trade real games make.
        public const float DribbleCloseDistMul     = 0.46f;  // touch-distance factor while held
        public const float DribbleCloseIntervalMul = 0.66f;  // touch-cadence factor (more touches)
        public const float DribbleCloseSpeedMul    = 0.68f;  // move-speed factor
        public const float DribbleCloseTurnMul     = 1.70f;  // facing-slew factor
        public const float DribbleCloseErrorMul    = 0.45f;  // scatter factor (deliberate touches are cleaner)

        // Shot on release (kick): the carried ball is launched in the aim/facing direction.
        public const float DribbleShotSpeed        = 17f;    // base release shot speed (m/s), scaled by ShotPowerMul
        public const float DribbleShotLift         = 0.16f;  // upward fraction added so it isn't a pure ground roll
        public const float DribbleRecaptureCooldown = 0.45f; // after a shot, don't re-grab the ball for this long

        // While carrying, the striker moves SLOWER and turns SLOWER by default; the Control
        // trap stat claws both back (a Control build dribbles nearly at full pace and turns
        // sharply, a raw build is ponderous with the ball). DribbleTightness (0..1) lerps
        // each penalty from its "no Control" value to "full Control".
        public const float DribbleMoveMulLow  = 0.72f;  // move-speed factor while dribbling, no Control
        public const float DribbleMoveMulHigh = 0.95f;  // move-speed factor while dribbling, full Control
        // Turn rate = how fast the facing yaw slews toward the mouse aim while carrying
        // (deg/sec). Low with no Control (ponderous), snappy with full Control.
        public const float DribbleTurnRateLow  = 260f;  // deg/sec facing slew while dribbling, no Control
        public const float DribbleTurnRateHigh = 680f;  // deg/sec facing slew while dribbling, full Control

        // ---- Scrimmage (full match: two goals, teams, AI, passing) ----
        // Chosen role + team size come from the pre-match screen.
        public enum ScrimRole { Outfield, Keeper }
        public static ScrimRole ScrimmageRole = ScrimRole.Outfield;
        public static int ScrimmagePerSide = 3;   // TOTAL players per side incl. keeper (3/5/11 => outfield = this-1)
        public static float ScrimmageMatchSeconds = 180f;   // match length (pre-match option); counts down to full time

        // ==================== SCRIMMAGE POSITIONS ====================
        // OWNER: menus/config. Read by PrematchUI's position picker and available to the multiplayer
        // lobby (PrematchUI.PositionPicker is static so both draw the identical grid). Adding a
        // position or reshaping a formation is a change HERE and nowhere else.
        //
        // SHIRT 0 IS ALWAYS THE KEEPER. That is already the multiplayer convention - the net match
        // derives a shirt as (slot < 4 ? slot : slot - 4) and calls shirt 0 the keeper on BOTH teams -
        // so single player is being brought onto it rather than the reverse. Nothing about a position
        // goes on the wire: a body's role is a pure function of (perSide, shirt), so every peer
        // computes the same answer from the slot it already holds.
        public enum ScrimPos { GK, LB, CB, RB, CM, CAM, LM, RM, LW, RW, ST }

        // The human's own shirt. Written by the pre-match position picker in single player, and from
        // the host-assigned slot in multiplayer. ScrimmageRole is DERIVED from it (see
        // ApplyScrimmageStatics) rather than being a second, independently-picked source of truth for
        // "am I the keeper" - which is what let a position and a role disagree.
        public static int ScrimmageShirt = 2;

        // One authored formation per roster size, indexed by shirt. Authored rather than generated
        // from a fill order, because a fill order puts the odd sizes somewhere silly: single player
        // offers 3/5/11 and multiplayer needs 2..4 (the 8-slot board caps it), so all of 1..11 are
        // written out. They grow into a 4-3-3: a centre back first, then width, then a second bank of
        // midfield, then wingers.
        static readonly ScrimPos[][] ScrimFormations =
        {
            new[] { ScrimPos.GK },                                                                     // 1
            new[] { ScrimPos.GK, ScrimPos.ST },                                                        // 2
            new[] { ScrimPos.GK, ScrimPos.CB, ScrimPos.ST },                                           // 3
            new[] { ScrimPos.GK, ScrimPos.CB, ScrimPos.CM, ScrimPos.ST },                              // 4
            new[] { ScrimPos.GK, ScrimPos.CB, ScrimPos.LM, ScrimPos.RM, ScrimPos.ST },                 // 5
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM, ScrimPos.ST },    // 6
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM, ScrimPos.CM,
                    ScrimPos.ST },                                                                     // 7
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM,
                    ScrimPos.CM, ScrimPos.ST },                                                        // 8
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM,
                    ScrimPos.CM, ScrimPos.LW, ScrimPos.ST },                                           // 9
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM,
                    ScrimPos.CM, ScrimPos.CAM, ScrimPos.LW, ScrimPos.ST },                             // 10
            new[] { ScrimPos.GK, ScrimPos.LB, ScrimPos.CB, ScrimPos.CB, ScrimPos.RB, ScrimPos.CM,
                    ScrimPos.CM, ScrimPos.CAM, ScrimPos.LW, ScrimPos.ST, ScrimPos.RW },                // 11
        };

        /// <summary>Formation for a roster size, indexed by shirt (0 = keeper). Sizes outside 1..11 clamp.</summary>
        public static ScrimPos[] Formation(int perSide)
            => ScrimFormations[Mathf.Clamp(perSide, 1, ScrimFormations.Length) - 1];

        /// <summary>
        /// What a shirt plays in this shape. An out-of-range shirt CLAMPS rather than throwing: a
        /// lobby can still hand out a shirt past the roster today (nothing gates shirt against
        /// perSide - that is the per-side cap owed to NetSession.SlotAllowed), and a UI label is not
        /// the place to crash over it. Clamping here hides nothing: the spawn path still throws.
        /// </summary>
        public static ScrimPos PositionOf(int perSide, int shirt)
        {
            var f = Formation(perSide);
            return f[Mathf.Clamp(shirt, 0, f.Length - 1)];
        }

        public static string PositionName(int perSide, int shirt) => PositionOf(perSide, shirt).ToString();

        /// <summary>
        /// Lowest shirt playing a position in this shape, or -1 when the shape has none (there is no
        /// LW at 3-a-side). Callers fall back to the striker, which is always the LAST shirt.
        /// </summary>
        public static int ShirtForPosition(int perSide, ScrimPos pos)
        {
            var f = Formation(perSide);
            for (int i = 0; i < f.Length; i++) if (f[i] == pos) return i;
            return -1;
        }

        /// <summary>The convention in one line, so no caller re-derives it as "index 0" and no other
        /// caller re-derives it as "the last one".</summary>
        public static bool KeeperShirt(int shirt) => shirt == 0;

        // Where a position lines up, as fractions: x of half-width (-1 = own left touchline), z of
        // half-length measured from the halfway line INTO OWN HALF (1 = own goal line). Team sign and
        // which end is "own" stay the caller's business.
        //
        // NOTHING READS THIS YET, stated plainly: the scrimmage spawn code lays players out by list
        // index and by parity into a back line and a forward line, which is why an 11-a-side kickoff
        // has depth but no shape. This is offered so the AI area adopts one table instead of
        // authoring a second one. Every z is strictly inside own half, so the whole table is legal at
        // a kickoff.
        static Vector2 PositionAnchorBase(ScrimPos pos) => pos switch
        {
            ScrimPos.GK  => new Vector2( 0.00f, 0.96f),
            ScrimPos.LB  => new Vector2(-0.74f, 0.62f),
            ScrimPos.CB  => new Vector2( 0.00f, 0.66f),
            ScrimPos.RB  => new Vector2( 0.74f, 0.62f),
            ScrimPos.CM  => new Vector2( 0.00f, 0.38f),
            ScrimPos.CAM => new Vector2( 0.00f, 0.22f),
            ScrimPos.LM  => new Vector2(-0.68f, 0.40f),
            ScrimPos.RM  => new Vector2( 0.68f, 0.40f),
            ScrimPos.LW  => new Vector2(-0.72f, 0.14f),
            ScrimPos.RW  => new Vector2( 0.72f, 0.14f),
            _            => new Vector2( 0.00f, 0.08f),   // ST
        };
        // Fraction of half-width two players sharing a position are splayed apart by.
        const float PositionSplay = 0.22f;

        /// <summary>Anchor for a specific shirt, with duplicated positions splayed so the two centre
        /// backs (and the two centre mids) do not stand on each other.</summary>
        public static Vector2 PositionAnchor(int perSide, int shirt)
        {
            var f = Formation(perSide);
            shirt = Mathf.Clamp(shirt, 0, f.Length - 1);
            var pos = f[shirt];
            Vector2 a = PositionAnchorBase(pos);
            int n = 0, k = 0;
            for (int i = 0; i < f.Length; i++) { if (f[i] != pos) continue; if (i == shirt) k = n; n++; }
            if (n > 1) a.x += Mathf.Lerp(-PositionSplay, PositionSplay, k / (float)(n - 1));
            return a;
        }

        // ==================== AI DIFFICULTY ====================
        // OWNER: menus/config. Single player picks a tier on the pre-match screen; MULTIPLAYER IS
        // FIXED AT NORMAL and is written on every peer by ApplyScrimmageStatics, so it cannot desync.
        // Difficulty is not on the wire and nothing about it can be - a client that disagreed would
        // be running a different sim behind the same snapshots.
        //
        // A tier may change WHEN and HOW WELL an AI acts. It may NOT hand one extra top speed, extra
        // reach, or knowledge it could not have. PaceUse is a FRACTION of the pace the body already
        // owns (see AiPace, 0.80x - 1.24x), never a multiplier above it, which is that rule written
        // into the units rather than left as an intention.
        //
        // NORMAL IS THE BALANCE ANCHOR: the scrimmage keeper is tuned to save 60-70% of shots that are
        // on target and not deflected AT THIS TIER, and the shooting work is tuned to the same number,
        // so a later change to one cannot silently undo the other.
        //
        // HOW TO MEASURE IT IN-ENGINE, with no new instrumentation: set the match clock to 10 min,
        // play out a scrimmage, and read the per-player stat table the match-rating code already
        // keeps. StatSaveShotWindow (2.5 s) plus StatSaveMinBallSpeed (6 m/s) already gate a keeper
        // touch into a SAVE, and a shot is banked whether it is saved or not. Take
        // saves / (saves + goals conceded) for BOTH keepers. Off-target shots never reach either
        // counter, so that ratio is already the on-target one. Two 10-minute 3-a-side matches is
        // roughly 40-60 on-target shots, which is about +/-6 points of confidence - enough to tell
        // 55% from 70% and not enough to chase a single point.
        public enum AiDifficulty { None = 0, Easy = 1, Normal = 2, Hard = 3, Insane = 4 }

        /// <summary>
        /// One row of the difficulty table. All 0..1 except the delay, which is seconds.
        ///   ReactionDelay  s an AI waits before responding to something it can see (ball struck,
        ///                  carrier turned). The biggest lever and the fairest one - it costs an AI
        ///                  time, not capability.
        ///   Decision       quality of the choice made: 1 takes the best option, 0 takes an also-ran.
        ///   ErrorRate      scale on the scatter a shot, pass, clearance or touch carries.
        ///   FirstTouch     how cleanly an arriving ball is brought under control.
        ///   PaceUse        fraction of the body's OWN top speed it actually uses.
        /// </summary>
        public readonly struct AiTuning
        {
            public readonly float ReactionDelay, Decision, ErrorRate, FirstTouch, PaceUse;
            public AiTuning(float react, float decision, float error, float touch, float pace)
            { ReactionDelay = react; Decision = decision; ErrorRate = error; FirstTouch = touch; PaceUse = pace; }
        }

        // Same shape as the keeper ladder below, indexed by AiDifficulty.
        //                            react  decide  error  touch   pace
        static readonly AiTuning[] AiTable =
        {
            new AiTuning(9.99f, 0.00f, 1.00f, 0.00f, 0.00f),   // None   - built, takes no decisions
            new AiTuning(0.55f, 0.35f, 0.65f, 0.35f, 0.75f),   // Easy
            new AiTuning(0.32f, 0.60f, 0.40f, 0.60f, 0.88f),   // Normal - the balance anchor
            new AiTuning(0.18f, 0.80f, 0.22f, 0.80f, 0.96f),   // Hard
            new AiTuning(0.09f, 0.95f, 0.10f, 0.95f, 1.00f),   // Insane
        };

        public static AiDifficulty AiLevel = AiDifficulty.Normal;
        /// <summary>The resolved row for the current tier. Read this; never index AiTable.</summary>
        public static AiTuning Ai => AiTable[Mathf.Clamp((int)AiLevel, 0, AiTable.Length - 1)];
        /// <summary>False only at None. None's 9.99 s reaction delay already stops anything that
        /// honours the delay; this is the explicit gate for anything that does not.</summary>
        public static bool AiActive => AiLevel != AiDifficulty.None;

        // The five named steps, shared by the AI ladder AND the keeper ladder, because they ARE the
        // same five steps. PrematchUI's local KeeperNames/KeeperVals copies were deleted in favour of
        // these; HostSetupUI still carries its own copy of the names.
        //
        // Easy is deliberately left at 0.25 and NOT nudged up. It trips three keeper gates as they
        // stand ("ability > 0.25", "ability <= 0.3", and KeeperClaimMinAbility 0.30), which is why
        // Easy currently never dives, never rushes and never claims - two of five tiers are statues.
        // Moving this to 0.31 would tiptoe past that instead of fixing it. The gates are the bug.
        public static readonly string[] AiLevelNames   = { "None", "Easy", "Normal", "Hard", "Insane" };
        public static readonly float[]  AiLevelAbility = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        /// <summary>Nearest ladder index to a raw 0..1 ability, so a value left over from the old
        /// slider (or from a future retune of these steps) still lands on a named button.</summary>
        public static int NearestAiLevel(float ability01)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < AiLevelAbility.Length; i++)
            {
                float d = Mathf.Abs(AiLevelAbility[i] - ability01);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ==================== SCRIMMAGE STATIC RESET ====================
        // Base values the mutable pre-match statics reset to, named once so the menu's "1.00x" and
        // the reset below cannot drift apart the way GoalWidth already did.
        public const float GoalWidthBase = 7.32f, GoalHeightBase = 2.44f;
        public const float StrikerMoveSpeedBase = 3.8f, KeeperStrafeSpeedBase = 5.5f;

        /// <summary>
        /// Writes EVERY mutable static a scrimmage reads, so nothing is inherited from the mode played
        /// before it. Called from PrematchUI.Apply (single player) and from the networked scrimmage
        /// branch in GameBootstrap. It lives HERE, and not inlined at both sites, because the
        /// goal-size leak was caused by exactly that duplication - fixing it by adding four more lines
        /// to each of two copies reproduces the cause.
        ///
        /// The leaks this closes, measured against the sliders that write them:
        ///   GoalWidth/GoalHeight  set-piece + accuracy prematch write up to 1.5x, i.e. a 10.98 m goal
        ///                         in a scrimmage - read by goal detection, keeper positioning and AI aim.
        ///   BallSpeedMul          0.5x - 2x, read by every ballistic launch.
        ///   KeeperAbility         0 - 1 from four other modes' keeper ladder. Scrimmage has no picker
        ///                         of its own, so a "None" free kick left BOTH scrimmage keepers as
        ///                         statues, and the net branch never wrote it at all.
        ///   StrikerMoveSpeed      0.5x - 1.8x, i.e. 1.9 - 6.84 m/s: locomotion, gait and dribble.
        ///   KeeperStrafeSpeed     0.5x - 1.8x, and KeeperJumpVel 0.6x - 1.6x. Keeper-mode-only
        ///                         sliders that the scrimmage AI keeper's track speed, dive speed and
        ///                         dive height all read.
        ///
        /// NOT written, because no scrimmage code path reads them: ShotDifficulty, ServeInterval, the
        /// wall / set-piece placement statics, the challenge-mode timers, FreeplayDelivery. GoalDepth
        /// is left alone too - it is mutable but nothing anywhere assigns it, and resetting statics
        /// nobody mutates is noise that hides the ones that matter.
        ///
        /// localShirt: pass the host-assigned shirt in multiplayer (slot % 4 on the capped two-team
        /// board), or leave it at -1 in single player, where the position picker has already written
        /// ScrimmageShirt. At -1 in a networked match the role is left exactly as the caller set it,
        /// which is the spectator case.
        /// </summary>
        public static void ApplyScrimmageStatics(bool networked, int localShirt = -1)
        {
            GoalWidth  = GoalWidthBase;
            GoalHeight = GoalHeightBase;
            BallSpeedMul = 1f;
            StrikerMoveSpeed  = StrikerMoveSpeedBase;
            KeeperStrafeSpeed = KeeperStrafeSpeedBase;
            KeeperJumpVel     = KeeperJumpVelBase;

            // MULTIPLAYER IS FIXED AT NORMAL, written on every peer so it cannot desync.
            if (networked) AiLevel = AiDifficulty.Normal;
            KeeperAbility = AiLevelAbility[Mathf.Clamp((int)AiLevel, 0, AiLevelAbility.Length - 1)];

            // Role is DERIVED from the shirt, so a position and a role can never disagree.
            if (localShirt >= 0) ScrimmageShirt = localShirt;
            ScrimmageShirt = Mathf.Clamp(ScrimmageShirt, 0, Mathf.Max(0, ScrimmagePerSide - 1));
            if (localShirt >= 0 || !networked)
                ScrimmageRole = KeeperShirt(ScrimmageShirt) ? ScrimRole.Keeper : ScrimRole.Outfield;
        }

        // The scrimmage pitch is its OWN square-ish field centred on origin, sized to the
        // team count, with a goal at each end (+Z and -Z) and walls all round. Independent
        // of the single-goal training arena so nothing else has to change.
        //
        // Sized so the GOAL does not dominate the pitch. This has been wrong in both directions.
        //
        // Originally the three sizes were one 11-a-side rectangle uniformly shrunk, which left two or
        // four outfielders each covering most of a full pitch. Correcting that on area-per-player gave
        // 36 x 25 and 50 x 30 - and those turned out to be far too small to defend, because the GOALS
        // never shrank with them. SimConfig.GoalWidth is a global read by 20-odd files, so a 3-a-side
        // pitch was 25 m wide with a regulation 7.32 m goal in it: 29% of the width, more than a keeper
        // can cover, and every attack that reached the box scored.
        //
        // These are bigger than BOTH previous sets. The number that actually governs how easily a goal
        // is scored is the goal's share of the pitch width, so that is what is being tuned:
        //     3 a side   54 x 36   goal = 20% of the width
        //     5 a side   76 x 48   goal = 15% of the width
        //    11 a side  105 x 68   goal = 11% of the width   (FIFA recommended, unchanged)
        // Small-sided stays deliberately more goal-dominant than 11-a-side - it should be higher
        // scoring - but 20% is a goal a keeper can work, where 29% was not.
        //
        // If scoring is STILL too easy, the more direct lever is the goal itself: GoalWidth is a mutable
        // static that PrematchUI already writes, so scrimmage could scale it per format instead of
        // growing the pitch further. That is a bigger change (it moves keeper dive tuning and aim assist
        // with it) and is deliberately not done here.
        public static float ScrimHalfLength(int perSide) => perSide >= 11 ? 52.5f : perSide >= 5 ? 38f : 27f;
        public static float ScrimHalfWidth(int perSide)  => perSide >= 11 ? 34f   : perSide >= 5 ? 24f : 18f;

        // Player (human) attacks +Z and defends -Z, matching the Striker/KeeperController
        // hardcoded facing. The team attacking +Z is "Home"; attacking -Z is "Away".
        public const float ScrimKickoffBallHeight = 0.3f;
        public const float ScrimKickoffFreeze     = 1.2f;   // ball/scoring frozen this long after kickoff/goal
        // Out-of-play safety: if the ball somehow sits nearly still against a wall for this
        // long, drop it back to a sensible in-play spot so a match can't stall.
        public const float ScrimStuckTime         = 4f;
        public const float ScrimStuckSpeed        = 0.5f;   // "nearly still" threshold (m/s)
        // The pitch is SEALED. Walls this tall plus a lid, and the goal-mouth gaps in the end
        // walls are filled in above the crossbar - a ball over the bar used to fly straight out
        // through the gap and leave the pitch entirely.
        public const float ScrimWallHeight        = 13f;
        public const float ScrimGoalGapPad        = 0.4f;   // clearance either side of the posts in the end wall

        // Passing (controlled outfielder). A pass picks the teammate nearest the aim ray.
        public const float PassGroundSpeed   = 12f;   // ground (rolled) pass base speed (m/s), scaled by PassPowerMul
        public const float PassLoftedSpeed   = 13f;   // lofted (chipped) pass base speed
        public const float PassLoftedArc     = 0.55f; // upward fraction of a lofted pass (higher = floatier)
        // Where a launched pass spawns relative to the passer, so it clears their own body
        // instead of rising into their torso (which flattened lofted passes to the ground).
        // Superseded by PassSpawnFromBody: the spawn is measured from the PASSER now, not from the
        // ball, because a look-ray pass can point behind and this offset then landed inside his legs.
        // Kept only so a tuner searching for it finds the reason it stopped being read.
        public const float PassSpawnLift     = 0.7f;   // extra metres up for a lofted pass
        public const float PassAimConeDot    = 0.2f;  // teammate must be within this cone of the aim to be picked
        public const float PassMaxRange      = 45f;   // don't target teammates further than this
        public const float PassLeadFrac      = 0.25f; // lead a moving target by this fraction of range/speed
        // Hold Q/E to charge: a tap is a soft pass, a full hold a hard/fast one. The charge
        // fraction (0..1 over PassMaxCharge seconds) scales speed between these bounds.
        public const float PassMaxCharge     = 0.6f;   // seconds of hold to reach full power
        // CAP AND WAIT, which is what FIFA actually does: the bar fills, then SITS at full until the
        // button comes up. Overholding is free, so a player who fills the bar while looking for a
        // runner is not punished for it, and the pass always leaves on a deliberate release.
        //
        // It also removes a whole class of network hazard. Firing at full meant the host picked the
        // moment from its own accumulated timer, so a mispredicted client bar could commit a pass, and
        // a stale repeated input frame could fire one on its own (which is why NetInputSource.Fresh
        // exists - that guard is still load-bearing for the CHARGE, just no longer for the commit).
        // Now nothing fires without a release edge, and the release edge is the one thing the wire
        // supplies reliably. Set true to go back to firing at full; the bar and the fill are identical
        // either way, only the commit changes.
        public const bool  PassAutoFireAtFull = false;

        // ---- Look-ray pass ranges (metres) ----
        // The bar's charge picks the pass DISTANCE along the look ray: a tap is a short one, a full bar
        // is the longest that type plays. Bands are deliberately inside the pitch - the smallest
        // scrimmage field is 36 x 25 m (SimConfig.ScrimHalfLength/Width at perSide < 5, and networked
        // scrimmage is capped to 2..4 a side) - because the box is SEALED and an aim point past the
        // wall is a pass into a wall. ScrimmageGame clamps the final aim into the arena as well.
        public const float PassRangeGroundMin = 4f;
        public const float PassRangeGroundMax = 22f;
        public const float PassRangeAirMin    = 6f;
        public const float PassRangeAirMax    = 26f;
        // The chip is SHORT by definition: it exists to drop a ball onto a team-mate's head or bicycle,
        // not to cover ground. The cap holds even for a maxed power build.
        public const float PassRangeChipMin   = 4f;
        public const float PassRangeChipMax   = 9f;
        public const float PassRangeChipCap   = 11f;
        // Apex height of a chip above its launch, which is what makes it a chip rather than a lob: the
        // shape is FIXED, so distance changes only how flat it looks, never how high it goes, and the
        // receiver can time it. 3.6 m clears a 2.9 m standing reach with enough margin to drop steeply
        // onto a head. It is deliberately not higher: at 6 m the ball arrives too steep and too fast to
        // volley or head, which is the entire point of the pass.
        public const float PassChipApexY      = 3.6f;
        // Spawn distance from the PASSER'S BODY, not from the ball. The carried ball sits ~0.72 m ahead
        // of the body (DribbleNearDistance), so the old "ball position + 0.6 m along the pass" was fine
        // only while the aim was roughly forward. A look ray can point BEHIND, and that offset then put
        // the spawn inside the passer's own legs and fired the pass into them.
        public const float PassSpawnFromBody = 0.85f;
        // Seconds to ease the run's heading back to the camera after an aim ends. See Striker.LockRun:
        // while a pass is aimed the run holds its own heading, and resuming instantly would kick it
        // sideways by however far the aim had swung.
        public const float PassAimBlendTime = 0.18f;
        // Minimum charge a FIRST-TIME pass is credited with. The ball only just arrived, so there was
        // no window to fill the bar, and without a floor every first-time ball is a minimum-range dink
        // no matter how the player was set. Not a full bar either - striking it without settling it
        // should still cost you the long option.
        public const float PassFirstTimeChargeFloor = 0.45f;
        public const float PassChargeMinMul  = 0.85f;  // drive factor for a bare tap (distance already sets the pace)
        public const float PassChargeMaxMul  = 1.30f;  // drive factor at full charge (a narrow band, not a power bar)
        // Accuracy scatter: at PassAccuracyMul = 1 (no Passing nodes) a pass is knocked off
        // its intended line by up to this angle + a power wobble; investment shrinks it to
        // ~0 (Maestro perk = pinpoint). Harder-charged passes also scatter a touch more.
        // CUT FROM 22. That figure was tuned when the aim point was a TEAMMATE (Passing.BestTarget):
        // scatter is a yaw error about the aim, so with a body at the far end a 22 degree miss often
        // still found somebody, and the cone was really a "who receives it" roll. The aim is now a raw
        // look ray, so the same cone is a pure positional error with nothing to catch it - at
        // PassAccuracyMul 1 (an uninvested build, since it is SkillTree.Mul("passacc") = 1 + sum) that
        // was +/-27 degrees on a 14 m pass, about 6.4 m of lateral miss, and passing was uncompletable.
        // At 9 the same build misses by about 2.7 m, which reads as a misplaced pass rather than a
        // random one, and a fully invested build still goes where it is pointed.
        public const float PassScatterMaxDeg = 9f;     // max aim error at low passing (deg)
        public const float PassPowerWobble   = 0.18f;  // +/- fraction of speed randomised at low passing

        // ---- Pass weight (the FIFA-style model; see Passing.cs) ----
        // PACE COMES FROM DISTANCE. A flat launch speed is the single reason the old passing
        // felt wrong: 12 m/s dies short on a 30 m switch and blasts a 6 m square ball past the
        // receiver. Ground pace = base + per-metre, then the hold trims it.
        public const float PassGroundBase      = 5.5f;  // m/s floor before distance is added
        public const float PassGroundPerMetre  = 0.62f; // extra m/s per metre of pass length
        public const float PassGroundMin       = 6f;    // never limper than this
        public const float PassGroundMax       = 26f;   // never a shot
        public const float PassGroundLift      = 0.35f; // tiny lift so it clears turf seams and rolls true
        // Lofted passes are solved to LAND on the target, so the knob is TIME OF FLIGHT, not
        // speed: longer chips hang longer, a tap floats and a hold drives it flatter.
        public const float PassLoftBaseTime    = 0.42f; // seconds of hang before distance is added
        public const float PassLoftTimePerMetre = 0.030f;
        public const float PassLoftFloatMul    = 1.30f; // tap: floatier, higher arc
        public const float PassLoftDrivenMul   = 0.78f; // full hold: driven, flatter, arrives sooner
        public const float PassLoftTimeMin     = 0.45f;
        public const float PassLoftTimeMax     = 1.9f;
        // Error grows with what actually makes a pass hard: range, pressure, and hitting it
        // first time. Accuracy closes the cone; Maestro shuts it.
        public const float PassScatterPerMetre     = 0.012f; // +1.2% of the cone per metre
        public const float PassScatterPressure     = 0.6f;   // +60% of the cone when fully closed down
        public const float PassFirstTimeScatterMul = 1.7f;   // hitting it without settling it costs accuracy
        public const float PassFirstTouchRadius    = 1.9f;   // ball within this of the feet = a first-time pass is on
        public const float PassPressureRadius      = 3.5f;   // an opponent inside this is pressure
        // Target choice. Weighs where you are pointing, forward progress, the receiver's
        // space, range fit, and whether the lane is blocked.
        public const float PassOpenRadius     = 6f;    // receiver space is "open" at this much room
        public const float PassLaneRadius     = 1.1f;  // a defender this near the line blocks it
        public const float PassIdealRange     = 14f;   // best-fitting pass length
        public const float PassRangeFalloff   = 26f;   // how fast the range fit decays either side
        public const float PassWeightAlign    = 1.0f;
        public const float PassWeightForward  = 0.8f;
        public const float PassWeightOpen     = 0.9f;
        public const float PassWeightRange    = 0.5f;
        public const float PassWeightLane     = 0.6f;
        public const float PassMinScore       = 0.35f; // below this there is no pass on
        public const float PassLeadMul        = 0.9f;  // fraction of the flight-time lead actually applied
        // Through balls: played into the grass ahead of a runner instead of at their feet.
        public const float PassThroughSpeedMin = 2f;    // receiver must actually be running
        public const float PassThroughLeadMul  = 0.55f; // metres ahead per m/s of their run
        public const float PassThroughSpaceMin = 4f;    // that space must be this clear of defenders
        public const float PassThroughBonus    = 1.1f;  // preference for a ball into space over one to feet

        // Auto-switch: control the teammate nearest the ball (outfield role). A manual switch key
        // cycles too. SwitchLockout (0.6 s) was deleted with the rest of the dead constants - nothing
        // read it, so the "brief lockout stops rapid flip-flopping" it documented never existed. If
        // flip-flopping shows up, it needs writing, not restoring.

        // Scrimmage LMB/RMB airborne shot (set-piece-style arc, no controllable spin).
        public const float ScrimLoftAngleDeg = 26f;    // launch elevation of a scrimmage deliberate shot
        public const float ScrimLoftMaxVy    = 7.5f;   // cap on the upward component so it can't balloon straight up

        // Outfield AI.
        public const float AiOutfieldSpeed    = 5.0f;  // base run speed for AI outfielders (keeps pace with play)
        // PER-PLAYER PACE. Every AI outfielder used to run this speed exactly, which is why pace was
        // worth nothing defensively: nobody could out-run you and you could not be out-run, whatever
        // either of you had invested. Each body now carries its own multiplier, so a quick winger can
        // pull away from a slow centre back and your own pace decides whether you get back.
        public const float AiPaceMin = 0.80f;   // 4.0 m/s
        public const float AiPaceMax = 1.24f;   // 6.2 m/s

        /// <summary>
        /// A body's pace multiplier, DERIVED from its team and shirt number rather than rolled. Two
        /// reasons it has to be deterministic: the host and every client build their own copies of the
        /// same squad, so a random draw would have the same player running at two speeds on two
        /// machines and the puppets fighting their snapshots; and a squad that re-rolls its pace every
        /// kickoff is not a squad. Keepers are left at 1 - they barely run, and a slow one just looks
        /// broken.
        /// </summary>
        public static float AiPace(int team, int shirt, bool keeper)
        {
            if (keeper) return 1f;
            // Small stable hash. The odd multipliers keep neighbouring shirt numbers apart, so a team
            // gets a spread rather than a gradient.
            int h = (team * 131 + shirt * 71 + 29) & 0xFF;
            return Mathf.Lerp(AiPaceMin, AiPaceMax, h / 255f);
        }
        public const float AiChaseStopDist    = 0.6f;  // stop closing when this near the ball
        public const float AiShootRange       = 20f;   // shoot when this close to the target goal with the ball
        public const float AiSupportSpread    = 7f;    // how far off-ball teammates spread from the carrier
        // NOT dead, despite being removed alongside three that were: Footballer.LaneClear forwards
        // this to Passing.LaneClear, so it has exactly one live caller. Deleting it broke the build.
        // Verified by grep at restore time: AiKickBoneImpulse / AiCarryNudge / AiPassLeadTime had zero
        // callers and are correctly gone; this one had one.
        public const float AiLaneCheckRadius = 1.1f; // a pass lane is blocked if an opponent is within this of the line
        public const float AiKickCooldown     = 0.35f; // min seconds between AI touches (flow without ping-ponging)
        public const float AiSeparationRadius = 3.8f;  // AI teammates keep at least this far apart
        // Smarter striker AI: dribble-carry toward goal, corner-aware arced shots, lane-checked passing.
        public const float AiCarrySpeed     = 5.6f;  // run speed while carrying the ball (a touch above base)
        // The AI carries the ball with the SAME touch model as the human (Dribble.Touch), so
        // a bot's dribble rolls free between touches and can be intercepted mid-roll exactly
        // like yours. These two give the bots a fixed mid-tier "Control stat" and a little aim
        // scatter of their own, since a Footballer has no PlayerProfile behind it.
        public const float AiDribbleTightness = 0.55f;  // bots' effective Control level, 0..1
        public const float AiTouchErrorDeg    = 7f;     // bots' per-touch aim scatter (deg)
        public const float AiDefenderAvoid  = 3.0f;  // steer the carry around an opponent within this range
        public const float AiShotScatter    = 1.1f;  // metres of aim scatter at the goal (keeps the AI beatable)
        // AiPassLeadTime and AiLaneCheckRadius are gone. Nothing read the lead time at all, and the
        // radius was read only by Footballer.LaneClear - itself a one-line wrapper nothing called.
        // Live pass-lane checks go through Passing.LaneClear with PassLaneRadius (1.1, the same
        // number) instead, so there is one radius rather than two that happened to agree.
        public const float AiPassAccuracy   = 0.62f; // bots' effective Passing stat, 0..1 (they misplace passes too)
        // (A networked player's passing stats used to be substituted here with a neutral 1.5 accuracy
        // and 1.0 power, because nothing carried skill data. They are on the wire now as a Passing node
        // mask - see SkillTree.PackPassing and NetSession.PassStatsForSlot - so both constants are gone
        // rather than left as a second live path that reads like the gap is still open. The substitute
        // was worse than it looked: 1.5 handed every client 0.59 of an accuracy scale they had not
        // bought, AND deleted the stat entirely for anyone who had maxed it, since Accuracy01 clamps at
        // 1 so a maxed 1.86 build and the 1.5 substitute produced the identical pass.)
        public const float AiPassCharge     = 0.5f;  // how firmly a bot strikes a pass (0 = weighted, 1 = driven)
        public const float AiShootConeDot   = -0.2f; // only shoot when facing roughly goalward (gdir . attackZ >= this)

        // Tackling / ball-winning. A tackle is a short forward lunge; if it reaches the ball
        // it dispossesses the carrier (kills their dribble) and knocks the ball loose.
        public const float TackleLunge     = 6.5f;  // forward lunge velocity of the tackler
        public const float TackleReach     = 1.6f;  // distance to the ball at which the tackle wins it
        public const float TackleCooldown  = 0.9f;  // seconds before the same player can tackle again
        public const float TackleKnock     = 4.5f;  // how hard the won ball is knocked away from the carrier
        // INSIDE TackleReach, deliberately. This was 2.2 against a TackleReach of 1.6, and once the
        // steal became a contest (Dribble.ContestTackle) that 0.6 m band was 100% wasted: the bot
        // lunged, the contest hard-gated it as TOO FAR, and it could never win. Measured live at 2.2:
        // 18 AI attempts, 1 won, 6% - against a 34% design target - because most attempts were fired
        // from outside the gate. The AI resolves its contest at the INSTANT it lunges (TryTackle), with
        // no arrival window like the human's, so the trigger has to sit where a win is possible.
        public const float AiTackleRange    = 1.55f; // an AI defender lunges when this close to the ball
        // How long an armed AI lunge gets to physically close the last bit of distance before its
        // contest resolves. Added after AiTackleRange 1.55 alone still measured 0/39 then 1/15 wins:
        // TryTackle used to lunge and contest in the SAME frame, so ContestTackle's timing term always
        // saw "committed from the edge of reach", its worst case, on every attempt. 0.35 s mirrors the
        // human's ResolveTackleWindow (0.4 s) and gives TackleLunge (see below) time to actually carry
        // him in before the geometry is judged.
        public const float AiTackleWindow   = 0.35f;

        // Knockdowns: a tackled player (or one caught by a slide tackle) falls over, goes
        // limp for a moment, then gets back up.
        public const float KnockdownTime    = 1.4f;  // seconds down before recovering
        public const float KnockdownImpulse = 5.5f;  // shove velocity applied to the felled player
        public const float KnockdownSpin    = 6f;    // tumble spin (deg/s about a horizontal axis)
        // Slide tackle: holding BOTH legs (LMB+RMB) while moving fast into an opponent
        // fells them (and the slider). It connects within this range at this min speed.
        public const float SlideTackleRange  = 1.7f;  // contact distance to the target
        public const float SlideTackleMinSpeed = 3.5f; // must be moving at least this fast to count as a slide
        public const float SlideTackleCooldown = 1.2f;

        // Diving header contact: a body in MID-FLIGHT of a dive that passes this close to an
        // opponent fells them - the same knockdown a slide tackle applies. Only the victim is
        // felled: the diver is already going down by definition (the dive lands belly-first).
        public const float DiveHeaderKnockRange = 1.5f;   // contact distance, pelvis to pelvis

        // ---- Post-goal replay ----
        // On-screen replay duration = ReplayWindow / ReplaySlowMul. 4s of real action played
        // at 0.36x slow-mo = ~11s on screen, all slowed. The 4s window (up from 2s) starts ~2s
        // earlier so the replay captures the PASS/CROSS + build-up, not just the shot.
        public const float ReplayWindow   = 4f;     // seconds of action buffered for the replay
        public const float ReplaySlowMul  = 0.36f;  // playback speed (0..1); 4/0.36 = ~11s watched
        // Live delay after the goal before the replay freezes + rolls. Physics keeps running
        // (and the recorder keeps buffering) during it, so the hold ends the captured 4s window
        // AFTER the ball crosses the line. At 1.3s the window is roughly [goal-2.7s .. goal+1.3s]
        // - it opens on the pass/build-up and closes on the ball settling in the net.
        public const float ReplayHold     = 1.3f;

        // ---- Networking (host-authoritative snapshot sync) ----
        public const float NetSnapshotInterval = 0.05f;  // host broadcasts ~20 snapshots/sec
        public const float NetInterpRate       = 14f;    // client puppet/ball lerp sharpness (1/s) - legacy fallback
        // Remote bodies are rendered this far in the PAST, interpolating between the two buffered
        // snapshots bracketing (now - delay). ~2 snapshot intervals so a late/dropped packet has a
        // neighbor to interpolate to instead of teleporting. The local player is predicted, not delayed.
        public const float NetInterpDelay      = 0.1f;   // 100 ms interpolation delay
        // Client server-reconciliation of the local predicted body (bounded error correction, not
        // rollback - the ragdoll isn't re-simulatable). Error below the deadzone is expected
        // prediction lag and ignored; a moderate error eases back at ReconcileRate/sec; an error
        // past ReconcileSnap is a real misprediction and is hard-snapped.
        public const float ReconcileDeadzone   = 0.35f;  // m of allowed predicted-vs-authoritative drift
        public const float ReconcileRate       = 6f;     // fraction/sec eased back for a moderate error
        public const float ReconcileSnap       = 2.5f;   // m error above which we hard-snap

        // ---- Skill-tree capstone perk magnitudes ----
        public const float CannonCapMul     = 1.25f;  // Cannon: raises the shot-speed ceiling
        public const float ImmovableMassMul = 1.6f;   // Immovable: extra effective mass (push resistance)
        public const float AfterburnerMul   = 1.30f;  // Afterburners: extra sprint speed on top
        public const float AerialPaceKeep   = 0.5f;   // Aerial: header keeps this fraction of vertical (vs HeaderVerticalKeep)
        public const float AerialGoalBias   = 0.95f;  // Aerial: header steers harder to goal (vs HeaderGoalBias)

        // ---- Headers (head contact). Low base power/accuracy; the Heading tree ramps
        //      both up noticeably (HeaderPowerMul/HeaderAccuracyMul from the profile). ----
        public const float HeaderPowerMul = 1.3f;    // extra power vs a normal strike (low base)
        public const float HeaderSwerve = 3f;        // added swerve (spin + lateral curl) - minimal by default
        public const float HeaderAccuracyMul = 1.35f; // base goal-ward steer on a header (low; Heading tree adds more)
        // Fraction of the header power+accuracy BOOST (the part above 1.0) applied on a GROUNDED
        // header; an airborne (jumped) header gets the full boost. Rewards timing a jump.
        public const float GroundedHeaderBoostFrac = 0.25f;
        // A header REDIRECTS the ball onto a goal-ward horizontal line (not just faster
        // in its old direction), so even a glancing touch flies fast toward goal.
        public const float HeaderGoalBias = 0.85f;   // 0..1: how strongly it aims at goal
        public const float HeaderMinSpeed = 15f;     // floor horizontal speed off a header (m/s)
        public const float HeaderVerticalKeep = 0.35f; // fraction of incoming vertical kept (stays flat)

        // Hard floor on a header's outgoing PITCH, in degrees below horizontal, after a species'
        // HeaderAction.DownDeg tilt is applied. The tilt is there so a body that heads from a
        // standing height a person has to jump for drives the ball down instead of flat and long,
        // but a ball that arrived falling steeply is already pointed down, and tilting that further
        // just buries it in the turf a metre in front of the animal. 32 deg still reads as a firm
        // downward header off a 2 m contact and still clears the ground well short of the keeper.
        public const float HeaderMaxDiveDeg = 32f;
    }
}
