using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch8(List<Design> l)
        {
            // Saint Kitts and Nevis: green (lower-fly) / red (upper-hoist) split by a
            // rising black band with yellow edges and two white stars.
            Add(l, "Saint Kitts and Nevis", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 122, 51), red = C(206, 17, 38), gold = C(255, 206, 0);
                DiagHalf(px, green, red, true);          // rising: green below-right, red above-left
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int d = y - x;
                        if (Mathf.Abs(d) < 40) px(x, y, gold);
                        if (Mathf.Abs(d) < 27) px(x, y, Black);
                    }
                DrawStar(px, 84, 84, 18, White);
                DrawStar(px, 172, 172, 18, White);
            });

            // Saint Lucia: cerulean field, twin-Piton emblem (white/black tall triangles
            // over a flat gold triangle).
            Add(l, "Saint Lucia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(101, 199, 235));
                Tri(px, 128, 205, 84, 64, 172, 64, White);        // white (widest, back)
                Tri(px, 128, 205, 96, 64, 160, 64, Black);        // black tall triangle
                Tri(px, 128, 150, 74, 64, 180, 64, C(255, 206, 0)); // gold flat triangle (front)
            });

            // Saint Vincent and the Grenadines: blue / gold / green vertical bands (gold widest),
            // three green diamonds in a V on the gold.
            Add(l, "Saint Vincent and the Grenadines", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 122, 51);
                VBand(px, 0, 63, C(0, 58, 140));
                VBand(px, 64, 191, C(252, 209, 22));
                VBand(px, 192, 255, green);
                Diamond(px, 100, 150, 11, 17, green);
                Diamond(px, 128, 108, 11, 17, green);
                Diamond(px, 156, 150, 11, 17, green);
            });

            // Suriname: green / white / red / white / green horizontal bands (2:1:4:1:2),
            // yellow star centred.
            Add(l, "Suriname", DesignTab.Nations, px =>
            {
                Color32 green = C(55, 126, 63), red = C(197, 22, 43);
                HBand(px, 0, 50, green);
                HBand(px, 51, 76, White);
                HBand(px, 77, 178, red);
                HBand(px, 179, 204, White);
                HBand(px, 205, 255, green);
                DrawStar(px, W / 2, RegionH / 2, 34, C(255, 205, 0));
            });

            // Trinidad and Tobago: red field, white-edged black band on the falling diagonal.
            Add(l, "Trinidad and Tobago", DesignTab.Nations, px =>
            {
                FillRegion(px, C(218, 41, 28));
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int d = (x + y) - 255;
                        if (Mathf.Abs(d) < 46) px(x, y, White);
                        if (Mathf.Abs(d) < 30) px(x, y, Black);
                    }
            });

            // Uruguay: nine white/blue stripes (top white), white canton with the Sun of May.
            Add(l, "Uruguay", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 56, 147);
                for (int i = 0; i < 9; i++)
                {
                    int y0 = i * RegionH / 9, y1 = (i + 1) * RegionH / 9 - 1;
                    HBand(px, y0, y1, (i % 2 == 0) ? White : blue);   // i=0 (bottom) white, i=8 (top) white
                }
                Rect(px, 0, 160, 96, 255, White);                     // upper-hoist canton
                Sun(px, 48, 207, 14, 11, 16, 0f, C(247, 203, 80));
            });

            // Venezuela: yellow / blue / red horizontal triband, arc of eight white stars on blue.
            Add(l, "Venezuela", DesignTab.Nations, px =>
            {
                HBand(px, 0, 85, C(207, 20, 43));      // red bottom
                HBand(px, 86, 170, C(0, 36, 125));     // blue middle
                HBand(px, 171, 255, C(255, 204, 0));   // yellow top
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f;
                    int sx = 70 + (int)(t * 116f);
                    int sy = 104 + (int)(28f * Mathf.Sin(Mathf.PI * t)); // downward-opening arch
                    DrawStar(px, sx, sy, 7, White);
                }
            });

            // Australia: navy field, Union Jack canton (top-left), Commonwealth Star, Southern Cross.
            Add(l, "Australia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 0, 128));
                UnionJack(px, 0, RegionH - 1 - 127, 127, RegionH - 1);   // top-left half
                StarN(px, 64, 62, 30, 7, 0f, White);                     // Commonwealth Star below canton
                StarN(px, 196, 40, 18, 7, 0f, White);                    // Alpha (bottom)
                StarN(px, 196, 210, 16, 7, 0f, White);                   // Gamma (top)
                StarN(px, 150, 120, 16, 7, 0f, White);                   // Beta (left)
                StarN(px, 240, 130, 16, 7, 0f, White);                   // Delta (right)
                StarN(px, 205, 92, 9, 5, 0f, White);                     // Epsilon (small)
            });

            // Fiji: sky-blue field, Union Jack canton, white shield with red cross on the fly.
            Add(l, "Fiji", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38);
                FillRegion(px, C(94, 185, 227));
                UnionJack(px, 0, RegionH - 1 - 127, 127, RegionH - 1);
                Rect(px, 160, 70, 216, 150, White);              // shield body
                Tri(px, 160, 70, 216, 70, 188, 46, White);       // shield point (bottom)
                Rect(px, 184, 46, 192, 150, red);                // cross vertical
                Rect(px, 160, 104, 216, 112, red);               // cross horizontal
                Rect(px, 160, 138, 216, 150, red);               // red chief
            });

            // Kiribati: red top with gold sun + frigatebird, blue bottom with white ocean waves.
            Add(l, "Kiribati", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), blue = C(0, 71, 171), gold = C(252, 209, 22);
                HBand(px, 128, 255, red);
                HBand(px, 0, 127, blue);
                int[] bases = { 35, 70, 105 };
                for (int x = 0; x < W; x++)
                {
                    int off = Mathf.RoundToInt(7f * Mathf.Sin(x * 0.05f));
                    foreach (int b in bases)
                        for (int y = b + off - 3; y <= b + off + 3; y++) px(x, y, White);
                }
                Sun(px, 128, 145, 22, 18, 17, 0f, gold);
                // frigatebird silhouette above the sun
                Disc(px, 128, 200, 8, gold);
                Rect(px, 124, 196, 132, 208, gold);
                Tri(px, 128, 200, 80, 222, 120, 205, gold);      // left wing
                Tri(px, 128, 200, 176, 222, 136, 205, gold);     // right wing
                Tri(px, 128, 200, 120, 180, 136, 180, gold);     // tail
            });

            // Marshall Islands: blue field, orange-over-white diagonal bands widening to the fly,
            // large white 24-point star in the upper hoist.
            Add(l, "Marshall Islands", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 63, 135), orange = C(224, 90, 42);
                FillRegion(px, blue);
                for (int x = 0; x < W; x++)
                {
                    float hw = 6f + x * 0.10f;                   // widen toward the fly
                    for (int y = 0; y < RegionH; y++)
                    {
                        float d = y - x;
                        if (d >= 0 && d < hw) px(x, y, orange);
                        else if (d < 0 && d > -hw) px(x, y, White);
                    }
                }
                StarN(px, 78, 185, 46, 24, 0f, White);
            });

            // Micronesia: light-blue field, four white stars in a diamond.
            Add(l, "Micronesia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(117, 170, 219));
                DrawStar(px, 128, 182, 18, White);
                DrawStar(px, 128, 74, 18, White);
                DrawStar(px, 78, 128, 18, White);
                DrawStar(px, 178, 128, 18, White);
            });

            // Nauru: blue field, thin gold horizontal stripe, white 12-point star lower-hoist.
            Add(l, "Nauru", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 35, 149));
                Rect(px, 0, 122, W - 1, 134, C(255, 206, 0));
                StarN(px, 100, 72, 30, 12, 0f, White);
            });

            // New Zealand: navy field, Union Jack canton, four red white-edged Southern Cross stars.
            Add(l, "New Zealand", DesignTab.Nations, px =>
            {
                Color32 red = C(204, 20, 43);
                FillRegion(px, C(0, 0, 128));
                UnionJack(px, 0, RegionH - 1 - 127, 127, RegionH - 1);
                int[,] s = { { 188, 44 }, { 200, 200 }, { 150, 120 }, { 226, 128 } };
                for (int i = 0; i < 4; i++)
                {
                    DrawStar(px, s[i, 0], s[i, 1], 18, White);
                    DrawStar(px, s[i, 0], s[i, 1], 13, red);
                }
            });

            // Palau: light-blue field, gold full-moon disc offset toward the hoist.
            Add(l, "Palau", DesignTab.Nations, px =>
            {
                FillRegion(px, C(56, 158, 216));
                Disc(px, 116, 128, 62, C(252, 222, 84));
            });

            // Papua New Guinea: falling-diagonal split, black (lower-hoist) with Southern Cross,
            // red (upper-fly) with a gold bird-of-paradise silhouette.
            Add(l, "Papua New Guinea", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), gold = C(252, 209, 22);
                DiagHalf(px, Black, red, false);                 // black lower-left, red upper-right
                // bird of paradise (gold) in the red triangle
                Disc(px, 168, 168, 9, gold);
                Rect(px, 168, 150, 176, 168, gold);
                Tri(px, 168, 168, 205, 150, 200, 182, gold);     // wing
                Tri(px, 172, 160, 220, 118, 210, 140, gold);     // tail plume
                Tri(px, 172, 158, 214, 108, 205, 130, gold);     // tail plume
                // Southern Cross (white) in the black triangle
                DrawStar(px, 70, 150, 12, White);
                DrawStar(px, 110, 120, 12, White);
                DrawStar(px, 60, 90, 12, White);
                DrawStar(px, 95, 70, 12, White);
                DrawStar(px, 85, 110, 7, White);
            });

            // Samoa: red field, blue canton (upper-hoist) with the Southern Cross.
            Add(l, "Samoa", DesignTab.Nations, px =>
            {
                FillRegion(px, C(206, 17, 38));
                Rect(px, 0, 128, 127, 255, C(0, 40, 104));       // upper-hoist canton
                DrawStar(px, 66, 216, 13, White);                // top
                DrawStar(px, 58, 150, 13, White);                // bottom
                DrawStar(px, 34, 182, 13, White);                // left
                DrawStar(px, 98, 176, 13, White);                // right
                DrawStar(px, 80, 205, 8, White);                 // small
            });

            // Solomon Islands: blue (upper-hoist) / green (lower-fly) split by a thin yellow
            // rising diagonal, five white stars in the blue.
            Add(l, "Solomon Islands", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 81, 186), green = C(33, 140, 54), gold = C(253, 209, 0);
                DiagHalf(px, green, blue, true);                 // rising: green below-right, blue above-left
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                        if (Mathf.Abs(y - x) < 7) px(x, y, gold);
                DrawStar(px, 50, 210, 10, White);
                DrawStar(px, 95, 215, 10, White);
                DrawStar(px, 45, 160, 10, White);
                DrawStar(px, 90, 165, 10, White);
                DrawStar(px, 68, 188, 10, White);
            });

            // Tonga: red field, white canton (upper-hoist) with a red couped cross.
            Add(l, "Tonga", DesignTab.Nations, px =>
            {
                Color32 red = C(200, 16, 46);
                FillRegion(px, red);
                Rect(px, 0, 150, 104, 255, White);               // canton
                Rect(px, 44, 168, 60, 240, red);                 // cross vertical arm
                Rect(px, 28, 196, 76, 212, red);                 // cross horizontal arm
            });

            // Tuvalu: light-blue field, Union Jack canton, nine gold stars on the fly.
            Add(l, "Tuvalu", DesignTab.Nations, px =>
            {
                Color32 gold = C(255, 206, 0);
                FillRegion(px, C(94, 185, 227));
                UnionJack(px, 0, RegionH - 1 - 127, 127, RegionH - 1);
                int[,] s = { { 170, 210 }, { 215, 180 }, { 240, 120 }, { 210, 70 },
                             { 160, 60 }, { 150, 150 }, { 200, 232 }, { 178, 108 }, { 232, 214 } };
                for (int i = 0; i < 9; i++) DrawStar(px, s[i, 0], s[i, 1], 9, gold);
            });

            // Vanuatu: red-over-green halves, black pall with gold fimbriation, gold boar-tusk emblem.
            Add(l, "Vanuatu", DesignTab.Nations, px =>
            {
                Color32 red = C(210, 16, 52), green = C(0, 146, 70), gold = C(255, 206, 0);
                HBand(px, 128, 255, red);
                HBand(px, 0, 127, green);
                // gold pall (wider) then black pall (narrower) => gold reads as fimbriation
                Tri(px, 0, 0, 0, 255, 168, 128, gold);           // gold hoist triangle
                Rect(px, 104, 104, W - 1, 152, gold);            // gold stem
                Tri(px, 0, 18, 0, 237, 150, 128, Black);         // black hoist triangle
                Rect(px, 118, 118, W - 1, 138, Black);           // black stem
                // boar-tusk + crossed namele leaves (gold) on the black triangle
                Crescent(px, 60, 128, 22, 11, Black, gold);
                for (int t = -14; t <= 14; t++)
                {
                    Disc(px, 58 + t, 128 + t, 1, gold);
                    Disc(px, 58 + t, 128 - t, 1, gold);
                }
            });
        }
    }
}
