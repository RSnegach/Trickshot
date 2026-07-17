using UnityEngine;

namespace Trickshot
{
    /// <summary>Identifies a body part. Index order is used for the pose tables below.</summary>
    public enum Bone
    {
        Pelvis = 0,
        Torso  = 1,
        Head   = 2,
        ThighL = 3,
        ThighR = 4,   // right leg is the kicking leg
        CalfL  = 5,
        CalfR  = 6,
        FootL  = 7,
        FootR  = 8,
        UpperArmL = 9,
        UpperArmR = 10,
        ForearmL = 11,
        ForearmR = 12,
        Count  = 13
    }

    /// <summary>
    /// Named target poses for the active ragdoll's target skeleton.
    ///
    /// A pose is a per-bone *local* rotation offset (in that bone's local space)
    /// applied on top of the bone's rest local rotation:
    ///     targetLocal = restLocal * Euler(offset)
    ///
    /// Build convention (see RagdollBuilder): the character is authored upright
    /// along +Y and facing +Z. A bone's local +X points to the character's right,
    /// local +Y points along the limb toward its child (down the leg), local +Z
    /// forward. So a positive X rotation pitches a limb's lower end backward
    /// (foot swings behind), which is how a knee folds.
    ///
    /// The pelvis entry is unused for joint drives (the pelvis is the free root and
    /// is stabilised by a balance torque instead); it is kept so indexing lines up.
    /// </summary>
    public static class RagdollPose
    {
        // offsets[pose][bone] = Euler degrees
        public static readonly Vector3[] Stand = New();

        public static readonly Vector3[] Load = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(12f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-35f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-35f, 0f, 0f)),
            (Bone.CalfL,  new Vector3(60f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(60f, 0f, 0f)),
            (Bone.FootL,  new Vector3(-15f, 0f, 0f)),
            (Bone.FootR,  new Vector3(-15f, 0f, 0f)),
        });

        // Bicycle: torso leans hard backward, right (kicking) leg whips up and over,
        // left leg tucks for the scissor. The pelvis also receives a backward angular
        // impulse from the Striker, so this pose plus that spin reads as a bicycle kick.
        public static readonly Vector3[] Bicycle = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(-55f, 0f, 0f)),
            (Bone.Head,   new Vector3(-25f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-100f, 0f, 0f)),  // kicking thigh drives up/forward
            (Bone.CalfR,  new Vector3(15f, 0f, 0f)),    // near-straight to whip through the ball
            (Bone.FootR,  new Vector3(-25f, 0f, 0f)),   // pointed
            (Bone.ThighL, new Vector3(-55f, 0f, 0f)),   // support leg tucks
            (Bone.CalfL,  new Vector3(75f, 0f, 0f)),
        });

        static Vector3[] New()
        {
            return new Vector3[(int)Bone.Count]; // all zero
        }

        static Vector3[] Set(Vector3[] arr, (Bone bone, Vector3 euler)[] entries)
        {
            foreach (var e in entries) arr[(int)e.bone] = e.euler;
            return arr;
        }
    }
}
