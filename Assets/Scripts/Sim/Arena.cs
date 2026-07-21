using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Builds the greybox training-ground arena: pitch, penalty box markings, goal
    /// with a net-mouth trigger, and boundary walls to keep the ball in play. Returns
    /// the pieces the rest of the game needs to reference.
    /// </summary>
    public static class Arena
    {
        public struct Refs
        {
            public Goal goal;
            public Transform goalCenter;
            public FlexNet net;   // wire the ball into this after the ball is created
        }

        // boundaryWalls: when false, the side/back invisible walls are skipped (open field of
        // play). Striker mode passes false so the pitch is open; the net backstops still stop
        // shots that actually go in the goal mouth.
        public static Refs Build(Transform root, bool boundaryWalls = true)
        {
            var refs = new Refs();

            var line  = Make.Glow(new Color(0.9f, 0.9f, 0.9f));
            var post  = Make.Mat(Color.white, 0.2f);
            var wall  = Make.Mat(new Color(0.3f, 0.3f, 0.34f, 1f), 0.05f);

            // NOTE: the ground plane and pitch line markings are built by PitchBuilder now
            // (the full regulation pitch). Arena used to build its own small pitch + box
            // markings at y=0, which sat COPLANAR with PitchBuilder's and Z-fought (the
            // white flickering streaks). Arena now only builds the goal, net, backstops,
            // and boundary walls.

            // Goal
            var goalRoot = Make.Empty("Goal", SimConfig.GoalCenter, root).transform;
            refs.goalCenter = goalRoot;
            float gw = SimConfig.GoalWidth, gh = SimConfig.GoalHeight, gd = SimConfig.GoalDepth;
            float postR = 0.07f;
            var frameMat = Make.Mat(Color.white, 0.3f);
            // Bouncy physics for the round frame -> fun deflections off woodwork.
            var woodwork = Make.PhysMat("Post", 0.75f, 0.3f, 0.3f);

            // Cylindrical uprights (along Y) and crossbar (along X) for round bounces.
            Make.Cylinder("PostL", postR, gh, SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("PostR", postR, gh, SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("Bar", postR, gw + postR * 2f, SimConfig.GoalCenter + new Vector3(0f, gh, 0f), 0, frameMat, goalRoot, woodwork);
            // Back frame (visual depth), also cylindrical.
            Make.Cylinder("BackPostL", postR, gh, SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, gd), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("BackPostR", postR, gh, SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh * 0.5f, gd), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("RailL", postR * 0.7f, gd, SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh, gd * 0.5f), 2, frameMat, goalRoot, woodwork);
            Make.Cylinder("RailR", postR * 0.7f, gd, SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh, gd * 0.5f), 2, frameMat, goalRoot, woodwork);

            // (Goal-line marking is drawn by PitchBuilder as part of the full pitch.)

            // See-through flexible net wrapping back + sides + top, rendered as net
            // strings (line grid) with an unlit material so it never shades to black.
            var netMat = Make.Unlit(new Color(0.92f, 0.92f, 0.98f, 1f));
            var netGo = new GameObject("FlexNet");
            netGo.transform.SetParent(goalRoot, false);
            netGo.transform.position = SimConfig.GoalCenter;   // goal-local origin at the line centre
            netGo.transform.rotation = Quaternion.identity;
            netGo.AddComponent<MeshFilter>();
            netGo.AddComponent<MeshRenderer>();
            var flex = netGo.AddComponent<FlexNet>();
            flex.Build(gw, gh, gd, SimConfig.NetCols, SimConfig.NetRows, netMat);
            refs.net = flex;

            // Invisible backstops (the net mesh is visual only) so shots that go IN the
            // mouth stop in the goal: back, both sides, and top of the goal box.
            // Minimum bounce-combine so the net kills the rebound (otherwise the ball's
            // own 0.55 bounce wins the Maximum combine and it springs back). The ball
            // then deadens into the net and rolls down instead of pinging off.
            var netPhys = Make.PhysMat("Net", 0f, 0.95f, 0.95f, PhysicsMaterialCombine.Minimum);
            MakeInvisibleSolid(goalRoot, new Vector3(gw, gh, 0.06f), SimConfig.GoalCenter + new Vector3(0f, gh * 0.5f, gd), netPhys);
            MakeInvisibleSolid(goalRoot, new Vector3(0.06f, gh, gd), SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, gd * 0.5f), netPhys);
            MakeInvisibleSolid(goalRoot, new Vector3(0.06f, gh, gd), SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh * 0.5f, gd * 0.5f), netPhys);
            MakeInvisibleSolid(goalRoot, new Vector3(gw, 0.06f, gd), SimConfig.GoalCenter + new Vector3(0f, gh, gd * 0.5f), netPhys);

            // Goal trigger: a thin slab spanning the mouth just inside the line.
            var trigger = Make.Box("GoalTrigger", new Vector3(gw, gh, 0.3f),
                                    SimConfig.GoalCenter + new Vector3(0f, gh * 0.5f, 0.2f), null, goalRoot, collider: true);
            var tcol = trigger.GetComponent<Collider>();
            tcol.isTrigger = true;
            var mr = trigger.GetComponent<Renderer>();
            if (mr != null) Object.Destroy(mr); // invisible
            refs.goal = trigger.AddComponent<Goal>();

            // Side + back-of-player boundary walls only. The GOAL END (+Z) is left OPEN
            // so wide / high / long shots sail out of bounds behind the goal and land
            // on the extended ground (counted as a miss). Only the net backstops stop a
            // ball that actually goes in the mouth. Skipped entirely when boundaryWalls is
            // false (striker mode = open field of play).
            if (boundaryWalls)
            {
                float wallH = 6f, t = 0.4f;
                float halfW = SimConfig.FieldWidth * 0.5f, halfL = SimConfig.FieldLength * 0.5f;
                MakeWall(root, wall, new Vector3(0f, wallH * 0.5f, -halfL - t * 0.5f), new Vector3(SimConfig.FieldWidth + t * 2f, wallH, t));
                MakeWall(root, wall, new Vector3(-halfW - t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, SimConfig.FieldLength));
                MakeWall(root, wall, new Vector3(halfW + t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, SimConfig.FieldLength));
            }

            return refs;
        }

        static void Line(Transform root, Material m, Vector3 pos, Vector3 size)
        {
            var go = Make.Box("Line", size, pos, m, root, collider: false);
        }

        static void MakeInvisibleSolid(Transform root, Vector3 size, Vector3 pos, PhysicsMaterial phys)
        {
            var go = Make.Box("Backstop", size, pos, null, root, collider: true);
            var r = go.GetComponent<Renderer>(); if (r != null) Object.Destroy(r);
            go.GetComponent<Collider>().material = phys;
            go.AddComponent<NetBackstop>();   // ball kills its rebound on contact (see BallController)
        }

        static void MakeWall(Transform root, Material m, Vector3 pos, Vector3 size)
        {
            var go = Make.Box("Wall", size, pos, m, root, collider: true);
            go.GetComponent<Collider>().material = Make.PhysMat("Wall", 0.35f, 0.4f, 0.4f);
            // Invisible: keep the collider (ball stays in play) but drop the renderer
            // so you can always see through the boundary.
            var r = go.GetComponent<Renderer>();
            if (r != null) Object.Destroy(r);
        }
    }
}
