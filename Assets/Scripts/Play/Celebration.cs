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
        // The six wheel emotes. Names are shown on the wheel.
        public enum Emote { FistPump, KneeSlide, Backflip, Wave, TPose, Griddy }
        public static readonly (Emote e, string name)[] Menu =
        {
            (Emote.FistPump,  "Fist Pump"),
            (Emote.KneeSlide, "Knee Slide"),
            (Emote.Backflip,  "Backflip"),
            (Emote.Wave,      "Wave"),
            (Emote.TPose,     "T-Pose"),
            (Emote.Griddy,    "Griddy"),
        };

        ActiveRagdoll _ragdoll;
        Emote _emote;
        float _t;              // elapsed
        float _dur;            // total duration
        bool _playing;

        // Saved controller state to restore when the emote ends.
        bool _savedUpright, _savedBalance, _savedLoco;

        public bool Playing => _playing;

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
                default:              return 1.6f;
            }
        }

        void Update()
        {
            if (!_playing || _ragdoll == null || _ragdoll.Pelvis == null) return;
            _t += Time.deltaTime;
            float p = Mathf.Clamp01(_t / _dur);   // 0..1 progress

            _ragdoll.ClearPoseOverrides();
            switch (_emote)
            {
                case Emote.FistPump:  PoseFistPump(p); break;
                case Emote.KneeSlide: PoseKneeSlide(p); break;
                case Emote.Backflip:  /* physics-driven; no pose needed */ break;
                case Emote.Wave:      PoseWave(p); break;
                case Emote.TPose:     PoseTPose(p); break;
                case Emote.Griddy:    PoseGriddy(p); break;
            }

            if (_t >= _dur) End();
        }

        // ---- emote pose animations (per-bone Euler overrides, layered on Stand) ----
        void PoseFistPump(float p)
        {
            // Right arm punches up repeatedly; slight torso bob.
            float pump = Mathf.Abs(Mathf.Sin(p * Mathf.PI * 4f));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(0f, 0f, -160f * pump - 10f));
            _ragdoll.SetPoseOverride(Bone.ForearmR, new Vector3(-40f * pump, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(0f, 0f, 20f));
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(-6f * pump, 0f, 0f));
        }

        void PoseKneeSlide(float p)
        {
            // Drop onto one knee, arms out; the forward velocity from Play does the slide.
            float k = Mathf.Clamp01(p * 3f);
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-70f * k, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR, new Vector3(130f * k, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(30f * k, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(0f, 0f, 90f * k));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(0f, 0f, -90f * k));
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(-14f * k, 0f, 0f));
        }

        void PoseWave(float p)
        {
            // Right arm up, forearm waves side to side.
            float wave = Mathf.Sin(p * Mathf.PI * 6f) * 25f;
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(0f, 0f, -150f));
            _ragdoll.SetPoseOverride(Bone.ForearmR, new Vector3(0f, 0f, wave));
        }

        void PoseTPose(float p)
        {
            // Both arms straight out to the sides (the meme). Ease in.
            float k = Mathf.Clamp01(p * 3f);
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(0f, 0f, 90f * k));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(0f, 0f, -90f * k));
            _ragdoll.SetPoseOverride(Bone.ForearmL, Vector3.zero);
            _ragdoll.SetPoseOverride(Bone.ForearmR, Vector3.zero);
        }

        void PoseGriddy(float p)
        {
            // Alternating high knees + arms swinging across (a loose Griddy).
            float s = Mathf.Sin(p * Mathf.PI * 8f);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-70f * liftL, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfL, new Vector3(60f * liftL, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-70f * liftR, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR, new Vector3(60f * liftR, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(0f, 0f, 70f + s * 30f));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(0f, 0f, -70f + s * 30f));
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
}
