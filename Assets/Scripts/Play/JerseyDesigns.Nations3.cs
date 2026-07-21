using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch3(List<Design> l)
        {
            // Afghanistan: black/red/green vertical triband with a white central emblem (mosque).
            Add(l, "Afghanistan", DesignTab.Nations, px =>
            {
                Color32 red = C(190, 20, 20), green = C(0, 122, 60);
                VTriband(px, Black, red, green);
                Disc(px, 128, 128, 24, White);          // emblem roundel
                Rect(px, 116, 118, 140, 140, White);    // mihrab block
                Tri(px, 116, 140, 140, 140, 128, 156, White);  // arch peak
                Disc(px, 128, 128, 8, red);             // mihrab niche
            });

            // Armenia: horizontal triband red / blue / orange.
            Add(l, "Armenia", DesignTab.Nations, px =>
                HBands(px, C(217, 0, 18), C(0, 51, 160), C(242, 168, 0)));

            // Azerbaijan: blue/red/green horizontal triband, white crescent + 8-point star.
            Add(l, "Azerbaijan", DesignTab.Nations, px =>
            {
                Color32 red = C(239, 51, 64);
                HBands(px, C(0, 181, 226), red, C(80, 155, 35));
                Crescent(px, 118, 128, 34, 14, red, White);
                StarN(px, 158, 128, 15, 8, 22.5f, White);
            });

            // Bahrain: white hoist with 5 red serrations, red fly.
            Add(l, "Bahrain", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38);
                FillRegion(px, red);
                Rect(px, 0, 0, 70, RegionH - 1, White);
                for (int i = 0; i < 5; i++)
                {
                    int y0 = i * RegionH / 5;
                    int y1 = (i + 1) * RegionH / 5 - 1;
                    int yc = (y0 + y1) / 2;
                    Tri(px, 70, y0, 70, y1, 110, yc, White);
                }
            });

            // Bhutan: diagonal split yellow (upper-hoist) / orange (lower-fly), white dragon.
            Add(l, "Bhutan", DesignTab.Nations, px =>
            {
                DiagHalf(px, C(255, 102, 0), C(255, 205, 0), true);
                // white dragon approximated along the diagonal
                Disc(px, 128, 128, 20, White);
                Disc(px, 100, 108, 14, White);
                Disc(px, 156, 148, 14, White);
                Disc(px, 84, 96, 9, White);
                Disc(px, 172, 160, 9, White);
                Disc(px, 138, 138, 4, C(60, 60, 60));   // eye hint
            });

            // Brunei: yellow field, white-over-black diagonal stripes, red emblem.
            Add(l, "Brunei", DesignTab.Nations, px =>
            {
                Color32 yellow = C(247, 222, 0), red = C(199, 0, 0);
                FillRegion(px, yellow);
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int d = x + y - (RegionH - 1);   // 0 on the main falling diagonal
                        if (d >= -4 && d <= 40) px(x, y, White);
                        else if (d >= -44 && d < -4) px(x, y, Black);
                    }
                // red emblem (parasol + crescent-ish arc)
                Disc(px, 128, 132, 16, red);
                Ring(px, 128, 118, 30, 24, red);
                Rect(px, 108, 150, 148, 156, red);       // wings/hands bar
            });

            // Cambodia: blue/red/blue horizontal bands, white Angkor Wat.
            Add(l, "Cambodia", DesignTab.Nations, px =>
            {
                Color32 blue = C(3, 45, 116), red = C(224, 0, 37);
                HBand(px, 0, RegionH / 4 - 1, blue);
                HBand(px, RegionH / 4, 3 * RegionH / 4 - 1, red);
                HBand(px, 3 * RegionH / 4, RegionH - 1, blue);
                int by = 96;
                Rect(px, 86, by, 170, by + 8, White);            // base
                Rect(px, 120, by + 8, 136, by + 70, White);      // centre tower
                Tri(px, 120, by + 70, 136, by + 70, 128, by + 92, White);
                Rect(px, 96, by + 8, 110, by + 48, White);       // left tower
                Tri(px, 96, by + 48, 110, by + 48, 103, by + 64, White);
                Rect(px, 146, by + 8, 160, by + 48, White);      // right tower
                Tri(px, 146, by + 48, 160, by + 48, 153, by + 64, White);
            });

            // China: red field, large gold star upper-hoist, 4 small stars arcing.
            Add(l, "China", DesignTab.Nations, px =>
            {
                Color32 gold = C(255, 222, 0);
                FillRegion(px, C(222, 41, 16));
                DrawStar(px, 54, 200, 26, gold);
                StarN(px, 98, 228, 9, 5, -18f, gold);
                StarN(px, 120, 212, 9, 5, -40f, gold);
                StarN(px, 120, 186, 9, 5, -58f, gold);
                StarN(px, 98, 168, 9, 5, -74f, gold);
            });

            // Cyprus: white field, copper island silhouette, two green olive branches.
            Add(l, "Cyprus", DesignTab.Nations, px =>
            {
                Color32 copper = C(215, 125, 0), olive = C(78, 145, 55);
                FillRegion(px, White);
                Disc(px, 116, 150, 26, copper);
                Rect(px, 92, 142, 168, 160, copper);
                Tri(px, 160, 150, 196, 158, 168, 142, copper);   // Karpas tail
                // olive branches below
                Tri(px, 104, 122, 126, 122, 115, 108, olive);
                Tri(px, 130, 122, 152, 122, 141, 108, olive);
                Disc(px, 118, 118, 4, olive);
                Disc(px, 138, 118, 4, olive);
            });

            // Georgia: white field, large red cross + 4 small Bolnisi crosses.
            Add(l, "Georgia", DesignTab.Nations, px =>
            {
                Color32 red = C(255, 0, 0);
                FillRegion(px, White);
                PlusCross(px, 128, 128, 16, red);
                int[,] q = { { 64, 192 }, { 192, 192 }, { 64, 64 }, { 192, 64 } };
                for (int i = 0; i < 4; i++)
                {
                    int cx = q[i, 0], cy = q[i, 1];
                    Rect(px, cx - 14, cy - 5, cx + 14, cy + 5, red);
                    Rect(px, cx - 5, cy - 14, cx + 5, cy + 14, red);
                }
            });

            // India: saffron/white/green triband, navy Ashoka Chakra with 24 spokes.
            Add(l, "India", DesignTab.Nations, px =>
            {
                HBands(px, C(255, 153, 51), White, C(19, 136, 8));
                Color32 navy = C(0, 0, 128);
                Ring(px, 128, 128, 30, 26, navy);
                Disc(px, 128, 128, 4, navy);
                for (int k = 0; k < 24; k++)
                {
                    float a = k * (Mathf.PI * 2f / 24f);
                    for (int t = 0; t <= 28; t++)
                    {
                        int sx = 128 + Mathf.RoundToInt(t * Mathf.Cos(a));
                        int sy = 128 + Mathf.RoundToInt(t * Mathf.Sin(a));
                        px(sx, sy, navy);
                    }
                }
            });

            // Indonesia: red over white.
            Add(l, "Indonesia", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(255, 0, 0));
                HBand(px, 0, RegionH / 2 - 1, White);
            });

            // Iran: green/white/red triband with central red emblem + edge script hint.
            Add(l, "Iran", DesignTab.Nations, px =>
            {
                Color32 green = C(35, 159, 64), red = C(218, 0, 0);
                HTriband(px, green, White, red);
                // central emblem (stylised sword + curls) approximation
                Rect(px, 124, 114, 132, 152, red);
                Ring(px, 108, 132, 15, 10, red);
                Ring(px, 148, 132, 15, 10, red);
                Disc(px, 128, 158, 5, red);
                // takbir script hint: tick marks along the two inner band edges
                for (int i = 0; i < 11; i++)
                {
                    int x = 10 + i * 22;
                    Rect(px, x, 168, x + 8, 172, White);   // along green/white edge
                    Rect(px, x, 84, x + 8, 88, White);     // along white/red edge
                }
            });

            // Iraq: red/white/black triband with green Takbir script (approximated).
            Add(l, "Iraq", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 122, 61);
                HTriband(px, C(206, 17, 38), White, Black);
                for (int i = 0; i < 3; i++)
                {
                    int cx = 78 + i * 50;
                    Rect(px, cx - 16, 126, cx + 16, 132, green);
                    Disc(px, cx - 10, 138, 5, green);
                    Disc(px, cx + 8, 140, 5, green);
                    Disc(px, cx, 120, 4, green);
                }
            });

            // Israel: white field, two blue stripes, blue Star of David (hexagram outline).
            Add(l, "Israel", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 56, 184);
                FillRegion(px, White);
                HBand(px, 196, 214, blue);
                HBand(px, 42, 60, blue);
                // solid hexagram (two triangles), then carve white inner to leave the outline
                Tri(px, 128, 164, 97, 110, 159, 110, blue);   // up triangle
                Tri(px, 128, 92, 97, 146, 159, 146, blue);    // down triangle
                Tri(px, 128, 150, 109, 117, 147, 117, White); // carve up
                Tri(px, 128, 106, 109, 139, 147, 139, White); // carve down
            });

            // Jordan: black/white/green triband, red hoist chevron with white 7-point star.
            Add(l, "Jordan", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38);
                HTriband(px, Black, White, C(0, 122, 61));
                Tri(px, 0, 0, 0, RegionH - 1, 128, RegionH / 2, red);
                StarN(px, 46, 128, 17, 7, 0f, White);
            });

            // Kazakhstan: sky-blue field, gold sun + eagle, gold hoist ornament.
            Add(l, "Kazakhstan", DesignTab.Nations, px =>
            {
                Color32 gold = C(255, 213, 0);
                FillRegion(px, C(0, 175, 201));
                Sun(px, 132, 152, 22, 20, 24, 0f, gold);
                // eagle below the sun (spread wings approximation)
                Tri(px, 132, 120, 88, 108, 132, 116, gold);
                Tri(px, 132, 120, 176, 108, 132, 116, gold);
                Disc(px, 132, 116, 6, gold);
                Tri(px, 122, 100, 142, 100, 132, 112, gold);   // tail
                // hoist ornament
                for (int y = 12; y < RegionH - 12; y += 26) Disc(px, 14, y, 5, gold);
            });

            // Kuwait: green/white/red triband with black hoist trapezoid.
            Add(l, "Kuwait", DesignTab.Nations, px =>
            {
                HTriband(px, C(0, 122, 61), White, C(206, 17, 38));
                Tri(px, 0, 0, 0, RegionH - 1, 85, 2 * RegionH / 3, Black);
                Tri(px, 0, 0, 85, RegionH / 3, 85, 2 * RegionH / 3, Black);
            });

            // Kyrgyzstan: red field with the sun-and-tunduk emblem (matches the reference:
            // a gold spiky sun, a red rim, and a red tunduk = crossed yurt-crown latticework
            // over a gold centre). Background red is matched to the reference image.
            Add(l, "Kyrgyzstan", DesignTab.Nations, px =>
            {
                Color32 red = C(232, 17, 45), gold = C(255, 200, 0);   // background red = #E8112D
                FillRegion(px, red);
                int cx = 128, cy = 128;
                // Prefer a real PNG (Assets/Resources/flags/kyrgyz_sun.png, Read/Write on)
                // centred on the field; fall back to a drawn sun + tunduk if it's absent.
                if (!OverlayImage(px, "kyrgyz_sun", cx, cy, 180, 180))
                {
                    Sun(px, cx, cy, 66, 30, 40, 4.5f, gold);
                    Ring(px, cx, cy, 66, 56, red);
                    Ring(px, cx, cy, 40, 34, red);
                    float[] angs = { -35f, -18f, 18f, 35f };
                    foreach (float ad in angs)
                    {
                        float a = ad * Mathf.Deg2Rad;
                        for (int t = -34; t <= 34; t++)
                        {
                            int x = cx + Mathf.RoundToInt(t * Mathf.Cos(a));
                            int y = cy + Mathf.RoundToInt(t * Mathf.Sin(a)) + 6;
                            Disc(px, x, y, 3, red);
                        }
                    }
                    Rect(px, cx - 30, cy + 2, cx + 30, cy + 7, red);
                    Rect(px, cx - 26, cy - 12, cx + 26, cy - 7, red);
                }
            });

            // Laos: red/blue/red bands with a large white disc.
            Add(l, "Laos", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), blue = C(0, 32, 91);
                HBand(px, 0, RegionH / 4 - 1, red);
                HBand(px, RegionH / 4, 3 * RegionH / 4 - 1, blue);
                HBand(px, 3 * RegionH / 4, RegionH - 1, red);
                Disc(px, 128, 128, 40, White);
            });
        }
    }
}
