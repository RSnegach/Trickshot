using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Math helpers for driving an active ragdoll.
    ///
    /// ConfigurableJoint.targetRotation is expressed in the joint's own axis
    /// space relative to the joint's rotation at creation time, which is why you
    /// cannot just assign a local rotation to it. The SetTargetRotationLocal
    /// helper below is the well established community solution (originally posted
    /// on the Unity forums) that converts a desired *local* rotation of the
    /// jointed body into the correct targetRotation value.
    /// </summary>
    public static class JointMath
    {
        /// <summary>
        /// Sets a ConfigurableJoint's target rotation to a rotation expressed
        /// relative to the connected body's frame (i.e. the local rotation the
        /// child should have relative to its parent).
        /// </summary>
        /// <param name="joint">The joint to drive.</param>
        /// <param name="targetLocalRotation">Desired local rotation of the jointed body relative to its parent.</param>
        /// <param name="startLocalRotation">The jointed body's local rotation captured when the joint was created.</param>
        public static void SetTargetRotationLocal(this ConfigurableJoint joint,
                                                  Quaternion targetLocalRotation,
                                                  Quaternion startLocalRotation)
        {
            if (joint.configuredInWorldSpace)
            {
                Debug.LogError("SetTargetRotationLocal should not be used with joints that are configured in world space.");
            }
            SetTargetRotationInternal(joint, targetLocalRotation, startLocalRotation, Space.Self);
        }

        static void SetTargetRotationInternal(ConfigurableJoint joint,
                                              Quaternion targetRotation,
                                              Quaternion startRotation,
                                              Space space)
        {
            // Calculate the rotation expressed by the joint's axis and secondary axis.
            var right = joint.axis;
            var forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            var up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            // Transform into world space.
            Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace);

            // Counter-rotate and apply the new local rotation.
            // The connectedBody path (Space.Self) uses the inverse; the world
            // anchor path (Space.World) does not.
            if (space == Space.World)
            {
                resultRotation *= startRotation * Quaternion.Inverse(targetRotation);
            }
            else
            {
                resultRotation *= Quaternion.Inverse(targetRotation) * startRotation;
            }

            // Transform back into joint space.
            resultRotation *= worldToJointSpace;

            joint.targetRotation = resultRotation;
        }

        /// <summary>
        /// Applies a critically-damped stabilising torque that drives a rigidbody's
        /// orientation toward targetRot. Used to keep the pelvis upright and facing
        /// a chosen direction. freq is roughly how fast it corrects (Hz), damping ~1
        /// is critically damped. Torque is applied as Acceleration so it is mass
        /// independent and easy to tune.
        /// </summary>
        public static void DriveTowardRotation(Rigidbody rb, Quaternion targetRot, float freq, float damping)
        {
            float kp = (6f * freq) * (6f * freq) * 0.25f;
            float kd = 4.5f * freq * damping;

            Quaternion delta = targetRot * Quaternion.Inverse(rb.rotation);
            // Ensure shortest path.
            if (delta.w < 0f)
            {
                delta.x = -delta.x; delta.y = -delta.y; delta.z = -delta.z; delta.w = -delta.w;
            }
            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsInfinity(axis.x) || float.IsNaN(axis.x)) return;
            if (angleDeg > 180f) angleDeg -= 360f;

            // angular error as a rotation vector (radians)
            Vector3 angularError = axis.normalized * (angleDeg * Mathf.Deg2Rad);
            Vector3 torque = kp * angularError - kd * rb.angularVelocity;
            rb.AddTorque(torque, ForceMode.Acceleration);
        }
    }
}
