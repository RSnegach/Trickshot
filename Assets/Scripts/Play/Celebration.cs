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
        // The wheel emotes. Names are shown on the wheel.
        public enum Emote { FistPump, KneeSlide, Backflip, Wave, TPose, Griddy, Bow, PushUps, Robot }
        public static readonly (Emote e, string name)[] Menu =
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
        };

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
                    // Slide forward along facing, upright held.
                    _ragdoll.UprightLock = false;
                    _ragdoll.BalanceEnabled = true;
                    _ragdoll.LocomotionEnabled = false;
                    _ragdoll.BodyOrientTarget = _ragdoll.FacingRotation;
                    _ragdoll.AddVelocityToAll(_ragdoll.FacingRotation * Vector3.forward * 5.5f);
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
                default:              return 1.6f;
            }
        }

        void Update()
        {
            if (!_playing || _ragdoll == null || _ragdoll.Pelvis == null) return;
            _t += Time.deltaTime;
            float p = Mathf.Clamp01(_t / _dur);   // 0..1 progress

            _ragdoll.ClearPoseOverrides();
            // Drive the pose via the shared static formulas so the client display path (which
            // poses kinematic puppets by transform) produces the identical dance.
            EmotePose.Apply(_emote, p, (bone, euler) => _ragdoll.SetPoseOverride(bone, euler));

            if (_t >= _dur) End();
        }

        void End()
        {
            _playing = false;
            _ragdoll.ClearPoseOverrides();
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
        public static void Apply(Celebration.Emote e, float p, System.Action<Bone, Vector3> set)
        {
            switch (e)
            {
                case Celebration.Emote.FistPump:
                {
                    float pump = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 4f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -160f * pump - 10f));
                    set(Bone.ForearmR, new Vector3(-40f * pump, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 20f));
                    set(Bone.Torso, new Vector3(-6f * pump, 0f, 0f));
                    break;
                }
                case Celebration.Emote.KneeSlide:
                {
                    float k = Mathf.Clamp01(p * 3f);
                    set(Bone.ThighR, new Vector3(-70f * k, 0f, 0f));
                    set(Bone.CalfR, new Vector3(130f * k, 0f, 0f));
                    set(Bone.ThighL, new Vector3(30f * k, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 90f * k));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -90f * k));
                    set(Bone.Torso, new Vector3(-14f * k, 0f, 0f));
                    break;
                }
                case Celebration.Emote.Backflip:
                    break;   // physics-driven; no pose
                case Celebration.Emote.Wave:
                {
                    float wave = Mathf.Sin(p * Mathf.PI * 6f) * 25f;
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -150f));
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
                    float pump = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 5f));
                    set(Bone.Torso, new Vector3(70f, 0f, 0f));
                    set(Bone.ThighL, new Vector3(40f, 0f, 0f));
                    set(Bone.ThighR, new Vector3(40f, 0f, 0f));
                    set(Bone.UpperArmL, new Vector3(0f, 0f, 80f));
                    set(Bone.UpperArmR, new Vector3(0f, 0f, -80f));
                    set(Bone.ForearmL, new Vector3(-90f * pump, 0f, 0f));
                    set(Bone.ForearmR, new Vector3(-90f * pump, 0f, 0f));
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
            }
        }
    }
}
