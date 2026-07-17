using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player control of the active-ragdoll striker: move around the target area,
    /// jump, and trigger a bicycle kick (jump + backward rotation + kicking-leg whip).
    ///
    /// Movement is world-relative and the body faces the direction you move, so YOU
    /// drive the turn: run into the box, then turn away from goal to line up the bike.
    /// The follow camera reads <see cref="Yaw"/> and orbits to sit behind you, so the
    /// view turns with the character. Facing is locked while the bicycle is live.
    /// </summary>
    public class Striker : MonoBehaviour
    {
        GameInput _input;
        ActiveRagdoll _ragdoll;

        public bool ControlEnabled = true;
        public float BicycleTimer { get; private set; }   // >0 while the kick is live
        public bool BicycleActive => BicycleTimer > 0f;

        float _facingYaw;
        public float Yaw => _facingYaw;                    // degrees, for the follow camera

        public void Init(GameInput input, ActiveRagdoll ragdoll)
        {
            _input = input;
            _ragdoll = ragdoll;
            _facingYaw = ragdoll.FacingRotation.eulerAngles.y;
        }

        void Update()
        {
            if (BicycleTimer > 0f)
                BicycleTimer = Mathf.Max(0f, BicycleTimer - Time.deltaTime);
        }

        /// <summary>Called by GameManager each frame while the striker is in play.</summary>
        public void Tick()
        {
            if (!ControlEnabled || _ragdoll.Pelvis == null) return;

            // World-relative movement: W/Up = +Z (toward goal), D/Right = +X.
            Vector2 mv = _input.Move;
            Vector3 wish = Vector3.ClampMagnitude(new Vector3(mv.x, 0f, mv.y), 1f);
            _ragdoll.MoveInput = wish * SimConfig.StrikerMoveSpeed;

            // Face the way you are moving (you steer the body); hold facing when still
            // and while the bicycle is inverting the torso.
            if (wish.sqrMagnitude > 0.02f && !BicycleActive)
            {
                float targetYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, targetYaw, 300f * Time.deltaTime);
            }
            if (!BicycleActive)
                _ragdoll.FacingRotation = Quaternion.Euler(0f, _facingYaw, 0f);

            // Jump (also preloads before a bike kick).
            if (_input.JumpPressed && _ragdoll.IsGrounded && !BicycleActive)
            {
                _ragdoll.SetPose(RagdollPose.Load, 14f);
                _ragdoll.AddImpulseToPelvis(Vector3.up * SimConfig.JumpImpulse);
                _ragdoll.SetPose(RagdollPose.Stand, 4f);
            }

            // Bicycle kick.
            if (_input.KickPressed && !BicycleActive)
                StartBicycle();

            if (BicycleActive)
                ManageBicycle();
        }

        void StartBicycle()
        {
            BicycleTimer = SimConfig.BicycleWindow;

            if (_ragdoll.IsGrounded)
                _ragdoll.AddImpulseToPelvis(Vector3.up * SimConfig.JumpImpulse * 1.05f);

            // Backward angular impulse about the character's right axis -> body
            // rotates backward / upside down for the bicycle.
            Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
            _ragdoll.AddTorqueToPelvis(-axis * SimConfig.BicycleBackSpin);

            _ragdoll.BalanceEnabled = false;   // let the pelvis invert freely
            _ragdoll.SetPose(RagdollPose.Bicycle, 16f);
        }

        void ManageBicycle()
        {
            Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
            if (BicycleTimer > SimConfig.BicycleWindow * 0.4f)
                _ragdoll.AddTorqueToPelvis(-axis * SimConfig.BicycleBackSpin * 0.15f);

            if (BicycleTimer <= 0.001f)
                RecoverFromBicycle();
        }

        void RecoverFromBicycle()
        {
            _ragdoll.BalanceEnabled = true;
            _facingYaw = _ragdoll.FacingRotation.eulerAngles.y; // resync so camera doesn't snap
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        public void ForceRecover()
        {
            BicycleTimer = 0f;
            RecoverFromBicycle();
        }
    }
}
