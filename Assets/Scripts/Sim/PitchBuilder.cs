using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Builds a full regulation soccer pitch under one root: a large flat turf ground
    /// plane (sized to cover the pitch, runoff, and the stand footprint so the stadium
    /// sits on solid ground), the complete set of white line markings, and a visual
    /// mirror goal at the far end so the pitch reads as a real two-goal field.
    ///
    /// All geometry is anchored to PitchLayout so it lines up with the existing gameplay.
    /// The attacking goal line is PitchLayout.AttackGoalLineZ (the existing goal at
    /// SimConfig.GoalCenter.z); the pitch runs back from there to PitchLayout.FarGoalLineZ.
    /// This builder does NOT touch the existing attacking goal, ball, or walls. Wire it
    /// in alongside Arena; nothing here overwrites Arena's objects.
    /// </summary>
    public static class PitchBuilder
    {
        // ---- Marking geometry (metres) ----
        const float LineWidth = 0.12f;   // painted line thickness across its run
        const float LineThk   = 0.02f;   // vertical box height of a marking
        const float LineY     = 0.02f;   // marking centre height (sits just above turf top at y=0)

        const float CenterCircleRadius = 9.15f;
        const int   CenterCircleSegments = 48;
        const float SpotSize = 0.3f;     // painted dot square edge (centre + penalty spots)

        // Regulation 18-yard box and 6-yard box (full pitch, wider than the training box).
        const float PenaltyBoxWidth = 40.3f;
        const float PenaltyBoxDepth = 16.5f;
        const float SixYardWidth    = 18.3f;
        const float SixYardDepth    = 5.5f;
        const float PenaltySpotDist = 11f;    // spot distance from the goal line
        const float PenaltyArcRadius = 9.15f; // arc centred on the penalty spot
        const int   PenaltyArcSegments = 16;

        const float CornerRadius = 1f;
        const int   CornerSegments = 8;

        // Ground plane must reach past the stands. Depth up a stand rake plus a margin.
        const float GroundMargin = 20f;

        // Visual net strings for the far goal (no colliders, purely cosmetic).
        const float NetStringW = 0.03f;
        const float NetSpacing = 0.5f;

        public static void Build(Transform root)
        {
            var pitchRoot = Make.Empty("FullPitch", Vector3.zero, root).transform;

            var grass = Make.Mat(new Color(0.22f, 0.45f, 0.24f), 0.05f);
            var line  = Make.Unlit(new Color(0.95f, 0.95f, 0.95f, 1f));

            float attackLineZ = PitchLayout.AttackGoalLineZ;
            float farLineZ     = PitchLayout.FarGoalLineZ;
            float centerZ      = PitchLayout.PitchCenterZ;
            float halfW        = PitchLayout.HalfWidth;

            // Ground plane: cover the pitch plus runoff plus the full stand footprint so
            // both the field and the stadium built by another agent rest on solid turf.
            // Top surface at y = 0 (box is 1 tall, centred at y = -0.5), matching markings.
            float standDepth  = PitchLayout.StandFrontGap + PitchLayout.StandRows * PitchLayout.RowDepth;
            float groundHalfX = halfW + standDepth + GroundMargin;
            float groundHalfZ = PitchLayout.PitchLength * 0.5f + standDepth + GroundMargin;
            var ground = Make.Box("Ground",
                new Vector3(groundHalfX * 2f, 1f, groundHalfZ * 2f),
                new Vector3(0f, -0.5f, centerZ), grass, pitchRoot);
            ground.GetComponent<Collider>().material = Make.PhysMat("Turf", 0.15f, 0.25f, 0.25f);

            // Touchlines (x = +/- halfW) run the full pitch length; goal lines run full
            // width at each end; halfway line splits the pitch at the centre.
            float lenZ = PitchLayout.PitchLength;
            StraightLine(pitchRoot, line, new Vector3(-halfW, LineY, centerZ), new Vector3(LineWidth, LineThk, lenZ + LineWidth));
            StraightLine(pitchRoot, line, new Vector3( halfW, LineY, centerZ), new Vector3(LineWidth, LineThk, lenZ + LineWidth));
            StraightLine(pitchRoot, line, new Vector3(0f, LineY, attackLineZ), new Vector3(halfW * 2f + LineWidth, LineThk, LineWidth));
            StraightLine(pitchRoot, line, new Vector3(0f, LineY, farLineZ),    new Vector3(halfW * 2f + LineWidth, LineThk, LineWidth));
            StraightLine(pitchRoot, line, new Vector3(0f, LineY, centerZ),     new Vector3(halfW * 2f + LineWidth, LineThk, LineWidth));

            // Centre circle + spot.
            Circle(pitchRoot, line, new Vector3(0f, LineY, centerZ), CenterCircleRadius, CenterCircleSegments);
            Spot(pitchRoot, line, new Vector3(0f, LineY, centerZ));

            // Both ends: penalty box, six-yard box, penalty spot, penalty arc.
            // Attacking end box extends toward -Z (into the pitch); far end toward +Z.
            EndMarkings(pitchRoot, line, attackLineZ, -1f);
            EndMarkings(pitchRoot, line, farLineZ,    +1f);

            // Four corner arcs, each a quarter circle bulging into the field.
            CornerArcs(pitchRoot, line, halfW, attackLineZ, farLineZ);

            // Far goal: cylindrical frame + visual net, mirror of the attacking goal.
            BuildFarGoal(pitchRoot, farLineZ);
        }

        // ---- Straight markings ----

        static void StraightLine(Transform root, Material m, Vector3 center, Vector3 size)
        {
            Make.Box("Line", size, center, m, root, collider: false);
        }

        static void Spot(Transform root, Material m, Vector3 center)
        {
            Make.Box("Spot", new Vector3(SpotSize, LineThk, SpotSize),
                     new Vector3(center.x, LineY, center.z), m, root, collider: false);
        }

        /// <summary>Penalty box, six-yard box, penalty spot, and penalty arc for one end.
        /// dir is the sign of +Z that points INTO the pitch from this goal line
        /// (-1 for the attacking end at +Z, +1 for the far end at -Z).</summary>
        static void EndMarkings(Transform root, Material m, float goalLineZ, float dir)
        {
            // Penalty (18-yard) box.
            float halfBoxW  = PenaltyBoxWidth * 0.5f;
            float boxFrontZ = goalLineZ + dir * PenaltyBoxDepth;
            float boxMidZ   = (goalLineZ + boxFrontZ) * 0.5f;
            StraightLine(root, m, new Vector3(0f, LineY, boxFrontZ), new Vector3(halfBoxW * 2f + LineWidth, LineThk, LineWidth));
            StraightLine(root, m, new Vector3(-halfBoxW, LineY, boxMidZ), new Vector3(LineWidth, LineThk, PenaltyBoxDepth));
            StraightLine(root, m, new Vector3( halfBoxW, LineY, boxMidZ), new Vector3(LineWidth, LineThk, PenaltyBoxDepth));

            // Six-yard box.
            float halfSixW  = SixYardWidth * 0.5f;
            float sixFrontZ = goalLineZ + dir * SixYardDepth;
            float sixMidZ   = (goalLineZ + sixFrontZ) * 0.5f;
            StraightLine(root, m, new Vector3(0f, LineY, sixFrontZ), new Vector3(halfSixW * 2f + LineWidth, LineThk, LineWidth));
            StraightLine(root, m, new Vector3(-halfSixW, LineY, sixMidZ), new Vector3(LineWidth, LineThk, SixYardDepth));
            StraightLine(root, m, new Vector3( halfSixW, LineY, sixMidZ), new Vector3(LineWidth, LineThk, SixYardDepth));

            // Penalty spot.
            float spotZ = goalLineZ + dir * PenaltySpotDist;
            Spot(root, m, new Vector3(0f, LineY, spotZ));

            // Penalty arc: the part of the 9.15 m circle around the spot that lies beyond
            // the box front line. Bulge points into the field (+Z = 90 deg, -Z = 270 deg).
            float half = Mathf.Acos((PenaltyBoxDepth - PenaltySpotDist) / PenaltyArcRadius) * Mathf.Rad2Deg;
            float bulgeDeg = dir > 0f ? 90f : 270f;
            Arc(root, m, new Vector3(0f, LineY, spotZ), PenaltyArcRadius, bulgeDeg - half, 2f * half, PenaltyArcSegments);
        }

        static void CornerArcs(Transform root, Material m, float halfW, float attackLineZ, float farLineZ)
        {
            // Angle convention: 0 = +X, 90 = +Z, 180 = -X, 270 = -Z. Each quarter arc runs
            // from the goal line to the touchline, bulging toward the pitch centre.
            // Attacking end (z = attackLineZ): field lies toward -Z.
            Arc(root, m, new Vector3( halfW, LineY, attackLineZ), CornerRadius, 180f, 90f, CornerSegments);
            Arc(root, m, new Vector3(-halfW, LineY, attackLineZ), CornerRadius, 270f, 90f, CornerSegments);
            // Far end (z = farLineZ): field lies toward +Z.
            Arc(root, m, new Vector3( halfW, LineY, farLineZ), CornerRadius, 90f, 90f, CornerSegments);
            Arc(root, m, new Vector3(-halfW, LineY, farLineZ), CornerRadius,  0f, 90f, CornerSegments);
        }

        // ---- Arc / circle helpers ----

        /// <summary>Lay short flat boxes tangent along an arc. Angle in degrees with
        /// 0 = +X and increasing toward +Z; the arc runs startDeg .. startDeg + sweepDeg.</summary>
        public static void Arc(Transform root, Material lineMat, Vector3 center, float radius,
                               float startDeg, float sweepDeg, int segments)
        {
            Vector3 prev = PointOnCircle(center, radius, startDeg);
            for (int i = 1; i <= segments; i++)
            {
                float a = startDeg + sweepDeg * ((float)i / segments);
                Vector3 p = PointOnCircle(center, radius, a);
                ArcSegment(root, lineMat, prev, p);
                prev = p;
            }
        }

        /// <summary>Full circle as tangent segment boxes.</summary>
        public static void Circle(Transform root, Material lineMat, Vector3 center, float radius, int segments)
        {
            Arc(root, lineMat, center, radius, 0f, 360f, segments);
        }

        static Vector3 PointOnCircle(Vector3 center, float radius, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(center.x + radius * Mathf.Cos(r), LineY, center.z + radius * Mathf.Sin(r));
        }

        static void ArcSegment(Transform root, Material m, Vector3 p0, Vector3 p1)
        {
            Vector3 d = p1 - p0;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector3 mid = (p0 + p1) * 0.5f;
            mid.y = LineY;
            // Length runs along the box local +Z; LookRotation aims that at the next point.
            // Overlap by LineWidth so segment joints have no gaps.
            var go = Make.Box("ArcSeg", new Vector3(LineWidth, LineThk, len + LineWidth), mid, m, root, collider: false);
            go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        // ---- Far goal (visual mirror of the attacking goal in Arena) ----

        static void BuildFarGoal(Transform root, float farLineZ)
        {
            var goalRoot = Make.Empty("FarGoal", new Vector3(0f, 0f, farLineZ), root).transform;
            float gw = SimConfig.GoalWidth, gh = SimConfig.GoalHeight, gd = SimConfig.GoalDepth;
            float postR = 0.07f;
            var frameMat = Make.Mat(Color.white, 0.3f);
            var woodwork = Make.PhysMat("Post", 0.75f, 0.3f, 0.3f);
            Vector3 c = new Vector3(0f, 0f, farLineZ);

            // Same layout as Arena's goal, but depth extends toward -Z (away from the
            // pitch) so the mouth faces the pitch centre. Uprights axis 1 (Y), crossbar
            // axis 0 (X), depth rails axis 2 (Z). Posts keep their capsule colliders.
            Make.Cylinder("PostL", postR, gh, c + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("PostR", postR, gh, c + new Vector3( gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("Bar", postR, gw + postR * 2f, c + new Vector3(0f, gh, 0f), 0, frameMat, goalRoot, woodwork);
            Make.Cylinder("BackPostL", postR, gh, c + new Vector3(-gw * 0.5f, gh * 0.5f, -gd), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("BackPostR", postR, gh, c + new Vector3( gw * 0.5f, gh * 0.5f, -gd), 1, frameMat, goalRoot, woodwork);
            Make.Cylinder("RailL", postR * 0.7f, gd, c + new Vector3(-gw * 0.5f, gh, -gd * 0.5f), 2, frameMat, goalRoot, woodwork);
            Make.Cylinder("RailR", postR * 0.7f, gd, c + new Vector3( gw * 0.5f, gh, -gd * 0.5f), 2, frameMat, goalRoot, woodwork);

            // Visual net: static string grid wrapping back + sides + top. No colliders.
            var netMat = Make.Unlit(new Color(0.92f, 0.92f, 0.98f, 1f));
            float backZ = farLineZ - gd;
            NetPlaneXY(goalRoot, netMat, backZ, -gw * 0.5f, gw * 0.5f, 0f, gh);      // back wall
            NetPlaneZY(goalRoot, netMat, -gw * 0.5f, backZ, farLineZ, 0f, gh);        // left side
            NetPlaneZY(goalRoot, netMat,  gw * 0.5f, backZ, farLineZ, 0f, gh);        // right side
            NetPlaneXZ(goalRoot, netMat, gh, -gw * 0.5f, gw * 0.5f, backZ, farLineZ); // roof
        }

        // Net string grids. Each plane is axis-aligned so thin boxes need no rotation.

        static void NetPlaneXY(Transform parent, Material m, float z, float x0, float x1, float y0, float y1)
        {
            float w = x1 - x0, h = y1 - y0;
            float midX = (x0 + x1) * 0.5f, midY = (y0 + y1) * 0.5f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(w / NetSpacing));
            int ny = Mathf.Max(1, Mathf.RoundToInt(h / NetSpacing));
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(x0, x1, (float)i / nx);
                Make.Box("Net", new Vector3(NetStringW, h, NetStringW), new Vector3(x, midY, z), m, parent, collider: false);
            }
            for (int j = 0; j <= ny; j++)
            {
                float y = Mathf.Lerp(y0, y1, (float)j / ny);
                Make.Box("Net", new Vector3(w, NetStringW, NetStringW), new Vector3(midX, y, z), m, parent, collider: false);
            }
        }

        static void NetPlaneZY(Transform parent, Material m, float x, float z0, float z1, float y0, float y1)
        {
            float d = Mathf.Abs(z1 - z0), h = y1 - y0;
            float midZ = (z0 + z1) * 0.5f, midY = (y0 + y1) * 0.5f;
            int nz = Mathf.Max(1, Mathf.RoundToInt(d / NetSpacing));
            int ny = Mathf.Max(1, Mathf.RoundToInt(h / NetSpacing));
            for (int i = 0; i <= nz; i++)
            {
                float z = Mathf.Lerp(z0, z1, (float)i / nz);
                Make.Box("Net", new Vector3(NetStringW, h, NetStringW), new Vector3(x, midY, z), m, parent, collider: false);
            }
            for (int j = 0; j <= ny; j++)
            {
                float y = Mathf.Lerp(y0, y1, (float)j / ny);
                Make.Box("Net", new Vector3(NetStringW, NetStringW, d), new Vector3(x, y, midZ), m, parent, collider: false);
            }
        }

        static void NetPlaneXZ(Transform parent, Material m, float y, float x0, float x1, float z0, float z1)
        {
            float w = x1 - x0, d = Mathf.Abs(z1 - z0);
            float midX = (x0 + x1) * 0.5f, midZ = (z0 + z1) * 0.5f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(w / NetSpacing));
            int nz = Mathf.Max(1, Mathf.RoundToInt(d / NetSpacing));
            for (int i = 0; i <= nz; i++)
            {
                float z = Mathf.Lerp(z0, z1, (float)i / nz);
                Make.Box("Net", new Vector3(w, NetStringW, NetStringW), new Vector3(midX, y, z), m, parent, collider: false);
            }
            for (int j = 0; j <= nx; j++)
            {
                float x = Mathf.Lerp(x0, x1, (float)j / nx);
                Make.Box("Net", new Vector3(NetStringW, NetStringW, d), new Vector3(x, y, midZ), m, parent, collider: false);
            }
        }
    }
}
