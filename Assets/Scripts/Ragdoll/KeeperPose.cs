using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Goalkeeper save poses, in the same per-bone Euler-offset format as RagdollPose.
    /// Convention (see RagdollPose): local +X = character's right; +X rotation folds a
    /// limb's lower end backward.
    ///
    /// Z IS THE LATERAL AXIS AND ITS SIGN IS THE OPPOSITE OF WHAT THIS HEADER USED TO CLAIM.
    /// A Z rotation acts about the bone's local forward, and Unity's positive rotation about +Z
    /// takes +X toward +Y, so it takes a DOWNWARD hanging limb (-Y) toward +X. That means:
    ///
    ///     +Z swings a hanging arm or leg toward the character's RIGHT, -Z toward his LEFT.
    ///
    /// So OUTWARD is -Z on a LEFT limb and +Z on a RIGHT limb. The old header had this inverted
    /// and asserted it had been checked on screen, which is how every pose in this file ended up
    /// authored with its lateral signs mirrored: limbs swung ACROSS the centreline instead of away
    /// from it. Ready's +40 / -40 upper arms were the visible case (the keeper stood with his arms
    /// crossed in front of him), but the whole save/dive set had the same fault and has now been
    /// flipped to match. EVERY pose below obeys the rule above, so the names mean what they say:
    ///
    /// - SaveLeft: drop onto the RIGHT knee, shoot the LEFT leg out low to the LEFT, both arms
    ///   thrown out to their own sides. Covers his left post.
    /// - SaveRight: the mirror of it.
    /// - Split: splayed star - both legs out sideways, both arms thrown out, making himself big.
    /// - Dive / DiveHigh / Jump: symmetric, so they read the same either way he goes; their Z only
    ///   decides whether the limbs spread apart or scissor across each other.
    ///
    /// Because the names are now honest, a selector picks the pose matching the side it wants. It
    /// still has to convert a WORLD-X side into his own left/right first, since a keeper facing +Z
    /// has his local right on world -X (see Goalkeeper.RightSign).
    /// </summary>
    public static class KeeperPose
    {
        static Vector3[] New() => new Vector3[(int)Bone.Count];
        static Vector3[] Set(Vector3[] a, (Bone b, Vector3 e)[] xs) { foreach (var x in xs) a[(int)x.b] = x.e; return a; }

        // Block to the LEFT: lean/leg out left, but BOTH arms spread wide (each to its
        // own side) to cover as much as possible.
        public static readonly Vector3[] SaveLeft = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(0f, 0f, -22f)),   // lean toward the left
            (Bone.ThighR, new Vector3(70f, 0f, 0f)),    // right knee down (fold under)
            (Bone.CalfR,  new Vector3(95f, 0f, 0f)),
            (Bone.ThighL, new Vector3(0f, 0f, -78f)),   // left leg splays out to the left
            (Bone.CalfL,  new Vector3(0f, 0f, -10f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, -130f)),// both arms thrown out to their own sides
            (Bone.ForearmL,  new Vector3(0f, 0f, -12f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 130f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, 12f)),
        });

        // Block to the RIGHT: mirror of SaveLeft (both arms out).
        public static readonly Vector3[] SaveRight = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(0f, 0f, 22f)),    // lean toward the right
            (Bone.ThighL, new Vector3(70f, 0f, 0f)),    // left knee down (fold under)
            (Bone.CalfL,  new Vector3(95f, 0f, 0f)),
            (Bone.ThighR, new Vector3(0f, 0f, 78f)),    // right leg splays out to the right
            (Bone.CalfR,  new Vector3(0f, 0f, 10f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 130f)),// both arms thrown out to their own sides
            (Bone.ForearmR,  new Vector3(0f, 0f, 12f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, -130f)),
            (Bone.ForearmL,  new Vector3(0f, 0f, -12f)),
        });

        // Splayed split - big star shape, arms out to both sides.
        public static readonly Vector3[] Split = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.ThighL, new Vector3(0f, 0f, -85f)),
            (Bone.ThighR, new Vector3(0f, 0f, 85f)),
            (Bone.CalfL,  new Vector3(0f, 0f, -8f)),
            (Bone.CalfR,  new Vector3(0f, 0f, 8f)),
            (Bone.UpperArmL, new Vector3(0f, 0f, -135f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 135f)),
            (Bone.ForearmL,  new Vector3(0f, 0f, -10f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, 10f)),
        });

        // Ready crouch: knees slightly bent, arms out a little, alert.
        public static readonly Vector3[] Ready = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,  new Vector3(10f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-22f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-22f, 0f, 0f)),
            (Bone.CalfL,  new Vector3(40f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(40f, 0f, 0f)),
            // OUT to his own sides. -Z on the left arm and +Z on the right arm is OUTWARD (see the
            // header): the old +40 / -40 swung BOTH arms across the chest and read on screen as the
            // keeper standing with his arms crossed in front of him.
            (Bone.UpperArmL, new Vector3(0f, 0f, -40f)),
            (Bone.UpperArmR, new Vector3(0f, 0f, 40f)),
            // Elbows only lightly bent. A -45 fold on top of arms that are already out swings the
            // hands back in front of the sternum, which put the crossed look straight back.
            (Bone.ForearmL,  new Vector3(-22f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-22f, 0f, 0f)),
        });

        // Straight jump: both arms punched straight overhead, body upright.
        public static readonly Vector3[] Jump = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(-170f, 0f, -8f)),
            (Bone.UpperArmR, new Vector3(-170f, 0f, 8f)),
            (Bone.ForearmL,  new Vector3(-5f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-5f, 0f, 0f)),
            (Bone.ThighL, new Vector3(-8f, 0f, 0f)),
            (Bone.ThighR, new Vector3(-8f, 0f, 0f)),
        });

        // Dive: arms SPREAD WIDE to both sides to cover area, legs straight together.
        // The KeeperController rolls the whole body horizontal, so relative to the
        // laid-out torso these arms reach out to make a big star. Used by the LOW dash dive.
        public static readonly Vector3[] Dive = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(0f, 0f, -150f)), // left arm straight out LEFT
            (Bone.UpperArmR, new Vector3(0f, 0f, 150f)),  // right arm straight out RIGHT
            (Bone.ForearmL,  new Vector3(0f, 0f, -12f)),
            (Bone.ForearmR,  new Vector3(0f, 0f, 12f)),
            (Bone.ThighL, new Vector3(5f, 0f, -6f)),      // legs long, slightly spread
            (Bone.ThighR, new Vector3(5f, 0f, 6f)),
            (Bone.CalfL,  new Vector3(5f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(5f, 0f, 0f)),
        });

        // High dive: both arms punched OVERHEAD (past the head, like the Jump pose) and only
        // modestly apart, rather than the wide star. Same overhead X as Jump (-170 flips the
        // hanging arm to point up); the ±Z gives a clean gap between them without crossing. Legs
        // as in Dive. The KeeperController drives this as the high dive's BASE pose (not an
        // additive override), so the arms are genuinely overhead instead of the wide arms tilted.
        public static readonly Vector3[] DiveHigh = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.UpperArmL, new Vector3(-170f, 0f, -26f)), // both up past the head, a modest gap apart
            (Bone.UpperArmR, new Vector3(-170f, 0f, 26f)),
            (Bone.ForearmL,  new Vector3(-6f, 0f, 0f)),      // forearms extend straight up
            (Bone.ForearmR,  new Vector3(-6f, 0f, 0f)),
            (Bone.ThighL, new Vector3(5f, 0f, -6f)),
            (Bone.ThighR, new Vector3(5f, 0f, 6f)),
            (Bone.CalfL,  new Vector3(5f, 0f, 0f)),
            (Bone.CalfR,  new Vector3(5f, 0f, 0f)),
        });

        // Holding a gathered ball. Upper arms come forward off the ribs and the elbows fold to
        // about a right angle, which is the whole read: straight arms out front look like a
        // sleepwalker, bent ones look like they are clamping something.
        //
        // The geometry is not arbitrary. KeeperHands pins the ball KeeperHoldForward (0.42 m) in
        // front of the chest, so the hands have to meet there - hence -28 on the upper arms to
        // bring them forward and -95 on the forearms to bring the hands back in to the middle. The
        // small mirrored Z pair tucks the elbows against the ribs rather than letting them wing
        // out. Legs stay softly bent so he can still shuffle along his line while holding.
        //
        // This shape is ONLY for a ball actually gathered. The ambient stance is Ready, arms out
        // to the sides: if a keeper looks like this when he has not caught anything, the claim
        // threshold is too low, not the pose.
        public static readonly Vector3[] Hold = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,     new Vector3(6f, 0f, 0f)),      // fractionally over the ball
            (Bone.UpperArmL, new Vector3(-28f, 0f, 10f)),   // forward off the ribs, elbow tucked IN
            (Bone.UpperArmR, new Vector3(-28f, 0f, -10f)),
            (Bone.ForearmL,  new Vector3(-95f, 0f, 18f)),   // ~right angle at the elbow, hands together
            (Bone.ForearmR,  new Vector3(-95f, 0f, -18f)),
            (Bone.ThighL,    new Vector3(-14f, 0f, 0f)),
            (Bone.ThighR,    new Vector3(-14f, 0f, 0f)),
            (Bone.CalfL,     new Vector3(26f, 0f, 0f)),
            (Bone.CalfR,     new Vector3(26f, 0f, 0f)),
        });

        // Pushing up off the turf: the first half of the stumble the KeeperController runs after a
        // dive. Deep knee fold with the torso pitched over them and the arms down and forward, as
        // if the hands were still taking weight. It blends to Ready over the back half, so it only
        // ever has to look like the START of standing up rather than a finished stance.
        public static readonly Vector3[] Rise = Set(New(), new (Bone, Vector3)[]
        {
            (Bone.Torso,     new Vector3(22f, 0f, 0f)),     // folded forward over the knees
            (Bone.ThighL,    new Vector3(-50f, 0f, 0f)),
            (Bone.ThighR,    new Vector3(-50f, 0f, 0f)),
            (Bone.CalfL,     new Vector3(85f, 0f, 0f)),     // deep crouch
            (Bone.CalfR,     new Vector3(85f, 0f, 0f)),
            (Bone.UpperArmL, new Vector3(-30f, 0f, -25f)),  // hands down and OUT, taking weight
            (Bone.UpperArmR, new Vector3(-30f, 0f, 25f)),
            (Bone.ForearmL,  new Vector3(-25f, 0f, 0f)),
            (Bone.ForearmR,  new Vector3(-25f, 0f, 0f)),
        });
    }
}
