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

        // Sitting on his backside, legs out in front. A NEGATIVE X on a thigh throws its lower
        // end forward (the same sign RaiseLeg uses to lift a leg), so -88 puts the thighs flat
        // out ahead; the knees stay a hair short of locked and the heels rest with the toes up.
        // Z is the LATERAL axis: +Z swings a limb toward his RIGHT and -Z toward his LEFT, so an
        // OUTWARD spread is -Z on a left limb and +Z on a right one. (The KeeperPose header used
        // to state that backwards; it now carries the derivation.) The pelvis entry stays zero -
        // it is the free root.
        public static readonly Vector3[] Sit = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(-12f, 0f, 0f)),      // lean back off the hips
            (Bone.Head,   new Vector3(-5f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-88f, 0f, -6f)),     // legs straight out front, slight splay
            (Bone.ThighR, new Vector3(-88f, 0f, 6f)),
            (Bone.CalfL,  new Vector3(12f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(12f, 0f, 0f)),
            (Bone.FootL,  new Vector3(-18f, 0f, 0f)),      // heels down, toes up
            (Bone.FootR,  new Vector3(-18f, 0f, 0f)),
            (Bone.UpperArmL, new Vector3(32f, 0f, -22f)),  // hands propped back and a little out
            (Bone.UpperArmR, new Vector3(32f, 0f, 22f)),
            (Bone.ForearmL,  new Vector3(-10f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-10f, 0f, 0f)),
        });

        // Sliding challenge: leading leg speared straight out in front, trailing leg folded under the
        // hips, trunk reclined so he goes down onto his backside and skids. Same sign conventions the
        // Sit block derives: a NEGATIVE X throws a thigh's lower end forward, a POSITIVE X on a calf
        // folds the knee, and +Z swings a limb toward his RIGHT (so an outward arm is -Z on the left
        // and +Z on the right). The hips are dropped separately through EmoteHeightOffset, exactly as
        // the sit does it - without that he plays this pose while still standing at full height.
        public static readonly Vector3[] Slide = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(-26f, 0f, 0f)),      // reclined back onto the hip
            (Bone.Head,   new Vector3(-10f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-82f, 0f, 4f)),      // leading leg out straight
            (Bone.CalfR,  new Vector3(8f, 0f, 0f)),        // near-locked, toe leading
            (Bone.FootR,  new Vector3(-20f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-28f, 0f, -6f)),     // trailing leg folded beneath him
            (Bone.CalfL,  new Vector3(96f, 0f, 0f)),
            (Bone.FootL,  new Vector3(-8f, 0f, 0f)),
            (Bone.UpperArmL, new Vector3(18f, 0f, -38f)),  // arms out wide, riding the slide
            (Bone.UpperArmR, new Vector3(18f, 0f, 38f)),
            (Bone.ForearmL,  new Vector3(-16f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-16f, 0f, 0f)),
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
