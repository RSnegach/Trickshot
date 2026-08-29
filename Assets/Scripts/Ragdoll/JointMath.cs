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
        /// Applies a PD stabilising torque that drives a rigidbody's orientation
        /// toward targetRot. Used to keep the pelvis upright and facing a chosen
        /// direction. freq is roughly how fast it corrects (Hz); damping is the
        /// damping ratio (1 = critically damped, i.e. fastest settle without
        /// overshoot; &lt;1 wobbles more). Torque is applied as Acceleration so it is
        /// mass independent and easy to tune - but read that last claim narrowly, and see inertiaMul.
        /// </summary>
        /// <param name="inertiaMul">
        /// Per-axis gain correction, in the body's LOCAL frame. Null or one changes nothing.
        ///
        /// Mass independence holds for a FREE body, because ForceMode.Acceleration makes Unity
        /// multiply the commanded acceleration by that body's own inertia tensor. It does NOT hold for
        /// a body joint-welded to others, which is the only way this is ever called. There the torque
        /// is still sized from the driven body's tensor but has
        /// to turn the whole assembly, so the achieved acceleration is the commanded one times
        /// I_driven / I_assembly, per axis, and BOTH gains shrink with it. Damping ratio scales as the
        /// square root of that. Measured on the yaw axis at the default build of each: a biped assembly
        /// is 6.6x its pelvis and damps at 0.330 against a nominal 0.85, which is livable; a horse is
        /// 135x and damps at 0.073, a 7.6-second sway that only loses a third of its amplitude per
        /// swing; an elephant is 70x and damps at 0.102.
        ///
        /// Passing the inverse ratio restores the intended response. Per axis rather than scalar
        /// because ForceMode.Acceleration resolves componentwise and a limbed body's inertia is
        /// strongly anisotropic. BodyLayout.RootDriveMul is what computes it.
        /// </param>
        public static void DriveTowardRotation(Rigidbody rb, Quaternion targetRot, float freq, float damping,
                                              Vector3? inertiaMul = null)
        {
            // kp = wn^2 with wn = 3*freq; critical kd = 2*wn = 6*freq, scaled by the
            // damping ratio so 'damping' means what the docstring says.
            float kp = (6f * freq) * (6f * freq) * 0.25f;
            float kd = 6f * freq * damping;

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

            if (inertiaMul.HasValue)
            {
                // Scale in the body's own frame, since the correction was derived from body-frame
                // inertia and the body yaws freely. Applying it to the whole expression rather than to
                // kp and kd separately is equivalent, both terms being linear in it, and keeps the
                // damping ratio at whatever `damping` asked for.
                Vector3 local = Quaternion.Inverse(rb.rotation) * torque;
                local.Scale(inertiaMul.Value);
                torque = rb.rotation * local;
            }

            rb.AddTorque(torque, ForceMode.Acceleration);
        }
    }
}
