using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A self-contained scrimmage pitch: a rectangular field centred on the origin with a
    /// goal at EACH end (+Z and -Z), a see-through net in each, and invisible boundary
    /// walls on all four sides so the ball never leaves play. Sized to the team count.
    ///
    /// Independent of the single-goal training Arena so none of the existing modes change.
    /// Scoring is done geometrically by ScrimmageGame (GoalAt), not by trigger callbacks.
    /// </summary>
    public static class ScrimmageArena
    {
        public struct Refs
        {
            public float halfLength;   // |z| to each goal line
            public float halfWidth;    // |x| to each touchline
            public Vector3 homeGoalCenter;  // +Z goal (Home attacks this)
            public Vector3 awayGoalCenter;  // -Z goal (Away attacks this)
        }

        public static Refs Build(Transform root, int perSide)
        {
            var refs = new Refs
            {
                halfLength = SimConfig.ScrimHalfLength(perSide),
                halfWidth  = SimConfig.ScrimHalfWidth(perSide),
            };
            float hl = refs.halfLength, hw = refs.halfWidth;
            refs.homeGoalCenter = new Vector3(0f, 0f, hl);
            refs.awayGoalCenter = new Vector3(0f, 0f, -hl);

            // Ground plane (own, so it doesn't rely on the training PitchBuilder).
            var grassMat = Make.Mat(new Color(0.20f, 0.42f, 0.20f), 0.05f);
            var ground = Make.Box("ScrimGround", new Vector3(hw * 2f + 8f, 0.4f, hl * 2f + 8f),
                                  new Vector3(0f, -0.2f, 0f), grassMat, root, collider: true);
            ground.GetComponent<Collider>().material = Make.PhysMat("Turf", 0.1f, 0.6f, 0.6f);

            // Centre + halfway markings (thin bright boxes, no collider).
            var line = Make.Mat(new Color(0.9f, 0.9f, 0.9f), 0.3f);
            Make.Box("Half", new Vector3(hw * 2f, 0.02f, 0.15f), new Vector3(0f, 0.02f, 0f), line, root, collider: false);
            var circ = Make.Box("Centre", new Vector3(2.4f, 0.02f, 2.4f), new Vector3(0f, 0.02f, 0f), line, root, collider: false);

            // A goal at each end.
            BuildGoal(root, refs.homeGoalCenter, faceNegZ: true);   // mouth opens toward -Z (play)
            BuildGoal(root, refs.awayGoalCenter, faceNegZ: false);  // mouth opens toward +Z (play)

            // Boundary walls. The two touchlines (along Z) are solid. The two GOAL-END walls
            // must NOT block the goal mouth, or shots can never score: build each end wall as
            // two segments with a gap the width of the goal mouth in the middle (the net's own
            // backstops stop a ball that actually goes in). Walls sit just outside the lines.
            var wallPhys = Make.PhysMat("Wall", 0.3f, 0.4f, 0.4f);
            float wallH = 6f, t = 0.4f;
            // Touchlines (+X / -X): full length.
            MakeWall(root, wallPhys, new Vector3(hw + t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, hl * 2f + t * 2f));
            MakeWall(root, wallPhys, new Vector3(-hw - t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, hl * 2f + t * 2f));
            // Goal-end walls (+Z / -Z): split around a gap slightly wider than the goal mouth.
            float gap = SimConfig.GoalWidth + 1.0f;   // clearance so a shot on target isn't clipped
            float segLen = (hw * 2f + t * 2f - gap) * 0.5f;
            if (segLen > 0.1f)
            {
                float segCenter = gap * 0.5f + segLen * 0.5f;
                foreach (float zEnd in new[] { hl + t * 0.5f, -hl - t * 0.5f })
                foreach (float xSign in new[] { 1f, -1f })
                    MakeWall(root, wallPhys, new Vector3(xSign * segCenter, wallH * 0.5f, zEnd), new Vector3(segLen, wallH, t));
            }

            return refs;
        }

        // Woodwork frame + net backstops for a goal at `center`. The mouth faces the pitch;
        // faceNegZ = the depth extends toward +Z (goal at +Z end), else toward -Z.
        static void BuildGoal(Transform root, Vector3 center, bool faceNegZ)
        {
            float gw = SimConfig.GoalWidth, gh = SimConfig.GoalHeight, gd = SimConfig.GoalDepth;
            float depthSign = faceNegZ ? 1f : -1f;   // net box extends AWAY from the pitch
            float postR = 0.07f;
            var frameMat = Make.Mat(Color.white, 0.3f);
            var woodwork = Make.PhysMat("Post", 0.6f, 0.3f, 0.3f);
            var goalRoot = Make.Empty(faceNegZ ? "HomeGoal" : "AwayGoal", center, root).transform;

            Make.Cylinder("PostL", postR, gh, center + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("PostR", postR, gh, center + new Vector3(gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("Bar", postR, gw + postR * 2f, center + new Vector3(0f, gh, 0f), 0, frameMat, goalRoot, woodwork);

            // See-through net (visual). FlexNet is authored mouth-toward -Z; rotate 180 for
            // the -Z goal so its pocket faces the pitch.
            var netMat = Make.Unlit(new Color(0.92f, 0.92f, 0.98f, 1f));
            var netGo = new GameObject("Net");
            netGo.transform.SetParent(goalRoot, false);
            netGo.transform.position = center;
            netGo.transform.rotation = faceNegZ ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            netGo.AddComponent<MeshFilter>();
            netGo.AddComponent<MeshRenderer>();
            netGo.AddComponent<FlexNet>().Build(gw, gh, gd, SimConfig.NetCols, SimConfig.NetRows, netMat);

            // Invisible backstops behind the mouth (back, sides, top) so a ball that enters
            // stops in the goal. Minimum bounce combine so it deadens (see BallController).
            var netPhys = Make.PhysMat("Net", 0f, 0.95f, 0.95f, PhysicsMaterialCombine.Minimum);
            float bz = depthSign * gd;              // back plane z-offset
            float mz = depthSign * gd * 0.5f;       // mid-depth z-offset
            MakeBackstop(goalRoot, new Vector3(gw, gh, 0.06f), center + new Vector3(0f, gh * 0.5f, bz), netPhys);
            MakeBackstop(goalRoot, new Vector3(0.06f, gh, gd), center + new Vector3(-gw * 0.5f, gh * 0.5f, mz), netPhys);
            MakeBackstop(goalRoot, new Vector3(0.06f, gh, gd), center + new Vector3(gw * 0.5f, gh * 0.5f, mz), netPhys);
            MakeBackstop(goalRoot, new Vector3(gw, 0.06f, gd), center + new Vector3(0f, gh, mz), netPhys);
        }

        static void MakeBackstop(Transform root, Vector3 size, Vector3 pos, PhysicsMaterial phys)
        {
            var go = Make.Box("Backstop", size, pos, null, root, collider: true);
            var r = go.GetComponent<Renderer>(); if (r != null) Object.Destroy(r);
            go.GetComponent<Collider>().material = phys;
            go.AddComponent<NetBackstop>();
        }

        static void MakeWall(Transform root, PhysicsMaterial phys, Vector3 pos, Vector3 size)
        {
            var go = Make.Box("Wall", size, pos, null, root, collider: true);
            go.GetComponent<Collider>().material = phys;
            var r = go.GetComponent<Renderer>(); if (r != null) Object.Destroy(r);   // invisible
        }
    }
}
