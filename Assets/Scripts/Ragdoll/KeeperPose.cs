using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Goalkeeper save poses, in the same per-bone Euler-offset format as RagdollPose.
    /// Convention (see RagdollPose): local +X = character's right; +X rotation folds a
    /// limb's lower end backward; a Z rotation swings a hanging arm/leg out sideways
    /// (+Z swings toward the character's left, -Z toward the right).
    ///
    /// - SaveLeft: drop onto the RIGHT knee, shoot the LEFT leg and LEFT arm out to the
    ///   left side (a low block covering the left post).
    /// - SaveRight: mirror (down on left knee, right leg + right arm out).
    /// - Split: splayed star - both legs out sideways, both arms thrown out to the sides,
    ///   like a keeper spreading to make himself big.
    /// </summary>
    public static class KeeperPose
    {
        static Vector3[] New() => new Vector3[(int)Bone.Count];
        static Vector3[] Set(Vector3[] a, (Bone b, Vector3 e)[] xs) { foreach (var x in xs) a[(int)x.b] = x.e; return a; }

        // Block to the LEFT: lean/leg out left, but BOTH arms spread wide (each to its
        // own side) to cover as much as possible.
        public static readonly Vector3[] SaveLeft = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(0f, 0f, 22f)),    // lean toward the left
            (Bone.ThighR, new Vector3(70f, 0f, 0f)),    // right knee down (fold under)
            (Bone.CalfR,  new Vector3(95f, 0f, 0f)),
            (Bone.ThighL, new Vector3(0f, 0f, 78f)),    // left leg splays out to the left
            (Bone.CalfL,  new Vector3(0f, 0f, 10f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, 130f)),// both arms thrown out to the sides
            (Bone.ForearmL,  new Vector3(0f, 0f, 12f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, -130f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, -12f)),
        });

        // Block to the RIGHT: mirror of SaveLeft (both arms out).
        public static readonly Vector3[] SaveRight = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(0f, 0f, -22f)),
            (Bone.ThighL, new Vector3(70f, 0f, 0f)),    // left knee down
            (Bone.CalfL,  new Vector3(95f, 0f, 0f)),
            (Bone.ThighR, new Vector3(0f, 0f, -78f)),   // right leg splays out right
            (Bone.CalfR,  new Vector3(0f, 0f, -10f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, -130f)),// both arms thrown out to the sides
            (Bone.ForearmR,  new Vector3(0f, 0f, -12f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, 130f)),
            (Bone.ForearmL,  new Vector3(0f, 0f, 12f)),
        });

        // Splayed split - big star shape, arms out to both sides.
        public static readonly Vector3[] Split = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.ThighL, new Vector3(0f, 0f, 85f)),
            (Bone.ThighR, new Vector3(0f, 0f, -85f)),
            (Bone.CalfL,  new Vector3(0f, 0f, 8f)),
            (Bone.CalfR,  new Vector3(0f, 0f, -8f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, 135f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, -135f)),
            (Bone.ForearmL,  new Vector3(0f, 0f, 10f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, -10f)),
        });

        // Ready crouch: knees slightly bent, arms out a little, alert.
        public static readonly Vector3[] Ready = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(10f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-22f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-22f, 0f, 0f)),
            (Bone.CalfL,  new Vector3(40f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(40f, 0f, 0f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, 40f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, -40f)),
            (Bone.ForearmL,  new Vector3(-45f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-45f, 0f, 0f)),
        });

        // Straight jump: both arms punched straight overhead, body upright.
        public static readonly Vector3[] Jump = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(-170f, 0f, 8f)),
            (Bone.UpperArmR, new Vector3(-170f, 0f, -8f)),
            (Bone.ForearmL,  new Vector3(-5f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-5f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-8f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-8f, 0f, 0f)),
        });

        // Dive: arms SPREAD WIDE to both sides to cover area, legs straight together.
        // The KeeperController rolls the whole body horizontal, so relative to the
        // laid-out torso these arms reach out to make a big star.
        public static readonly Vector3[] Dive = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(0f, 0f, 150f)),  // left arm straight out left
            (Bone.UpperArmR, new Vector3(0f, 0f, -150f)), // right arm straight out right
            (Bone.ForearmL,  new Vector3(0f, 0f, 12f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, -12f)),
            (Bone.ThighL, new Vector3(5f, 0f, 6f)),       // legs long, slightly spread
            (Bone.ThighR, new Vector3(5f, 0f, -6f)),
            (Bone.CalfL,  new Vector3(5f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(5f, 0f, 0f)),
        });
    }
}
