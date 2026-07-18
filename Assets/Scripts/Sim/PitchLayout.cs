using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// SHARED GEOMETRY CONTRACT for the full pitch + stadium build. Every agent that
    /// touches the arena, stadium, or crowd reads dimensions and stand layout from here
    /// so the pieces line up. Nothing here builds anything - it is pure data + helpers.
    ///
    /// Coordinate frame matches SimConfig: +Z runs toward the attacking goal, +X is the
    /// pitch width. The existing goal sits at SimConfig.GoalCenter (z = FieldLength*0.5),
    /// and the FULL pitch is laid out so THAT goal is the attacking goal line - gameplay
    /// stays in the attacking third exactly where it is now; the pitch just extends back
    /// and out, wrapped by stands.
    /// </summary>
    public static class PitchLayout
    {
        // ---- Full regulation-ish pitch (metres) ----
        public const float PitchLength = 105f;   // along Z
        public const float PitchWidth  = 68f;    // along X

        // Attacking goal line = the existing goal's z. The pitch runs from there back.
        public static float AttackGoalLineZ => SimConfig.GoalCenter.z;         // z = +17 by default
        public static float FarGoalLineZ    => AttackGoalLineZ - PitchLength;  // opposite end
        public static float PitchCenterZ    => AttackGoalLineZ - PitchLength * 0.5f;
        // X is centred on 0.
        public const float HalfWidth = PitchWidth * 0.5f;   // +/-34

        // Grass surround (runoff) beyond the touch/goal lines before the stands rise.
        public const float Runoff = 6f;

        // ---- Stands (one per side, tiered terraces of seats) ----
        // A stand is a raked bank of rows rising away from the pitch. Seats step UP and
        // BACK each row. The crowd fills these seats; the stadium builds the structure
        // (steps, walls, roof) over the same footprint. The rake shape comes from the
        // SELECTED venue (StadiumStyle.Active) so every builder that reads these agrees.
        public static int   StandRows       => StadiumStyle.Active.StandRows;
        public static float RowRise         => StadiumStyle.Active.RowRise;
        public static float RowDepth        => StadiumStyle.Active.RowDepth;
        public static float StandBaseHeight => StadiumStyle.Active.StandBaseHeight;
        public const  float SeatSpacing     = 1.15f;   // metres between fans along a row
        public const  float StandFrontGap   = Runoff + 2f;  // distance from the line to row 0

        public enum Side { PlusX, MinusX, AttackEnd, FarEnd }

        public struct Seat
        {
            public Vector3 pos;       // world seat position (fan stands here)
            public Quaternion facing; // faces the pitch centre
            public Side side;
            public int row;
        }

        /// <summary>Front-edge world rectangle (centre line at pitch level) of a stand
        /// side, plus the outward direction the rows climb. Used by the stadium builder.</summary>
        public static void StandFront(Side side, out Vector3 center, out Vector3 alongDir,
                                      out Vector3 outDir, out float halfLength)
        {
            float cz = PitchCenterZ;
            switch (side)
            {
                case Side.PlusX:
                    center = new Vector3(HalfWidth + StandFrontGap, 0f, cz);
                    alongDir = Vector3.forward; outDir = Vector3.right;
                    halfLength = PitchLength * 0.5f + Runoff;
                    break;
                case Side.MinusX:
                    center = new Vector3(-HalfWidth - StandFrontGap, 0f, cz);
                    alongDir = Vector3.forward; outDir = Vector3.left;
                    halfLength = PitchLength * 0.5f + Runoff;
                    break;
                case Side.AttackEnd:
                    center = new Vector3(0f, 0f, AttackGoalLineZ + StandFrontGap);
                    alongDir = Vector3.right; outDir = Vector3.forward;
                    halfLength = HalfWidth + Runoff;
                    break;
                default: // FarEnd
                    center = new Vector3(0f, 0f, FarGoalLineZ - StandFrontGap);
                    alongDir = Vector3.right; outDir = Vector3.back;
                    halfLength = HalfWidth + Runoff;
                    break;
            }
        }

        /// <summary>Enumerate every seat in a stand side, in world space, each facing the
        /// pitch centre. The crowd builder places one fan per seat.</summary>
        public static IEnumerable<Seat> Seats(Side side)
        {
            StandFront(side, out Vector3 center, out Vector3 alongDir, out Vector3 outDir, out float halfLength);
            Vector3 pitchCenter = new Vector3(0f, 0f, PitchCenterZ);
            int seatsPerRow = Mathf.Max(1, Mathf.FloorToInt((halfLength * 2f) / SeatSpacing));
            for (int row = 0; row < StandRows; row++)
            {
                Vector3 rowBase = center
                    + outDir * (StandFrontGap * 0f + row * RowDepth)   // step back
                    + Vector3.up * (StandBaseHeight + row * RowRise);   // step up
                for (int s = 0; s < seatsPerRow; s++)
                {
                    float along = -halfLength + (s + 0.5f) * SeatSpacing;
                    Vector3 pos = rowBase + alongDir * along;
                    Vector3 look = pitchCenter - pos; look.y = 0f;
                    Quaternion facing = look.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(look.normalized, Vector3.up)
                        : Quaternion.identity;
                    yield return new Seat { pos = pos, facing = facing, side = side, row = row };
                }
            }
        }

        public static readonly Side[] AllSides =
            { Side.PlusX, Side.MinusX, Side.AttackEnd, Side.FarEnd };
    }
}
