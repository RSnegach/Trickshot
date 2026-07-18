using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Builds the stadium SHELL that wraps the full pitch: raked terraces, a low
    /// advertising perimeter wall, tall back walls, cantilevered roofs, corner infill
    /// towers, floodlight pylons, and one player tunnel. This is the built environment
    /// only. The crowd is placed separately by Crowd.cs, one fan per PitchLayout seat.
    ///
    /// Every terrace tread is aligned to the PitchLayout seat footprint so fans stand
    /// exactly on the steps: for row r the tread top sits at StandBaseHeight + r*RowRise
    /// and the row is offset r*RowDepth outward from the StandFront centre line, which is
    /// exactly where PitchLayout.Seats puts the fan for that row.
    ///
    /// All geometry is greybox primitives via Make. Colliders are DESTROYED on everything
    /// visual (steps, roof, corners, pylons, tunnel) so nothing snags the ball. Only the
    /// perimeter wall and the back walls keep colliders so a wild ball stays in the bowl.
    /// No point/spot Lights are added; floodlamps are emissive materials only (cheap).
    /// </summary>
    public static class StadiumBuilder
    {
        // ---- Palette ----
        static readonly Color ConcreteColor = new Color(0.60f, 0.60f, 0.63f);   // stand terraces
        static readonly Color RoofColor     = new Color(0.13f, 0.13f, 0.16f);   // roof + dark undersides
        static readonly Color AccentColor   = new Color(0.15f, 0.45f, 0.90f);   // team colour: wall + tread nosing
        static readonly Color TunnelColor   = new Color(0.05f, 0.05f, 0.06f);   // recessed tunnel mouth
        static readonly Color PylonColor    = new Color(0.28f, 0.28f, 0.31f);   // floodlight steel
        static readonly Color LampColor     = new Color(1.00f, 0.97f, 0.85f);   // emissive floodlamp

        // ---- Terrace step geometry ----
        const float StepOverlap = 0.15f;   // vertical overlap so stacked steps read as one solid bank
        const float StepExtend  = 0.02f;   // outward overlap so adjacent rows have no seam
        const float NosingDepth = 0.20f;   // team-colour strip along the front edge of each tread
        const float NosingHeight = 0.12f;

        // ---- Walls ----
        const float PerimeterWallHeight = 1.2f;   // advertising-board height
        const float PerimeterThickness  = 0.4f;
        const float PerimeterStandoff   = 0.25f;  // gap from row 0 front toward the pitch
        const float BackWallThickness   = 0.6f;
        const float BackWallExtra       = 3.0f;   // how far the back wall rises above the top tread

        // ---- Roof ----
        const float RoofDepth     = 12f;   // how far the canopy reaches out over the rows
        const float RoofThickness = 0.5f;
        const float RoofClearance = 4f;    // height of the underside above the top tread
        const int   RoofSupports  = 3;     // vertical props per stand (visual only)
        const float SupportSize   = 0.5f;

        // ---- Corner towers ----
        const int   CornerSteps      = 5;
        const float CornerInsetOut   = 7f;   // how far the tower sits outward from the bowl corner point
        const float CornerBaseFoot   = 20f;  // bottom footprint edge
        const float CornerTopFoot    = 8f;   // top footprint edge

        // ---- Floodlight pylons ----
        const float PylonHeight   = 32f;
        const float PylonRadius   = 0.5f;
        const float PylonInsetOut = 12f;   // outward from the bowl corner point
        const int   LampCols      = 3;     // emissive lamp grid on the pitch-facing head
        const int   LampRows      = 2;
        const float LampCell      = 1.5f;

        // ---- Tunnel (player entrance) ----
        const float TunnelWidth  = 5f;
        const float TunnelHeight = 3f;
        const float TunnelDepth  = 4f;
        const PitchLayout.Side TunnelSide = PitchLayout.Side.PlusX;

        public static void Build(Transform root)
        {
            var stadium = Make.Empty("Stadium", Vector3.zero, root).transform;

            var concrete = Make.Mat(ConcreteColor, 0.05f);
            var roofMat  = Make.Mat(RoofColor, 0.1f);
            var accent   = Make.Mat(AccentColor, 0.15f);
            var pylonMat = Make.Mat(PylonColor, 0.2f, 0.4f);
            var lampMat  = Make.Glow(LampColor);
            var tunnelMat = Make.Mat(TunnelColor, 0.0f);
            var wallPhys = Make.PhysMat("StadiumWall", 0.3f, 0.4f, 0.4f);

            foreach (var side in PitchLayout.AllSides)
            {
                var sideRoot = Make.Empty(side + "Stand", Vector3.zero, stadium).transform;
                BuildTerrace(sideRoot, side, concrete, accent);
                BuildPerimeterWall(sideRoot, side, accent, wallPhys);
                BuildBackWall(sideRoot, side, concrete, wallPhys);
                BuildRoof(sideRoot, side, roofMat);
                if (side == TunnelSide) BuildTunnel(sideRoot, side, tunnelMat);
            }

            BuildCorners(stadium, concrete);
            BuildPylons(stadium, pylonMat, roofMat, lampMat);
        }

        // ---------------------------------------------------------------- Terrace

        /// <summary>Raked bank of steps. One step box per row (top face at the seat height),
        /// plus a thin team-colour nosing along each tread's pitch-side edge.</summary>
        static void BuildTerrace(Transform p, PitchLayout.Side side, Material concrete, Material accent)
        {
            PitchLayout.StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            float standLen = halfLength * 2f;

            for (int r = 0; r < PitchLayout.StandRows; r++)
            {
                float treadTop  = PitchLayout.StandBaseHeight + r * PitchLayout.RowRise;
                float outOffset = r * PitchLayout.RowDepth;

                // Row 0 fills to the ground; higher rows overlap the row below them so the
                // whole bank reads as one solid raked terrace with no gaps between steps.
                float bottom = (r == 0) ? 0f : treadTop - PitchLayout.RowRise - StepOverlap;
                float h  = treadTop - bottom;
                float cy = (treadTop + bottom) * 0.5f;

                Vector3 size = SizeVec(alongDir, standLen, outDir, PitchLayout.RowDepth + StepExtend, h);
                Vector3 pos  = P(center, alongDir, 0f, outDir, outOffset, cy);
                Make.Box(side + "_Step" + r, size, pos, concrete, p, collider: false);

                // Nosing: thin accent strip flush with the tread top, on the pitch-side edge.
                float noseOut = outOffset - PitchLayout.RowDepth * 0.5f + NosingDepth * 0.5f;
                Vector3 nSize = SizeVec(alongDir, standLen * 0.998f, outDir, NosingDepth, NosingHeight);
                Vector3 nPos  = P(center, alongDir, 0f, outDir, noseOut, treadTop - NosingHeight * 0.5f);
                Make.Box(side + "_Nose" + r, nSize, nPos, accent, p, collider: false);
            }
        }

        // ---------------------------------------------------------------- Walls

        /// <summary>Low advertising-board wall in front of row 0. Keeps its collider so a
        /// wild ball is nudged back toward the pitch. Split around the tunnel mouth.</summary>
        static void BuildPerimeterWall(Transform p, PitchLayout.Side side, Material accent, PhysicsMaterial phys)
        {
            PitchLayout.StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            float standLen = halfLength * 2f;
            float outOffset = -PitchLayout.RowDepth * 0.5f - PerimeterStandoff;
            float cy = PerimeterWallHeight * 0.5f;

            if (side != TunnelSide)
            {
                Vector3 size = SizeVec(alongDir, standLen + 1f, outDir, PerimeterThickness, PerimeterWallHeight);
                Vector3 pos  = P(center, alongDir, 0f, outDir, outOffset, cy);
                SolidWall(side + "_PerimWall", size, pos, accent, p, phys);
            }
            else
            {
                // Two segments leaving a gap for the tunnel at the centre of this side.
                float segLen = (standLen - TunnelWidth) * 0.5f;
                float segAlong = TunnelWidth * 0.5f + segLen * 0.5f;
                Vector3 segSize = SizeVec(alongDir, segLen, outDir, PerimeterThickness, PerimeterWallHeight);
                SolidWall(side + "_PerimWallL", segSize, P(center, alongDir, -segAlong, outDir, outOffset, cy), accent, p, phys);
                SolidWall(side + "_PerimWallR", segSize, P(center, alongDir,  segAlong, outDir, outOffset, cy), accent, p, phys);
            }
        }

        /// <summary>Tall wall behind the top row, capping the back of the bowl. Keeps its
        /// collider so nothing sails out the back.</summary>
        static void BuildBackWall(Transform p, PitchLayout.Side side, Material concrete, PhysicsMaterial phys)
        {
            PitchLayout.StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            float standLen = halfLength * 2f;
            int topRow = PitchLayout.StandRows - 1;
            float topTread = PitchLayout.StandBaseHeight + topRow * PitchLayout.RowRise;
            float h = topTread + BackWallExtra;
            float outOffset = topRow * PitchLayout.RowDepth + PitchLayout.RowDepth * 0.5f + BackWallThickness * 0.5f;

            Vector3 size = SizeVec(alongDir, standLen + 2f, outDir, BackWallThickness, h);
            Vector3 pos  = P(center, alongDir, 0f, outDir, outOffset, h * 0.5f);
            SolidWall(side + "_BackWall", size, pos, concrete, p, phys);
        }

        // ---------------------------------------------------------------- Roof

        /// <summary>Flat canopy cantilevered from the back of the stand out over the top
        /// rows, dark underside, held up by a few visual props. Pitch stays open to sky.</summary>
        static void BuildRoof(Transform p, PitchLayout.Side side, Material roofMat)
        {
            PitchLayout.StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            float standLen = halfLength * 2f;
            int topRow = PitchLayout.StandRows - 1;
            float topTread = PitchLayout.StandBaseHeight + topRow * PitchLayout.RowRise;
            float backOut  = topRow * PitchLayout.RowDepth + PitchLayout.RowDepth * 0.5f;
            float roofY    = topTread + RoofClearance;
            float roofOut  = backOut - RoofDepth * 0.5f + 1f;   // reaches from the back edge forward over the top rows

            Vector3 size = SizeVec(alongDir, standLen + 2f, outDir, RoofDepth, RoofThickness);
            Vector3 pos  = P(center, alongDir, 0f, outDir, roofOut, roofY);
            Make.Box(side + "_Roof", size, pos, roofMat, p, collider: false);

            // Vertical props from the top terrace up to the roof underside for a supported look.
            float propH  = roofY - topTread;
            float propOut = backOut - 0.6f;
            for (int i = 0; i < RoofSupports; i++)
            {
                float t = RoofSupports == 1 ? 0.5f : i / (float)(RoofSupports - 1);
                float along = Mathf.Lerp(-standLen * 0.4f, standLen * 0.4f, t);
                Vector3 pSize = SizeVec(alongDir, SupportSize, outDir, SupportSize, propH);
                Vector3 pPos  = P(center, alongDir, along, outDir, propOut, topTread + propH * 0.5f);
                Make.Box(side + "_Prop" + i, pSize, pPos, roofMat, p, collider: false);
            }
        }

        // ---------------------------------------------------------------- Tunnel

        /// <summary>Dark recessed box punched into the base of one stand, lined up with the
        /// gap in that side's perimeter wall. Visual only.</summary>
        static void BuildTunnel(Transform p, PitchLayout.Side side, Material tunnelMat)
        {
            PitchLayout.StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            float outOffset = TunnelDepth * 0.5f - 0.2f;   // starts at the wall gap, recedes into the terrace
            Vector3 size = SizeVec(alongDir, TunnelWidth - 0.4f, outDir, TunnelDepth, TunnelHeight);
            Vector3 pos  = P(center, alongDir, 0f, outDir, outOffset, TunnelHeight * 0.5f);
            Make.Box(side + "_Tunnel", size, pos, tunnelMat, p, collider: false);
        }

        // ---------------------------------------------------------------- Corners

        /// <summary>Fill the four gaps where the side stands meet the end stands with a
        /// stepped ziggurat tower that rises to roughly the bowl height. Visual only.</summary>
        static void BuildCorners(Transform stadium, Material concrete)
        {
            var cornerRoot = Make.Empty("Corners", Vector3.zero, stadium).transform;
            float cornerX = PitchLayout.HalfWidth + PitchLayout.StandFrontGap;

            int c = 0;
            foreach (float xSign in new[] { 1f, -1f })
            foreach (bool attack in new[] { true, false })
            {
                float cz = attack ? (PitchLayout.AttackGoalLineZ + PitchLayout.StandFrontGap)
                                  : (PitchLayout.FarGoalLineZ - PitchLayout.StandFrontGap);
                float zSign = attack ? 1f : -1f;
                float baseX = xSign * cornerX + xSign * CornerInsetOut;
                float baseZ = cz + zSign * CornerInsetOut;

                float bottom = 0f;
                for (int i = 0; i < CornerSteps; i++)
                {
                    float t = i / (float)(CornerSteps - 1);
                    float foot = Mathf.Lerp(CornerBaseFoot, CornerTopFoot, t);
                    float segH = Mathf.Lerp(5f, 3.5f, t);
                    float cy = bottom + segH * 0.5f;
                    Make.Box("Corner" + c + "_" + i, new Vector3(foot, segH, foot),
                             new Vector3(baseX, cy, baseZ), concrete, cornerRoot, collider: false);
                    bottom += segH;
                }
                c++;
            }
        }

        // ---------------------------------------------------------------- Pylons

        /// <summary>Four floodlight masts at the outer corners: a tall steel pole (cylinder
        /// axis 1 = Y, matching Arena's uprights) topped by a dark frame carrying an
        /// emissive lamp grid aimed at the pitch centre. Colliders removed (cheap, and they
        /// sit outside the bowl anyway). No real Unity Lights are created.</summary>
        static void BuildPylons(Transform stadium, Material pylonMat, Material frameMat, Material lampMat)
        {
            var pylonRoot = Make.Empty("Pylons", Vector3.zero, stadium).transform;
            Vector3 pitchCenter = new Vector3(0f, 0f, PitchLayout.PitchCenterZ);
            float cornerX = PitchLayout.HalfWidth + PitchLayout.StandFrontGap;

            int c = 0;
            foreach (float xSign in new[] { 1f, -1f })
            foreach (bool attack in new[] { true, false })
            {
                float cz = attack ? (PitchLayout.AttackGoalLineZ + PitchLayout.StandFrontGap)
                                  : (PitchLayout.FarGoalLineZ - PitchLayout.StandFrontGap);
                float zSign = attack ? 1f : -1f;
                float px = xSign * cornerX + xSign * PylonInsetOut;
                float pz = cz + zSign * PylonInsetOut;
                var pos = new Vector3(px, 0f, pz);

                // Pole: vertical cylinder. Destroy the capsule collider Make.Cylinder adds.
                var pole = Make.Cylinder("Pylon" + c + "_Pole", PylonRadius, PylonHeight,
                                         new Vector3(px, PylonHeight * 0.5f, pz), 1, pylonMat, pylonRoot);
                var col = pole.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                // Head: face the pitch centre. inward = flat direction from mast to centre.
                Vector3 inward = pitchCenter - pos; inward.y = 0f; inward = inward.normalized;
                Vector3 perp = new Vector3(-inward.z, 0f, inward.x);   // horizontal, across the head
                float headY = PylonHeight - 1.5f;
                Vector3 headCenter = new Vector3(px, headY, pz);

                // Dark backing frame, then the emissive lamp grid on its pitch-facing face.
                float frameW = LampCols * LampCell + 0.6f;
                float frameH = LampRows * LampCell + 0.6f;
                Vector3 frameSize = perp * frameW + Vector3.up * frameH + inward * 0.5f;
                Make.Box("Pylon" + c + "_Frame",
                         new Vector3(Mathf.Abs(frameSize.x), Mathf.Abs(frameSize.y), Mathf.Abs(frameSize.z)),
                         headCenter, frameMat, pylonRoot, collider: false);

                Vector3 lampBase = headCenter + inward * 0.35f;
                for (int cx2 = 0; cx2 < LampCols; cx2++)
                for (int ry = 0; ry < LampRows; ry++)
                {
                    float off = (cx2 - (LampCols - 1) * 0.5f) * LampCell;
                    float yo  = (ry - (LampRows - 1) * 0.5f) * LampCell;
                    Vector3 lp = lampBase + perp * off + Vector3.up * yo;
                    Vector3 lSize = perp * (LampCell * 0.8f) + Vector3.up * (LampCell * 0.8f) + inward * 0.25f;
                    Make.Box("Pylon" + c + "_Lamp" + cx2 + "_" + ry,
                             new Vector3(Mathf.Max(0.2f, Mathf.Abs(lSize.x)), Mathf.Max(0.2f, Mathf.Abs(lSize.y)), Mathf.Max(0.2f, Mathf.Abs(lSize.z))),
                             lp, lampMat, pylonRoot, collider: false);
                }
                c++;
            }
        }

        // ---------------------------------------------------------------- Helpers

        /// <summary>World position from a StandFront frame: alongOff runs along the stand,
        /// outOff runs outward up the rake, y is absolute height. alongDir/outDir are unit
        /// axis vectors (right/left/forward/back) so this is just a component sum.</summary>
        static Vector3 P(Vector3 center, Vector3 alongDir, float alongOff, Vector3 outDir, float outOff, float y)
        {
            return new Vector3(
                center.x + alongDir.x * alongOff + outDir.x * outOff,
                y,
                center.z + alongDir.z * alongOff + outDir.z * outOff);
        }

        /// <summary>Box size from a StandFront frame. alongDir and outDir are perpendicular
        /// axis-aligned unit vectors (one is the X axis, the other the Z axis), so the along
        /// length lands on whichever world axis alongDir points down, and likewise for out.</summary>
        static Vector3 SizeVec(Vector3 alongDir, float alongLen, Vector3 outDir, float outLen, float height)
        {
            return new Vector3(
                Mathf.Abs(alongDir.x) * alongLen + Mathf.Abs(outDir.x) * outLen,
                height,
                Mathf.Abs(alongDir.z) * alongLen + Mathf.Abs(outDir.z) * outLen);
        }

        static void SolidWall(string name, Vector3 size, Vector3 pos, Material mat, Transform parent, PhysicsMaterial phys)
        {
            var go = Make.Box(name, size, pos, mat, parent, collider: true);
            var col = go.GetComponent<Collider>();
            if (col != null && phys != null) col.material = phys;
        }
    }
}
