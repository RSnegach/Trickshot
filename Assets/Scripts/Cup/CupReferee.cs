using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The referee of a cup round (design 7.1): a Human-species AI body with no cosmetics in the
    /// black-and-white striped kit (JerseyDesigns.RefereeName) and black limbs, who never touches
    /// play - the ball ignores every collider of his, and he is never in a lineup or a snapshot's
    /// player set (he rides as a virtual body like the AI).
    ///
    /// He does exactly three things: stands still on his mark (3 m to the side of the ball, level
    /// with it, facing the taker), raises his right forearm to his mouth before EVERY whistle
    /// (the WhistleRaise emote - the driver owns the timing: raise, wait CupTuning.WhistleRaiseSeconds,
    /// AudioManager.PlayWhistle, hold, drop), and walks to and from the penalty spot for the coin
    /// toss (MoveToward, for CupCoinToss).
    /// </summary>
    public sealed class CupReferee : MonoBehaviour
    {
        /// <summary>His body.</summary>
        public ActiveRagdoll Body { get; private set; }
        /// <summary>The emote player the whistle raise runs on (Celebration.Emote.WhistleRaise).</summary>
        public Celebration Celeb { get; private set; }
        /// <summary>Where he stands during play (feet level).</summary>
        public Vector3 Mark { get; private set; }
        /// <summary>The way he faces on his mark (toward the taker).</summary>
        public Quaternion MarkFacing { get; private set; } = Quaternion.identity;

        float _gaitPhase;
        bool _walking;

        /// <summary>
        /// Build him under the round root at a mark. `torso` / `limb` are the round's referee
        /// materials (CupKitCache.Referee() and a black limb) - owned by the cache, never by him.
        /// </summary>
        public static CupReferee Create(Transform root, BallController ball, Material torso, Material limb,
                                        Vector3 mark, Quaternion facing)
        {
            var go = new GameObject("CupReferee");
            if (root != null) go.transform.SetParent(root, true);
            var r = go.AddComponent<CupReferee>();
            r.Body = CupBodies.BuildAi(go, mark, facing, torso, limb, gloves: false);
            r.Celeb = go.AddComponent<Celebration>();
            r.Celeb.Init(r.Body);
            r.Mark = mark;
            r.MarkFacing = facing;
            // Cosmetic: the ball passes through him, whatever the shot does (design 10).
            if (ball != null) ball.IgnoreBody(r.Body, true);
            return r;
        }

        /// <summary>The referee is standing (built and not torn down).</summary>
        public bool Alive => Body != null && Body.Pelvis != null;

        /// <summary>He is mid-raise (forearm at the mouth, or on the way).</summary>
        public bool Raising => Celeb != null && Celeb.Playing && Celeb.CurrentEmote == Celebration.Emote.WhistleRaise;

        /// <summary>Snap him to a new mark, facing a point (the taker's run-up start), standing.</summary>
        public void SetMark(Vector3 mark, Vector3 faceToward)
        {
            Mark = new Vector3(mark.x, 0f, mark.z);
            Vector3 to = faceToward - Mark; to.y = 0f;
            MarkFacing = to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to.normalized, Vector3.up) : Quaternion.identity;
            Snap(Mark, MarkFacing);
        }

        /// <summary>Teleport him to a spot with a facing and stand him up (any emote cancelled).</summary>
        public void Snap(Vector3 spot, Quaternion facing)
        {
            if (!Alive) return;
            if (Celeb != null) Celeb.Cancel();
            _walking = false;
            _gaitPhase = 0f;
            Body.ResetTo(new Vector3(spot.x, 0f, spot.z), facing);
        }

        /// <summary>Back to his play mark from wherever the ceremony left him.</summary>
        public void ReturnToMark() => Snap(Mark, MarkFacing);

        /// <summary>
        /// Start the raise. The driver fires AudioManager.PlayWhistle after CupTuning.WhistleRaiseSeconds
        /// (the emote reaches the mouth at p = 0.375 of its 1.2 s, which is 0.45 s), holds the hand
        /// there CupTuning.WhistleHoldAfter, then eases it down over its last 0.3 s.
        /// </summary>
        public void RaiseWhistle()
        {
            if (!Alive || Celeb == null) return;
            _walking = false;
            Body.MoveInput = Vector3.zero;
            Body.ClearPoseOverrides();
            if (Celeb.Playing) Celeb.Cancel();   // Play snapshots the control flags: never stack one on another
            Celeb.Play(Celebration.Emote.WhistleRaise);
        }

        /// <summary>
        /// Walk toward a flat target at `speed` (the coin toss: CupTuning.WalkSpeed to the penalty
        /// spot and back). Returns the remaining flat distance so the caller decides when he has
        /// arrived; call Stop(facing) then. The gait is the project's cosmetic run gait at walking
        /// pace (MenuScene.Jog's shape), on the live body's own locomotion.
        /// </summary>
        public float MoveToward(Vector3 target, float speed, float dt)
        {
            if (!Alive) return 0f;
            if (Celeb != null && Celeb.Playing) Celeb.Cancel();
            _walking = true;
            Vector3 me = Body.Pelvis.position;
            Vector3 to = new Vector3(target.x - me.x, 0f, target.z - me.z);
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.forward;
            Body.UprightLock = true;
            Body.LocomotionEnabled = true;
            Body.MoveInput = dir * speed;
            Body.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
            Gait(dt, CupPoses.GaitAmount(speed));
            return dist;
        }

        /// <summary>End a walk: no steering, no stale stride, facing as asked.</summary>
        public void Stop(Quaternion facing)
        {
            if (!Alive) return;
            _walking = false;
            _gaitPhase = 0f;
            Body.MoveInput = Vector3.zero;
            Body.FacingRotation = facing;
            Body.ClearPoseOverrides();
            Body.SetPose(RagdollPose.Stand, 5f);
        }

        /// <summary>
        /// Every frame from the driver: hold a clean stand on the mark whenever no emote and no
        /// walk owns the pose, so a nudge or a settle never leaves him slumped.
        /// </summary>
        public void Tick()
        {
            if (!Alive) return;
            if (_walking) return;
            if (Celeb != null && Celeb.Playing) return;
            Body.MoveInput = Vector3.zero;
            Body.SetPose(RagdollPose.Stand, 4f);
        }

        // The cosmetic alternating-leg gait: the ONE walk gait every cup body uses
        // (CupPoses.WalkGait - the lineup walkers, the walk-back, the captains at the toss), so
        // the referee strides like everyone else at the same speed.
        void Gait(float dt, float amount)
        {
            Body.ClearPoseOverrides();
            CupPoses.WalkGait(Body, ref _gaitPhase, dt, amount);
            Body.SetPose(RagdollPose.Stand, 5f);
        }
    }
}
