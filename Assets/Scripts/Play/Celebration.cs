using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Procedural celebration emotes for a player ragdoll. The emote wheel calls Play(id);
    /// this drives a short scripted pose/animation on the ActiveRagdoll (pose overrides,
    /// and for the acrobatic ones a body spin / hop), then hands control back.
    ///
    /// While an emote runs, the owning controller (Striker) suspends its own control via
    /// Playing so the two don't fight. Emotes are cosmetic - they don't touch the ball.
    /// </summary>
    public class Celebration : MonoBehaviour
    {
        // The wheel emotes. Names are shown on the wheel. Enum values are the NETWORK id (a
        // byte on the wire), so only ever APPEND new emotes - never reorder/insert.
        public enum Emote
        {
            FistPump, KneeSlide, Backflip, Wave, TPose, Griddy, Bow, PushUps, Robot,
            Dab, Floss, Clap, Salute, HeartHands, Shrug, MuscleFlex, Point,
            Sprinkler, HandsUp, Facepalm, Charleston, Cheer, Twirl, Disco, Thinker,
            // Newest wheel (shown 2nd in the order, but appended here so wire ids never shift)
            Twerk, FishFlop, Moonwalk, Wave2, Crip, Vibe, Kick, Slide2,
        }

        // Emote pages, cycled by the wheel's left/right arrows. The "fun/new" wheel is shown
        // SECOND in the order (per request), though its emotes were appended to the enum last so
        // network ids stay stable.
        public static readonly (Emote e, string name)[][] Pages =
        {
            new (Emote, string)[]   // Wheel 1
            {
                (Emote.FistPump,  "Fist Pump"),
                (Emote.KneeSlide, "Knee Slide"),
                (Emote.Backflip,  "Backflip"),
                (Emote.Wave,      "Wave"),
                (Emote.TPose,     "T-Pose"),
                (Emote.Griddy,    "Griddy"),
                (Emote.Bow,       "Bow"),
                (Emote.PushUps,   "Push-Ups"),
                (Emote.Robot,     "Robot"),
            },
            new (Emote, string)[]   // Wheel 2 (the newest set)
            {
                (Emote.Twerk,     "Twerk"),
                (Emote.FishFlop,  "Fish Flop"),
                (Emote.Moonwalk,  "Moonwalk"),
                (Emote.Wave2,     "Big Wave"),
                (Emote.Crip,      "C-Walk"),
                (Emote.Vibe,      "Vibe"),
                (Emote.Kick,      "High Kick"),
                (Emote.Slide2,    "Shuffle"),
            },
            new (Emote, string)[]   // Wheel 3
            {
                (Emote.Dab,        "Dab"),
                (Emote.Floss,      "Floss"),
                (Emote.Clap,       "Clap"),
                (Emote.Salute,     "Salute"),
                (Emote.HeartHands, "Heart Hands"),
                (Emote.Shrug,      "Shrug"),
                (Emote.MuscleFlex, "Muscle Flex"),
                (Emote.Point,      "Point"),
            },
            new (Emote, string)[]   // Wheel 4
            {
                (Emote.Sprinkler,  "Sprinkler"),
                (Emote.HandsUp,    "Hands Up"),
                (Emote.Facepalm,   "Facepalm"),
                (Emote.Charleston, "Charleston"),
                (Emote.Cheer,      "Cheer"),
                (Emote.Twirl,      "Twirl"),
                (Emote.Disco,      "Disco"),
                (Emote.Thinker,    "Thinker"),
            },
        };

        // Back-compat: the first wheel (single-player scrimmage uses this directly).
        public static readonly (Emote e, string name)[] Menu = Pages[0];

        ActiveRagdoll _ragdoll;
        Emote _emote;
        float _t;              // elapsed
        float _dur;            // total duration
        bool _playing;

        // Saved controller state to restore when the emote ends.
        bool _savedUpright, _savedBalance, _savedLoco;

        public bool Playing => _playing;
        // For network sync: which emote is playing and how far through (0..1).
        public Emote CurrentEmote => _emote;
        public float Progress01 => _dur > 0f ? Mathf.Clamp01(_t / _dur) : 0f;

        public void Init(ActiveRagdoll ragdoll) => _ragdoll = ragdoll;

        public void Play(Emote e)
        {
            if (_ragdoll == null || _ragdoll.Pelvis == null) return;
            _emote = e;
            _t = 0f;
            _dur = DurationFor(e);
            _playing = true;
            _flopped = false;

            _savedUpright = _ragdoll.UprightLock;
            _savedBalance = _ragdoll.BalanceEnabled;
            _savedLoco = _ragdoll.LocomotionEnabled;

            // Stand still for all; the acrobatic ones free the body to move.
            _ragdoll.MoveInput = Vector3.zero;

            switch (e)
            {
                case Emote.Backflip:
                    // Free the body and spin it backward while hopping up.
                    _ragdoll.UprightLock = false;
                    _ragdoll.BalanceEnabled = false;
                    _ragdoll.LocomotionEnabled = false;
                    _ragdoll.LaunchVerticalAll(6.5f);
                    _ragdoll.SpinWholeBody(_ragdoll.FacingRotation * Vector3.right, -520f);
                    break;
                case Emote.KneeSlide:
                    // Slide forward on the shins, torso HELD UPRIGHT (knees fold under; see the
                    // pose). Keep the upright lock on so he stays vertical through the slide.
                    _ragdoll.UprightLock = true;
                    _ragdoll.BalanceEnabled = true;
                    _ragdoll.LocomotionEnabled = false;
                    _ragdoll.AddVelocityToAll(_ragdoll.FacingRotation * Vector3.forward * 5.5f);
                    break;
                case Emote.FishFlop:
                    // Run forward upright, then belly-flop to the SIDE (a sideways topple). The
                    // run is a forward launch; the flop is a lateral velocity + roll spin part-way
                    // through (see Update's timed hand-off). Free the body so it can topple.
                    _ragdoll.UprightLock = true;    // starts upright (running); Update frees it to flop
                    _ragdoll.BalanceEnabled = true;
                    _ragdoll.LocomotionEnabled = false;
                    _ragdoll.AddVelocityToAll(_ragdoll.FacingRotation * Vector3.forward * 5f);
                    break;
                case Emote.Moonwalk:
                    // Glide BACKWARD while facing forward (the moonwalk). Locomotion stays ON so
                    // MoveInput (set each frame in Update) drives a steady backward slide; the leg
                    // shuffle pose plays on top. Upright-locked so he stays vertical as he glides.
                    _ragdoll.UprightLock = true;
                    _ragdoll.BalanceEnabled = true;
                    _ragdoll.LocomotionEnabled = true;
                    break;
                default:
                    // Standing emotes: stay locked upright, pose only.
                    _ragdoll.UprightLock = true;
                    _ragdoll.BalanceEnabled = true;
                    _ragdoll.LocomotionEnabled = false;
                    break;
            }
        }

        static float DurationFor(Emote e)
        {
            switch (e)
            {
                case Emote.Backflip:  return 1.1f;
                case Emote.KneeSlide: return 1.4f;
                case Emote.Griddy:    return 2.0f;
                case Emote.PushUps:   return 2.2f;
                case Emote.Robot:     return 2.0f;
                case Emote.Bow:       return 1.5f;
                case Emote.FishFlop:  return 1.6f;
                case Emote.Twerk:     return 2.2f;
                default:              return 1.6f;
            }
        }

        bool _flopped;   // FishFlop: has the sideways belly-flop been triggered yet

        void Update()
        {
            if (!_playing || _ragdoll == null || _ragdoll.Pelvis == null) return;
            _t += Time.deltaTime;
            float p = Mathf.Clamp01(_t / _dur);   // 0..1 progress

            // Fish Flop: run upright for the first ~40%, then release the upright lock and
            // launch sideways + roll so he belly-flops to the side. One-shot at the hand-off.
            if (_emote == Emote.FishFlop && !_flopped && p >= 0.4f)
            {
                _flopped = true;
                _ragdoll.UprightLock = false;
                _ragdoll.BalanceEnabled = false;
                _ragdoll.LocomotionEnabled = false;
                Vector3 side = _ragdoll.FacingRotation * Vector3.right;   // flop to his right
                _ragdoll.AddVelocityToAll(side * 3.5f + Vector3.up * 2.2f);
                _ragdoll.SpinWholeBody(_ragdoll.FacingRotation * Vector3.forward, -430f);  // roll onto the belly/side
            }

            // Moonwalk: steer a steady BACKWARD glide (opposite the facing) while the leg-shuffle
            // pose plays. Driven every frame so ApplyLocomotion keeps the slide going for the whole
            // emote; the root motion streams to remote puppets via the snapshot position.
            if (_emote == Emote.Moonwalk)
                _ragdoll.MoveInput = _ragdoll.FacingRotation * Vector3.back * SimConfig.MoonwalkGlideSpeed;

            _ragdoll.ClearPoseOverrides();
            // Drive the pose via the shared static formulas so the client display path (which
            // poses kinematic puppets by transform) produces the identical dance.
            EmotePose.Apply(_emote, p, (bone, euler) => _ragdoll.SetPoseOverride(bone, euler));
            // Whole-body vertical bob (push-ups) on the live dynamic body.
            _ragdoll.EmoteHeightOffset = EmotePose.RootLift(_emote, p);

            if (_t >= _dur) End();
        }

        void End()
        {
            _playing = false;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.EmoteHeightOffset = 0f;   // stop the vertical bob (push-ups)
            _ragdoll.MoveInput = Vector3.zero;   // stop the moonwalk glide
            _ragdoll.BodyOrientTarget = null;
            // Kill any residual whole-body spin (Backflip) BEFORE re-locking upright, else
            // the leftover orbital velocity in the limbs flings them when the pelvis snaps
            // rigid (zeroing pelvis angular velocity alone is not enough - see StopBodySpin).
            _ragdoll.StopBodySpin();
            _ragdoll.UprightLock = _savedUpright;
            _ragdoll.BalanceEnabled = _savedBalance;
            _ragdoll.LocomotionEnabled = _savedLoco;
        }

        // Stop immediately (control switch / reset).
        public void Cancel()
        {
            if (_playing) End();
        }
    }

    /// <summary>
    /// The per-bone pose formulas for each emote, as a static, side-effect-free module so BOTH
    /// the live Celebration (drives ConfigurableJoint targets on a dynamic body) and the network
    /// display path (poses a kinematic puppet's bone transforms directly) compute the SAME dance.
    /// `p` is 0..1 progress. `set(bone, eulerDegrees)` is the additive local-rotation override for
    /// that bone, layered on the Stand rest pose. Backflip/KneeSlide are physics-driven (gross
    /// translation the snapshot already carries), so they emit no pose here.
    /// </summary>
    public static class EmotePose
    {
        // Scratch pose buffer (indexed by (int)Bone). Main-thread only (live Update + display
        // puppet both run on the main thread and never nest), so a shared static is safe and
        // avoids per-frame GC. Apply fills it, clamps arms out of the torso, then emits.
        static readonly Vector3[] _buf = new Vector3[(int)Bone.Count];

        // Public entry: builds the raw per-bone pose, runs the arm-clip clamp so no arm segment
        // ever overlaps the torso (works for BOTH the live joint-driven body and the kinematic
        // display puppet, since both call through here), then fires `set` for each posed bone.
        public static void Apply(Celebration.Emote e, float p, System.Action<Bone, Vector3> set)
        {
            for (int i = 0; i < _buf.Length; i++) _buf[i] = Vector3.zero;
            Build(e, p, _WriteBuf);
            // Only clamp when the torso is roughly UPRIGHT and at rest height. Poses that pitch
            // the torso hard or drop the whole body (push-ups plank, bow, twerk squat, knee slide)
            // move the torso away from the rest AABB, so the rest-box test would be wrong there -
            // and in those poses the arms are part of a deliberate physical arrangement, not a
            // chest clip. The user's issue is standing celebrations where an arm crosses the body.
            bool uprightRestPose = Mathf.Abs(_buf[(int)Bone.Torso].x) < 25f
                                   && Mathf.Abs(EmotePose.RootLift(e, p)) < 0.05f;
            if (uprightRestPose) ClampArms(_buf);
            for (int i = 0; i < _buf.Length; i++)
                if (_buf[i] != Vector3.zero) set((Bone)i, _buf[i]);
        }

        // Cached delegate (no closure over locals -> zero per-call allocation).
        static readonly System.Action<Bone, Vector3> _WriteBuf = (bone, euler) => _buf[(int)bone] = euler;

        // ---- Arm-clip clamp ----------------------------------------------------------------
        // Keep every arm segment out of the torso volume. Rather than hand-tune per-pose euler
        // limits (fragile - depends on Unity's left-handed rotation sign), we run the SAME forward
        // kinematics the rig uses (Quaternion.Euler, so the engine's own sign is authoritative) to
        // find where each arm segment lands, and if it pierces the torso box we add just enough
        // ABDUCTION (swing the arm out to its side, +Z on L / -Z on R) to lift it clear. Abduction
        // only ever moves the arm laterally away from the body, so it can't create a new clip, and
        // it preserves the read of the pose (a raised/forward arm stays raised/forward, just not
        // buried in the chest). Legs/torso/head are untouched. Identical on the live joint-driven
        // body and the kinematic display puppet, since both pose from this same euler set.
        //
        // Torso AABB (visible torso 0.36x0.46x0.22) padded by the ARM RADIUS (0.05) on each axis,
        // so a test point is "inside" exactly when an arm capsule of that radius would touch the
        // torso. Shoulders sit at x=+/-0.26, which is 0.03 OUTSIDE the padded x-edge (0.23) - so a
        // rest/abducted arm never false-flags; only a segment angled in through the chest does.
        static readonly Vector3 _torsoC = new Vector3(0f, 1.34f, 0f);
        static readonly Vector3 _torsoH = new Vector3(0.18f + 0.05f, 0.23f + 0.05f, 0.11f + 0.05f);
        const float ShoulderX = 0.26f, ShoulderY = 1.57f, UpperArmLen = 0.33f, ForearmLen = 0.31f;

        static void ClampArms(Vector3[] buf)
        {
            ClampArm(buf, (int)Bone.UpperArmL, (int)Bone.ForearmL, +1f);
            ClampArm(buf, (int)Bone.UpperArmR, (int)Bone.ForearmR, -1f);
        }

        // abductSign = +1 LEFT arm (shoulder at x=-0.26, local +Z abducts OUT to the left),
        //              -1 RIGHT arm (shoulder at x=+0.26, local -Z abducts OUT to the right).
        // The shoulder X and the abduction axis sign are OPPOSITE - getting this pairing wrong
        // swings arms across the chest instead of clear of it.
        //
        // SAFETY NET, never a regression: only nudges an arm out when a segment genuinely enters
        // the padded torso box, only ACCEPTS a step that strictly increases clearance, and leaves
        // already-raised/out arms alone. Verified against every standing pose x all p: clean poses
        // get 0 steps; it only fires on a real cross-chest segment.
        static void ClampArm(Vector3[] buf, int ua, int fa, float abductSign)
        {
            Vector3 shoulder = new Vector3(-abductSign * ShoulderX, ShoulderY, 0f);

            // Arms raised out to the side / overhead (|abduction| already large) clear the chest
            // by construction; abducting them further only wraps them past vertical. Skip.
            if (Mathf.Abs(buf[ua].z) >= 100f) return;

            float clear = ArmClearance(buf[ua], buf[fa], shoulder);
            for (int iter = 0; iter < 10 && clear < 0f; iter++)
            {
                Vector3 trial = buf[ua];
                trial.z += abductSign * 8f;
                float tClear = ArmClearance(trial, buf[fa], shoulder);
                if (tClear <= clear) break;   // not helping (e.g. wrapped past vertical) -> stop
                buf[ua] = trial;
                clear = tClear;
            }
        }

        // Signed clearance of the WHOLE arm from the padded torso box: >0 fully clear (metres to
        // the nearest face), <0 penetrating. min over both segments, checking BOTH rig conventions
        // (kinematic puppet: forearm world-rot ABSOLUTE, matching DisplayEmote; live body: forearm
        // rides the upper arm), so the clamp is correct for both.
        static float ArmClearance(Vector3 uaEuler, Vector3 faEuler, Vector3 shoulder)
        {
            Vector3 down = Vector3.down;
            Quaternion uaRot = Quaternion.Euler(uaEuler);
            Vector3 elbow = shoulder + uaRot * down * UpperArmLen;
            Vector3 handPuppet = elbow + Quaternion.Euler(faEuler) * down * ForearmLen;
            Vector3 handLive   = elbow + (uaRot * Quaternion.Euler(faEuler)) * down * ForearmLen;
            return Mathf.Min(SegClearance(shoulder, elbow),
                             Mathf.Min(SegClearance(elbow, handPuppet), SegClearance(elbow, handLive)));
        }

        // Min signed box-clearance sampled along a segment (>0 outside, <0 inside).
        static float SegClearance(Vector3 a, Vector3 b)
        {
            float min = float.MaxValue;
            for (int i = 0; i <= 8; i++)
            {
                Vector3 d = Vector3.Lerp(a, b, i / 8f) - _torsoC;
                float c = Mathf.Max(Mathf.Abs(d.x) - _torsoH.x,
                          Mathf.Max(Mathf.Abs(d.y) - _torsoH.y, Mathf.Abs(d.z) - _torsoH.z));
                if (c < min) min = c;
            }
            return min;
        }

        static void Build(Celebration.Emote e, float p, System.Action<Bone, Vector3> set)
        {
            switch (e)
            {
                case Celebration.Emote.FistPump:
                {
                    float pump = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 4f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -160f * pump - 10f));
                    // Arm snaps FULLY STRAIGHT at the top of the punch (forearm -> 0), bends only
                    // on the way down.
                    set(Bone.ForearmR, new Vector3(-45f * (1f - pump), 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 20f));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.Torso, new Vector3(-6f * pump, 0f, 0f));
                    break;
                }
                case Celebration.Emote.KneeSlide:
                {
                    // KNEELING slide on both shins: thighs drop back a little, both knees fold
                    // hard so the shins are flat under him, torso stays UPRIGHT (slight lean back),
                    // arms flung out straight. RootLift lowers him so the shins meet the ground.
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.ThighL, new Vector3(-20f * k, 0f, 0f));
                    set(Bone.ThighR, new Vector3(-20f * k, 0f, 0f));
                    set(Bone.CalfL, new Vector3(135f * k, 0f, 0f));   // shin folds under
                    set(Bone.CalfR, new Vector3(135f * k, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 100f * k));  // arms out + slightly up
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -100f * k));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    set(Bone.Torso, new Vector3(-10f * k, 0f, 0f));      // slight triumphant lean back
                    set(Bone.Head, new Vector3(-8f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Backflip:
                    break;   // physics-driven; no pose
                case Celebration.Emote.Wave:
                {
                    float wave = Mathf.Sin(p * Mathf.PI * 6f) * 25f;
                    // Arm raised fully up the side, straight, forearm waving side to side.
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -165f));
                    set(Bone.ForearmR, new Vector3(0f, 0f, wave));
                    break;
                }
                case Celebration.Emote.TPose:
                {
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 90f * k));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -90f * k));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    break;
                }
                case Celebration.Emote.Griddy:
                {
                    float s = Mathf.Sin(p * Mathf.PI * 8f);
                    float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
                    set(Bone.ThighL, new Vector3(-70f * liftL, 0f, 0f));
                    set(Bone.CalfL, new Vector3(60f * liftL, 0f, 0f));
                    set(Bone.ThighR, new Vector3(-70f * liftR, 0f, 0f));
                    set(Bone.CalfR, new Vector3(60f * liftR, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 70f + s * 30f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -70f + s * 30f));
                    break;
                }
                case Celebration.Emote.Bow:
                {
                    float k = Mathf.Sin(Mathf.Clamp01(p * 1.5f) * Mathf.PI * 0.5f);
                    set(Bone.Torso, new Vector3(55f * k, 0f, 0f));
                    set(Bone.Head, new Vector3(20f * k, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(30f * k, 0f, 25f));
                    set(Bone.UpperArmR, new Vector3(30f * k, 0f, -25f));
                    break;
                }
                case Celebration.Emote.PushUps:
                {
                    // Plank: torso pitched flat forward, legs straight back, arms down to the
                    // ground. `up` = 1 at the top (arms STRAIGHT), 0 at the bottom (elbows bent).
                    // The whole body also bobs vertically (see RootLift) so it visibly goes up/down.
                    float up = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 5f);
                    set(Bone.Torso, new Vector3(78f, 0f, 0f));
                    set(Bone.Head, new Vector3(-20f, 0f, 0f));
                    set(Bone.ThighL, new Vector3(12f, 0f, 0f));
                    set(Bone.ThighR, new Vector3(12f, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 70f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -70f));
                    // Elbows: straight (0) at the top, bent (~95) at the bottom.
                    set(Bone.ForearmL, new Vector3(-95f * (1f - up), 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-95f * (1f - up), 0f, 0f));
                    break;
                }
                case Celebration.Emote.Robot:
                {
                    float beat = Mathf.Floor(p * 8f) % 2f;
                    float a = beat > 0.5f ? 1f : 0f;
                    set(Bone.UpperArmR, new Vector3(-90f * a, 0f, -10f));
                    set(Bone.ForearmR, new Vector3(-80f * a, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(-90f * (1f - a), 0f, 10f));
                    set(Bone.ForearmL, new Vector3(-80f * (1f - a), 0f, 0f));
                    set(Bone.ThighR, new Vector3(-35f * (1f - a), 0f, 0f));
                    set(Bone.ThighL, new Vector3(-35f * a, 0f, 0f));
                    break;
                }
                default:
                    ApplyExtra(e, p, set);   // wheels 2 & 3
                    break;
            }
        }

        // Vertical body offset (metres) for emotes that raise/lower the whole body. Push-ups
        // drop into a plank near the ground and pump up/down; everything else stays at 0.
        // Applied to the pelvis (live body) and to every display-puppet bone (remote view).
        public static float RootLift(Celebration.Emote e, float p)
        {
            switch (e)
            {
                case Celebration.Emote.PushUps:
                {
                    // Sink ~0.7m into a plank, then bob down another ~0.18m at the bottom of each
                    // rep. up = 1 at the top of the push, 0 at the bottom.
                    float up = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 5f);
                    float sink = Mathf.Clamp01(p * 4f) * 0.7f;   // ease into the plank
                    return -sink - (1f - up) * 0.18f;
                }
                case Celebration.Emote.KneeSlide:
                    return -Mathf.Clamp01(p * 3f) * 0.45f;   // drop onto the shins
                case Celebration.Emote.Twerk:
                    return -0.35f;                            // squat down into the wide stance
                default: return 0f;
            }
        }

        // Wheels 2 & 3. All standing poses (upright held); arms fully extend where the move
        // calls for it (straight elbows = forearm 0). Local +Z on an UpperArm raises it out to
        // that side (L positive up-left, R negative up-right); +X on an UpperArm swings it forward.
        static void ApplyExtra(Celebration.Emote e, float p, System.Action<Bone, Vector3> set)
        {
            switch (e)
            {
                case Celebration.Emote.Dab:
                {
                    // A proper dab: BACK arm (right) fully extended straight out and up to the
                    // side; FRONT arm (left) bent across the body; HEAD bows DOWN into the crook
                    // of the bent front arm.
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.UpperArmR, new Vector3(-25f, 0f, -155f * k));   // back arm out+up
                    set(Bone.ForearmR, Vector3.zero);                        // FULLY extended (straight)
                    set(Bone.UpperArmL, new Vector3(-30f * k, 0f, 95f * k)); // front arm raised across
                    set(Bone.ForearmL, new Vector3(-95f * k, 0f, 0f));       // BENT elbow
                    set(Bone.Head, new Vector3(34f * k, 0f, 0f));            // bow the head DOWN into it
                    set(Bone.Torso, new Vector3(14f * k, 0f, -8f * k));      // lean into the dab
                    break;
                }
                case Celebration.Emote.Floss:
                {
                    // Both straight arms swing side to side, hips counter-swing (the floss).
                    float s = Mathf.Sin(p * Mathf.PI * 8f);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 25f + s * 30f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -25f + s * 30f));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    set(Bone.Torso, new Vector3(0f, s * 12f, s * 8f));
                    break;
                }
                case Celebration.Emote.Clap:
                {
                    // Arms forward, hands meeting at centre repeatedly (a clap).
                    float c = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 6f);   // 1 = hands together
                    set(Bone.UpperArmL, new Vector3(-75f, 0f, 25f + 25f * c));
                    set(Bone.UpperArmR, new Vector3(-75f, 0f, -25f - 25f * c));
                    set(Bone.ForearmL, new Vector3(-20f, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-20f, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Salute:
                {
                    // Right hand snaps up to the brow; left arm straight at the side.
                    float k = Mathf.Clamp01(p * 4f);
                    set(Bone.UpperArmR, new Vector3(-30f * k, 0f, -95f * k));
                    set(Bone.ForearmR, new Vector3(-130f * k, 0f, 0f));      // bent up to the temple
                    set(Bone.Torso, new Vector3(-4f, 0f, 0f));
                    break;
                }
                case Celebration.Emote.HeartHands:
                {
                    // Both hands raised together above the chest making a heart.
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.UpperArmL, new Vector3(-95f * k, 0f, 35f * k));
                    set(Bone.UpperArmR, new Vector3(-95f * k, 0f, -35f * k));
                    set(Bone.ForearmL, new Vector3(-60f * k, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-60f * k, 0f, 0f));
                    set(Bone.Head, new Vector3(-8f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Shrug:
                {
                    // Forearms out, palms up, a slow shrug of the arms.
                    float k = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 2f);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 55f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -55f));
                    set(Bone.ForearmL, new Vector3(-70f, -30f * k, 0f));
                    set(Bone.ForearmR, new Vector3(-70f, 30f * k, 0f));
                    set(Bone.Head, new Vector3(0f, 0f, 6f * (k - 0.5f)));
                    break;
                }
                case Celebration.Emote.MuscleFlex:
                {
                    // Classic double-biceps: arms up to the sides, forearms curled hard in.
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 95f * k));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -95f * k));
                    set(Bone.ForearmL, new Vector3(-145f * k, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-145f * k, 0f, 0f));
                    set(Bone.Torso, new Vector3(0f, Mathf.Sin(p * Mathf.PI * 4f) * 6f, 0f));
                    break;
                }
                case Celebration.Emote.Point:
                {
                    // Right arm fully straight, pointing forward at the loser; slight lean in.
                    float k = Mathf.Clamp01(p * 4f);
                    set(Bone.UpperArmR, new Vector3(-95f * k, 0f, -10f));
                    set(Bone.ForearmR, Vector3.zero);                        // straight point
                    set(Bone.Torso, new Vector3(8f * k, 0f, 0f));
                    set(Bone.Head, new Vector3(6f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Sprinkler:
                {
                    // One arm straight out, ratcheting around while the other pumps behind the head.
                    float sweep = Mathf.Repeat(p * 2f, 1f);                  // 0..1 ratchet
                    set(Bone.UpperArmR, new Vector3(0f, -90f + sweep * 120f, -95f));
                    set(Bone.ForearmR, Vector3.zero);                        // straight sprinkler arm
                    set(Bone.UpperArmL, new Vector3(-120f, 0f, 30f));
                    set(Bone.ForearmL, new Vector3(-90f, 0f, 0f));           // hand behind the head
                    break;
                }
                case Celebration.Emote.HandsUp:
                {
                    // Both arms thrown fully straight overhead, a little wave.
                    float s = Mathf.Sin(p * Mathf.PI * 4f) * 12f;
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 170f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -170f));
                    set(Bone.ForearmL, new Vector3(0f, 0f, s));
                    set(Bone.ForearmR, new Vector3(0f, 0f, -s));
                    break;
                }
                case Celebration.Emote.Facepalm:
                {
                    // Head drops, one hand to the face (disappointment).
                    float k = Mathf.Clamp01(p * 2f);
                    set(Bone.UpperArmR, new Vector3(-70f * k, 0f, -55f * k));
                    set(Bone.ForearmR, new Vector3(-120f * k, 0f, 0f));
                    set(Bone.Head, new Vector3(22f * k, 0f, 0f));
                    set(Bone.Torso, new Vector3(10f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Charleston:
                {
                    // 20s dance: knees swivel in/out while straight arms swing opposite.
                    float s = Mathf.Sin(p * Mathf.PI * 6f);
                    set(Bone.ThighL, new Vector3(-25f - 20f * s, 0f, 0f));
                    set(Bone.ThighR, new Vector3(-25f + 20f * s, 0f, 0f));
                    set(Bone.CalfL, new Vector3(30f, 0f, 0f));
                    set(Bone.CalfR, new Vector3(30f, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(40f * s, 0f, 20f));
                    set(Bone.UpperArmR, new Vector3(-40f * s, 0f, -20f));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    break;
                }
                case Celebration.Emote.Cheer:
                {
                    // Both straight arms punch overhead on the beat; small hop feel via torso.
                    float pump = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 5f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 120f + 45f * pump));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -120f - 45f * pump));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    set(Bone.Torso, new Vector3(-6f * pump, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Twirl:
                {
                    // Arms out straight like a spin; torso yaws around (the puppet root yaw is
                    // networked, so the whole-body turn reads; here we splay the arms + lean).
                    float s = Mathf.Sin(p * Mathf.PI * 4f);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 95f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -95f));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    set(Bone.Torso, new Vector3(0f, 0f, s * 14f));
                    set(Bone.Head, new Vector3(0f, 0f, s * 10f));
                    break;
                }
                case Celebration.Emote.Disco:
                {
                    // Saturday-night: one arm points up-out, the other down-across, alternating.
                    float s = Mathf.Sin(p * Mathf.PI * 5f);
                    float a = 0.5f + 0.5f * s;
                    set(Bone.UpperArmR, new Vector3(-30f, 0f, -60f - 90f * a));   // up-out
                    set(Bone.ForearmR, Vector3.zero);
                    set(Bone.UpperArmL, new Vector3(20f, 0f, 30f + 30f * a));      // down-across
                    set(Bone.ForearmL, new Vector3(-20f, 0f, 0f));
                    set(Bone.Torso, new Vector3(0f, 0f, s * 8f));
                    break;
                }
                case Celebration.Emote.Thinker:
                {
                    // Hand to chin, slight forward lean (the pensive taunt).
                    float k = Mathf.Clamp01(p * 2f);
                    set(Bone.UpperArmR, new Vector3(-55f * k, 0f, -35f * k));
                    set(Bone.ForearmR, new Vector3(-115f * k, 0f, 0f));
                    set(Bone.Torso, new Vector3(14f * k, 0f, 0f));
                    set(Bone.Head, new Vector3(10f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Twerk:
                {
                    // Legs planted WIDE (thighs abducted out to the sides via local +Z/-Z), knees
                    // bent into a squat (calves fold), hands on the knees, and the pelvis/torso
                    // SHAKE violently and fast. RootLift drops him into the squat.
                    float shake = Mathf.Sin(p * Mathf.PI * 24f);        // fast, violent
                    set(Bone.ThighL, new Vector3(-30f, 0f, 42f));       // wide stance (out to the side)
                    set(Bone.ThighR, new Vector3(-30f, 0f, -42f));
                    set(Bone.CalfL, new Vector3(70f, 0f, 0f));          // bent knees
                    set(Bone.CalfR, new Vector3(70f, 0f, 0f));
                    // Violent pelvis/hip shake: pitch the torso back and forth hard + fast.
                    set(Bone.Torso, new Vector3(18f + shake * 22f, 0f, 0f));
                    set(Bone.Head, new Vector3(-10f, 0f, 0f));          // look up/ahead
                    // Hands braced on the knees.
                    set(Bone.UpperArmL, new Vector3(-40f, 0f, 55f));
                    set(Bone.UpperArmR, new Vector3(-40f, 0f, -55f));
                    set(Bone.ForearmL, new Vector3(-40f, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-40f, 0f, 0f));
                    break;
                }
                case Celebration.Emote.FishFlop:
                {
                    // Physics-driven (see Celebration.Update): runs upright, then belly-flops to
                    // the side. Pose: running arms early, then arms/legs splayed flat for the flop.
                    if (p < 0.4f)
                    {
                        float run = Mathf.Sin(p * Mathf.PI * 10f);
                        set(Bone.ThighL, new Vector3(-40f * run, 0f, 0f));
                        set(Bone.ThighR, new Vector3(40f * run, 0f, 0f));
                        set(Bone.UpperArmL, new Vector3(35f * run, 0f, 10f));
                        set(Bone.UpperArmR, new Vector3(-35f * run, 0f, -10f));
                    }
                    else
                    {
                        // Superman flop: arms straight overhead, legs straight back.
                        set(Bone.UpperArmL, new Vector3(0f, 0f, 165f));
                        set(Bone.UpperArmR, new Vector3(0f, 0f, -165f));
                        set(Bone.ForearmL, Vector3.zero);
                        set(Bone.ForearmR, Vector3.zero);
                        set(Bone.ThighL, new Vector3(15f, 0f, 0f));
                        set(Bone.ThighR, new Vector3(15f, 0f, 0f));
                    }
                    break;
                }
                case Celebration.Emote.Moonwalk:
                {
                    // Gliding-back leg shuffle, arms loose at the sides swinging.
                    float s = Mathf.Sin(p * Mathf.PI * 6f);
                    set(Bone.ThighL, new Vector3(-25f - 25f * s, 0f, 0f));
                    set(Bone.CalfL, new Vector3(Mathf.Max(0f, 40f * s), 0f, 0f));
                    set(Bone.ThighR, new Vector3(-25f + 25f * s, 0f, 0f));
                    set(Bone.CalfR, new Vector3(Mathf.Max(0f, -40f * s), 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(-15f * s, 0f, 12f));
                    set(Bone.UpperArmR, new Vector3(15f * s, 0f, -12f));
                    break;
                }
                case Celebration.Emote.Wave2:
                {
                    // Both arms fully up and waving big overhead.
                    float w = Mathf.Sin(p * Mathf.PI * 5f) * 20f;
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 165f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -165f));
                    set(Bone.ForearmL, new Vector3(0f, 0f, w));
                    set(Bone.ForearmR, new Vector3(0f, 0f, -w));
                    set(Bone.Torso, new Vector3(0f, 0f, Mathf.Sin(p * Mathf.PI * 2.5f) * 8f));
                    break;
                }
                case Celebration.Emote.Crip:
                {
                    // C-Walk-ish: quick heel-toe leg kicks alternating, arms crossed low.
                    float s = Mathf.Sin(p * Mathf.PI * 7f);
                    set(Bone.ThighL, new Vector3(-50f * Mathf.Max(0f, s), 0f, 0f));
                    set(Bone.CalfL, new Vector3(70f * Mathf.Max(0f, s), 0f, 0f));
                    set(Bone.ThighR, new Vector3(-50f * Mathf.Max(0f, -s), 0f, 0f));
                    set(Bone.CalfR, new Vector3(70f * Mathf.Max(0f, -s), 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(-25f, 0f, 30f));
                    set(Bone.UpperArmR, new Vector3(-25f, 0f, -30f));
                    set(Bone.ForearmL, new Vector3(-60f, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-60f, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Vibe:
                {
                    // Chill two-step sway with a nod, hands loosely up.
                    float s = Mathf.Sin(p * Mathf.PI * 4f);
                    set(Bone.Torso, new Vector3(0f, 0f, s * 10f));
                    set(Bone.Head, new Vector3(6f * Mathf.Abs(s), 0f, s * 6f));
                    set(Bone.UpperArmL, new Vector3(-30f, 0f, 40f + s * 10f));
                    set(Bone.UpperArmR, new Vector3(-30f, 0f, -40f + s * 10f));
                    set(Bone.ForearmL, new Vector3(-55f, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-55f, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Kick:
                {
                    // Big alternating high kicks, arms out straight for balance.
                    float s = Mathf.Sin(p * Mathf.PI * 4f);
                    set(Bone.ThighR, new Vector3(-110f * Mathf.Max(0f, s), 0f, 0f));
                    set(Bone.ThighL, new Vector3(-110f * Mathf.Max(0f, -s), 0f, 0f));
                    set(Bone.CalfR, Vector3.zero);
                    set(Bone.CalfL, Vector3.zero);
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 80f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -80f));
                    set(Bone.ForearmL, Vector3.zero);
                    set(Bone.ForearmR, Vector3.zero);
                    break;
                }
                case Celebration.Emote.Slide2:
                {
                    // Melbourne shuffle: fast in-place running-man feel, arms pumping.
                    float s = Mathf.Sin(p * Mathf.PI * 9f);
                    set(Bone.ThighL, new Vector3(-55f * Mathf.Max(0f, s), 0f, 0f));
                    set(Bone.CalfL, new Vector3(50f * Mathf.Max(0f, s), 0f, 0f));
                    set(Bone.ThighR, new Vector3(-55f * Mathf.Max(0f, -s), 0f, 0f));
                    set(Bone.CalfR, new Vector3(50f * Mathf.Max(0f, -s), 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(-40f * s, 0f, 12f));
                    set(Bone.UpperArmR, new Vector3(40f * s, 0f, -12f));
                    set(Bone.ForearmL, new Vector3(-50f, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-50f, 0f, 0f));
                    break;
                }
            }
        }
    }
}
