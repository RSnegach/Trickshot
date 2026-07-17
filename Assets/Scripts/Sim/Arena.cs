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
        }

        public static Refs Build(Transform root)
        {
            var refs = new Refs();

            var grass = Make.Mat(new Color(0.24f, 0.42f, 0.24f), 0.05f);
            var line  = Make.Glow(new Color(0.9f, 0.9f, 0.9f));
            var post  = Make.Mat(Color.white, 0.2f);
            var wall  = Make.Mat(new Color(0.3f, 0.3f, 0.34f, 1f), 0.05f);

            // Pitch
            var ground = Make.Box("Pitch", new Vector3(SimConfig.FieldWidth, 1f, SimConfig.FieldLength),
                                   new Vector3(0f, -0.5f, 0f), grass, root);
            ground.GetComponent<Collider>().material = Make.PhysMat("Turf", 0.15f, 0.7f, 0.7f);

            // Penalty box markings (thin flat boxes on the turf, no colliders)
            float goalZ = SimConfig.GoalCenter.z;
            float boxNearZ = goalZ - SimConfig.PenaltyBoxDepth;
            Line(root, line, new Vector3(0f, 0.01f, boxNearZ),
                 new Vector3(SimConfig.PenaltyBoxWidth, 0.02f, 0.2f)); // 18-yard line
            Line(root, line, new Vector3(-SimConfig.PenaltyBoxWidth * 0.5f, 0.01f, (goalZ + boxNearZ) * 0.5f),
                 new Vector3(0.2f, 0.02f, SimConfig.PenaltyBoxDepth));
            Line(root, line, new Vector3(SimConfig.PenaltyBoxWidth * 0.5f, 0.01f, (goalZ + boxNearZ) * 0.5f),
                 new Vector3(0.2f, 0.02f, SimConfig.PenaltyBoxDepth));

            // Goal
            var goalRoot = Make.Empty("Goal", SimConfig.GoalCenter, root).transform;
            refs.goalCenter = goalRoot;
            float gw = SimConfig.GoalWidth, gh = SimConfig.GoalHeight, gd = SimConfig.GoalDepth;
            float postR = 0.06f;
            // uprights
            Make.Box("PostL", new Vector3(postR * 2f, gh, postR * 2f),
                      SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), post, goalRoot);
            Make.Box("PostR", new Vector3(postR * 2f, gh, postR * 2f),
                      SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh * 0.5f, 0f), post, goalRoot);
            // crossbar
            Make.Box("Bar", new Vector3(gw + postR * 2f, postR * 2f, postR * 2f),
                      SimConfig.GoalCenter + new Vector3(0f, gh, 0f), post, goalRoot);
            // net (visual, thin walls behind the line, with colliders so the ball stops)
            var net = Make.Mat(new Color(0.85f, 0.85f, 0.9f, 1f), 0.0f);
            Make.Box("NetBack", new Vector3(gw, gh, 0.08f),
                      SimConfig.GoalCenter + new Vector3(0f, gh * 0.5f, gd), net, goalRoot);
            Make.Box("NetTop", new Vector3(gw, 0.08f, gd),
                      SimConfig.GoalCenter + new Vector3(0f, gh, gd * 0.5f), net, goalRoot);
            Make.Box("NetSideL", new Vector3(0.08f, gh, gd),
                      SimConfig.GoalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, gd * 0.5f), net, goalRoot);
            Make.Box("NetSideR", new Vector3(0.08f, gh, gd),
                      SimConfig.GoalCenter + new Vector3(gw * 0.5f, gh * 0.5f, gd * 0.5f), net, goalRoot);

            // Goal trigger: a thin slab spanning the mouth just inside the line.
            var trigger = Make.Box("GoalTrigger", new Vector3(gw, gh, 0.3f),
                                    SimConfig.GoalCenter + new Vector3(0f, gh * 0.5f, 0.2f), null, goalRoot, collider: true);
            var tcol = trigger.GetComponent<Collider>();
            tcol.isTrigger = true;
            var mr = trigger.GetComponent<Renderer>();
            if (mr != null) Object.Destroy(mr); // invisible
            refs.goal = trigger.AddComponent<Goal>();

            // Boundary walls (keep the ball roughly in the training slice)
            float wallH = 6f, t = 0.4f;
            float halfW = SimConfig.FieldWidth * 0.5f, halfL = SimConfig.FieldLength * 0.5f;
            MakeWall(root, wall, new Vector3(0f, wallH * 0.5f, -halfL - t * 0.5f), new Vector3(SimConfig.FieldWidth + t * 2f, wallH, t));
            MakeWall(root, wall, new Vector3(0f, wallH * 0.5f, halfL + t * 0.5f), new Vector3(SimConfig.FieldWidth + t * 2f, wallH, t));
            MakeWall(root, wall, new Vector3(-halfW - t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, SimConfig.FieldLength));
            MakeWall(root, wall, new Vector3(halfW + t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, SimConfig.FieldLength));

            return refs;
        }

        static void Line(Transform root, Material m, Vector3 pos, Vector3 size)
        {
            var go = Make.Box("Line", size, pos, m, root, collider: false);
        }

        static void MakeWall(Transform root, Material m, Vector3 pos, Vector3 size)
        {
            var go = Make.Box("Wall", size, pos, m, root, collider: true);
            go.GetComponent<Collider>().material = Make.PhysMat("Wall", 0.35f, 0.4f, 0.4f);
        }
    }
}
