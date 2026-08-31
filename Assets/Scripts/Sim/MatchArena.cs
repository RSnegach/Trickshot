using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A self-contained match pitch: a rectangular field centred on the origin with a
    /// goal at EACH end (+Z and -Z), a see-through net in each, and an invisible SEALED box
    /// around it - walls on all four sides, lintels above both crossbars, and a lid - so the
    /// ball genuinely cannot leave play. Sized to the team count.
    ///
    /// Independent of the single-goal training Arena so none of the existing modes change.
    /// Scoring is done geometrically by MatchGame (GoalAt), not by trigger callbacks.
    /// </summary>
    public static class MatchArena
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
            var grassMat = Turf.Ground(new Color(0.20f, 0.42f, 0.20f), hw * 2f + 8f, hl * 2f + 8f);
            // 4 m THICK, not 0.4. The top face stays at y = 0; all of the extra goes downward.
            //
            // Depth is what makes a body that ends up under the surface RECOVERABLE. Measured: a body
            // pushed 0.02 m down still overlaps the slab and Unity's depenetration ejects it within a
            // frame, but once it is past the bottom face nothing touches it again and it is gone for
            // good. At 0.4 m that margin was tiny, and ActiveRagdoll has eight direct rb.position
            // writes (SnapBone, SnapLayout, the display-puppet paths, the free-kick restore) which
            // bypass continuous collision entirely - so a write of half a metre put a bone straight
            // through the floor with no sweep to stop it.
            //
            // Costs nothing: it is one box, static-batched with the rest of the arena, and the extra
            // volume is never rendered because only the top face is ever visible.
            var ground = Make.Box("ScrimGround", new Vector3(hw * 2f + 8f, 4f, hl * 2f + 8f),
                                  new Vector3(0f, -2f, 0f), grassMat, root, collider: true);
            ground.GetComponent<Collider>().material = Make.PhysMat("Turf", 0.1f, 0.6f, 0.6f);

            // Painted markings. Thin bright boxes, no colliders. NOT static-batched: these hang
            // off the shared match root next to the ball and the players, which move.
            var line = Make.Mat(new Color(0.9f, 0.9f, 0.9f), 0.3f);

            // Marking sizes are regulation metres scaled to THIS pitch. perSide 11 is a real
            // 105x68 field (hl 52.5, hw 34), so it scales by 1 and the smaller pitches shrink
            // proportionally. ONE uniform factor, so the D and the centre circle stay round.
            // Denominators track SimConfig.ScrimHalfLength/Width at perSide 11; if those change,
            // change these with them or 11-a-side stops drawing regulation markings.
            float mk = Mathf.Min(hw / 34f, hl / 52.5f);

            // Touchlines, goal lines, halfway line.
            Line(root, line, new Vector3(-hw, LineY, 0f), new Vector3(LineW, LineThk, hl * 2f + LineW));
            Line(root, line, new Vector3( hw, LineY, 0f), new Vector3(LineW, LineThk, hl * 2f + LineW));
            Line(root, line, new Vector3(0f, LineY, -hl), new Vector3(hw * 2f + LineW, LineThk, LineW));
            Line(root, line, new Vector3(0f, LineY,  hl), new Vector3(hw * 2f + LineW, LineThk, LineW));
            Line(root, line, new Vector3(0f, LineY, 0f),  new Vector3(hw * 2f, LineThk, LineW));

            // Centre circle + centre spot.
            PitchBuilder.Circle(root, line, new Vector3(0f, LineY, 0f), CenterCircleR * mk, CenterCircleSegs);
            Line(root, line, new Vector3(0f, LineY, 0f), new Vector3(SpotSize, LineThk, SpotSize));

            // 18-yard box, 6-yard box, penalty spot and D at BOTH ends. dir is the sign of +Z
            // that points into the pitch from that goal line.
            EndMarkings(root, line,  hl, -1f, mk);
            EndMarkings(root, line, -hl, +1f, mk);

            // A goal at each end.
            BuildGoal(root, refs.homeGoalCenter, faceNegZ: true);   // mouth opens toward -Z (play)
            BuildGoal(root, refs.awayGoalCenter, faceNegZ: false);  // mouth opens toward +Z (play)

            // Boundary walls. The two touchlines (along Z) are solid. The two GOAL-END walls
            // must NOT block the goal mouth, or shots can never score: build each end wall as
            // two segments with a gap the width of the goal mouth in the middle (the net's own
            // backstops stop a ball that actually goes in). Walls sit just outside the lines.
            var wallPhys = Make.PhysMat("Wall", 0.3f, 0.4f, 0.4f);
            float wallH = SimConfig.ScrimWallHeight, t = 0.4f;
            // Touchlines (+X / -X): full length.
            MakeWall(root, wallPhys, new Vector3(hw + t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, hl * 2f + t * 2f));
            MakeWall(root, wallPhys, new Vector3(-hw - t * 0.5f, wallH * 0.5f, 0f), new Vector3(t, wallH, hl * 2f + t * 2f));
            // Goal-end walls (+Z / -Z): split around a gap the width of the goal mouth, with a
            // LINTEL closing that gap ABOVE the crossbar. The gap used to run all the way up to
            // the sky, so any ball over the bar - a skied shot, a clearance, a keeper's punt -
            // flew straight out through it and left the pitch entirely.
            float gap = SimConfig.GoalWidth + SimConfig.ScrimGoalGapPad * 2f;
            float segLen = (hw * 2f + t * 2f - gap) * 0.5f;
            float lintelY = SimConfig.GoalHeight + 0.25f;            // clear of the crossbar
            float lintelH = Mathf.Max(0.2f, wallH - lintelY);
            foreach (float zEnd in new[] { hl + t * 0.5f, -hl - t * 0.5f })
            {
                if (segLen > 0.1f)
                {
                    float segCenter = gap * 0.5f + segLen * 0.5f;
                    foreach (float xSign in new[] { 1f, -1f })
                        MakeWall(root, wallPhys, new Vector3(xSign * segCenter, wallH * 0.5f, zEnd), new Vector3(segLen, wallH, t));
                }
                MakeWall(root, wallPhys, new Vector3(0f, lintelY + lintelH * 0.5f, zEnd), new Vector3(gap, lintelH, t));
            }
            // LID. Walls alone only bound the ball sideways; anything lofted cleared them and was
            // gone. A dead ceiling closes the box. Low bounce so a ball that reaches it drops back
            // into play instead of pinging around up there.
            MakeWall(root, Make.PhysMat("Lid", 0.2f, 0.12f, 0.12f),
                     new Vector3(0f, wallH + t * 0.5f, 0f),
                     new Vector3(hw * 2f + t * 2f, t, hl * 2f + t * 2f));

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

            Make.Cylinder("PostL", postR, gh, center + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork).AddComponent<GoalFrame>();
            Make.Cylinder("PostR", postR, gh, center + new Vector3(gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, goalRoot, woodwork).AddComponent<GoalFrame>();
            Make.Cylinder("Bar", postR, gw + postR * 2f, center + new Vector3(0f, gh, 0f), 0, frameMat, goalRoot, woodwork).AddComponent<GoalFrame>();

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

        // ---- Marking geometry. Regulation metres; Build scales them by mk. ----
        const float LineW    = 0.12f;   // painted line thickness across its run
        const float LineThk  = 0.02f;   // vertical height of a marking box
        const float LineY    = 0.02f;   // marking centre height (turf top is y = 0)
        const float SpotSize = 0.3f;    // painted dot square edge
        const float CenterCircleR = 9.15f;
        const int   CenterCircleSegs = 40;
        const float PenBoxDepth = 16.5f;  // 18-yard box depth off the goal line
        const float PenBoxSide  = 16.5f;  // box edge, out from each post (7.32 goal -> 40.32 wide)
        const float SixDepth    = 5.5f;   // 6-yard goal area depth
        const float SixSide     = 5.5f;   // goal area edge, out from each post (-> 18.32 wide)
        const float PenSpotDist = 11f;    // penalty spot off the goal line
        const float PenArcR     = 9.15f;  // D radius, centred on the spot
        const int   PenArcSegs  = 16;

        /// <summary>18-yard box, 6-yard goal area, penalty spot and D for one goal line.
        /// dir is the sign of +Z that points INTO the pitch (-1 at the +Z goal, +1 at the -Z
        /// goal); mk scales regulation metres to this pitch. Both box widths grow OUT from the
        /// live goal width, which an earlier set-piece match can leave scaled, so the goal area
        /// can never end up narrower than the posts.</summary>
        static void EndMarkings(Transform root, Material m, float goalLineZ, float dir, float mk)
        {
            float halfGoal = SimConfig.GoalWidth * 0.5f;

            // 18-yard box.
            float boxHalfW  = halfGoal + PenBoxSide * mk;
            float boxDepth  = PenBoxDepth * mk;
            float boxFrontZ = goalLineZ + dir * boxDepth;
            float boxMidZ   = (goalLineZ + boxFrontZ) * 0.5f;
            Line(root, m, new Vector3(0f, LineY, boxFrontZ), new Vector3(boxHalfW * 2f + LineW, LineThk, LineW));
            Line(root, m, new Vector3(-boxHalfW, LineY, boxMidZ), new Vector3(LineW, LineThk, boxDepth));
            Line(root, m, new Vector3( boxHalfW, LineY, boxMidZ), new Vector3(LineW, LineThk, boxDepth));

            // 6-yard goal area.
            float sixHalfW  = halfGoal + SixSide * mk;
            float sixDepth  = SixDepth * mk;
            float sixFrontZ = goalLineZ + dir * sixDepth;
            float sixMidZ   = (goalLineZ + sixFrontZ) * 0.5f;
            Line(root, m, new Vector3(0f, LineY, sixFrontZ), new Vector3(sixHalfW * 2f + LineW, LineThk, LineW));
            Line(root, m, new Vector3(-sixHalfW, LineY, sixMidZ), new Vector3(LineW, LineThk, sixDepth));
            Line(root, m, new Vector3( sixHalfW, LineY, sixMidZ), new Vector3(LineW, LineThk, sixDepth));

            // Penalty spot.
            float spotZ = goalLineZ + dir * PenSpotDist * mk;
            Line(root, m, new Vector3(0f, LineY, spotZ), new Vector3(SpotSize, LineThk, SpotSize));

            // D: the slice of the arc around the spot that lies OUTSIDE the box front line.
            // Depth, spot distance and radius all carry the same mk, so the half-angle is scale
            // independent and the arc ends land exactly on the box front line.
            float half = Mathf.Acos(Mathf.Clamp((PenBoxDepth - PenSpotDist) / PenArcR, -1f, 1f)) * Mathf.Rad2Deg;
            float bulgeDeg = dir > 0f ? 90f : 270f;
            PitchBuilder.Arc(root, m, new Vector3(0f, LineY, spotZ), PenArcR * mk,
                             bulgeDeg - half, 2f * half, PenArcSegs);
        }

        static void Line(Transform root, Material m, Vector3 pos, Vector3 size)
        {
            Make.Box("Line", size, pos, m, root, collider: false);
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
