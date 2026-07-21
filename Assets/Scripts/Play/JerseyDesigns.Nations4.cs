using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch4(List<Design> l)
        {
            // Lebanon: red top/bottom quarters, white middle half, green cedar centered.
            Add(l, "Lebanon", DesignTab.Nations, px =>
            {
                Color32 red = C(237, 28, 36), green = C(0, 122, 61), brown = C(101, 67, 33);
                FillRegion(px, White);
                HBand(px, 0, 63, red);
                HBand(px, 192, 255, red);
                Rect(px, 124, 92, 132, 112, brown);          // trunk
                Tri(px, 96, 112, 160, 112, 128, 140, green); // lower tier
                Tri(px, 104, 128, 152, 128, 128, 152, green);
                Tri(px, 110, 144, 146, 144, 128, 166, green);
            });

            // Malaysia: 14 red/white stripes, blue canton upper-hoist, yellow crescent + 14-pt star.
            Add(l, "Malaysia", DesignTab.Nations, px =>
            {
                Color32 red = C(204, 0, 0), blue = C(1, 0, 102), yellow = C(255, 204, 0);
                HBands(px, red, White, red, White, red, White, red, White, red, White, red, White, red, White);
                Rect(px, 0, 128, 130, 255, blue);            // canton
                Crescent(px, 52, 190, 30, 13, blue, yellow);
                StarN(px, 96, 188, 20, 14, 0f, yellow);
            });

            // Maldives: red field, green central panel, white crescent opening toward fly.
            Add(l, "Maldives", DesignTab.Nations, px =>
            {
                Color32 red = C(212, 0, 0), green = C(0, 122, 61);
                FillRegion(px, red);
                Rect(px, 44, 40, 212, 216, green);
                Crescent(px, 150, 128, 34, 15, green, White);
            });

            // Mongolia: red/blue/red vertical bands, gold Soyombo on the hoist band (approx).
            Add(l, "Mongolia", DesignTab.Nations, px =>
            {
                Color32 red = C(197, 42, 53), blue = C(0, 102, 178), g = C(246, 190, 0);
                VTriband(px, red, blue, red);
                int sx = 42;
                Tri(px, sx - 10, 214, sx + 10, 214, sx, 236, g);   // flame
                Tri(px, sx - 4, 214, sx + 4, 214, sx, 230, g);
                Disc(px, sx, 200, 7, g);                            // sun
                Crescent(px, sx, 184, 8, 4, red, g);               // moon
                Tri(px, sx - 9, 172, sx + 9, 172, sx, 158, g);     // upper spear
                Rect(px, sx - 16, 146, sx + 16, 152, g);           // bar
                Rect(px, sx - 16, 108, sx + 16, 114, g);           // bar
                Tri(px, sx - 9, 90, sx + 9, 90, sx, 104, g);       // lower spear
                Rect(px, sx - 18, 116, sx - 12, 144, g);           // walls
                Rect(px, sx + 12, 116, sx + 18, 144, g);
                Disc(px, sx, 130, 9, g);                           // yin-yang core
            });

            // Myanmar: yellow/green/red horizontal triband, large white star centered.
            Add(l, "Myanmar", DesignTab.Nations, px =>
            {
                HTriband(px, C(254, 203, 0), C(52, 178, 51), C(234, 40, 57));
                DrawStar(px, 128, 128, 54, White);
            });

            // Nepal: double-pennon approximated as two stacked triangles, blue border, crimson field,
            // white moon (upper) and white sun (lower). Heavily approximated shape.
            Add(l, "Nepal", DesignTab.Nations, px =>
            {
                Color32 crimson = C(220, 20, 60), blue = C(0, 53, 148);
                FillRegion(px, White);
                // blue outline triangles
                Tri(px, 40, 250, 40, 120, 178, 150, blue);
                Tri(px, 40, 134, 40, 6, 208, 116, blue);
                // crimson field inset
                Tri(px, 50, 240, 50, 126, 160, 150, crimson);
                Tri(px, 50, 128, 50, 16, 188, 118, crimson);
                // moon (upper pennon)
                Crescent(px, 90, 176, 15, -6, crimson, White);
                // sun (lower pennon)
                Sun(px, 96, 70, 9, 8, 12, 0f, White);
            });

            // North Korea: red field, blue stripes top/bottom with white fimbriations, white disc + red star.
            Add(l, "North Korea", DesignTab.Nations, px =>
            {
                Color32 red = C(237, 28, 36), blue = C(2, 73, 150);
                FillRegion(px, red);
                HBand(px, 216, 255, blue);
                HBand(px, 208, 215, White);
                HBand(px, 0, 39, blue);
                HBand(px, 40, 47, White);
                Disc(px, 92, 128, 34, White);
                DrawStar(px, 92, 128, 22, red);
            });

            // Oman: red hoist band with white emblem, white/red/green horizontal bands on the fly.
            Add(l, "Oman", DesignTab.Nations, px =>
            {
                Color32 red = C(199, 0, 0), green = C(0, 122, 61);
                HBands(px, White, red, green);   // top white, mid red, bottom green
                VBand(px, 0, 60, red);           // hoist band
                // national emblem (khanjar + crossed swords) approximated in white
                Rect(px, 27, 200, 33, 230, White);       // dagger
                Tri(px, 27, 230, 33, 230, 30, 240, White);
                Rect(px, 14, 210, 46, 216, White);       // crossbar
                Disc(px, 30, 198, 5, White);             // belt boss
            });

            // Pakistan: dark green field, white hoist bar, white crescent + star.
            Add(l, "Pakistan", DesignTab.Nations, px =>
            {
                Color32 green = C(1, 66, 37);
                FillRegion(px, green);
                Rect(px, 0, 0, 64, 255, White);
                Crescent(px, 150, 118, 34, 15, green, White);
                StarN(px, 182, 150, 18, 5, 20f, White);
            });

            // Philippines: blue over red, white hoist triangle, gold sun + 3 stars.
            Add(l, "Philippines", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 56, 168), red = C(206, 17, 38), gold = C(255, 205, 0);
                HBand(px, 128, 255, blue);
                HBand(px, 0, 127, red);
                Tri(px, 0, 0, 0, 255, 148, 128, White);   // hoist triangle
                Sun(px, 46, 128, 14, 12, 8, 0f, gold);
                DrawStar(px, 14, 240, 10, gold);
                DrawStar(px, 14, 16, 10, gold);
                DrawStar(px, 120, 128, 10, gold);
            });

            // Qatar: white hoist, maroon fly, 9 white serrations.
            Add(l, "Qatar", DesignTab.Nations, px =>
            {
                Color32 maroon = C(138, 21, 56);
                FillRegion(px, maroon);
                Rect(px, 0, 0, 96, 255, White);
                for (int i = 0; i < 9; i++)
                {
                    int y0 = i * RegionH / 9;
                    int y1 = (i + 1) * RegionH / 9 - 1;
                    int yc = (y0 + y1) / 2;
                    Tri(px, 96, y0, 96, y1, 124, yc, White);
                }
            });

            // Saudi Arabia: green field, white shahada bar + white sword beneath (approx).
            Add(l, "Saudi Arabia", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 122, 61);
                FillRegion(px, green);
                Rect(px, 40, 145, 216, 170, White);       // script block
                Rect(px, 46, 120, 210, 126, White);       // sword blade
                Rect(px, 204, 112, 214, 134, White);      // hilt
                Tri(px, 46, 120, 46, 126, 32, 123, White);// tip toward hoist
            });

            // Singapore: red over white, white crescent + 5 stars upper-hoist.
            Add(l, "Singapore", DesignTab.Nations, px =>
            {
                Color32 red = C(237, 28, 36);
                HBand(px, 128, 255, red);
                HBand(px, 0, 127, White);
                Crescent(px, 62, 200, 30, 13, red, White);
                StarRing(px, 108, 200, 26, 9, 5, White);
            });

            // South Korea: white field, red/blue taegeuk (top-half red, bottom-half blue), 4 trigrams.
            Add(l, "South Korea", DesignTab.Nations, px =>
            {
                Color32 red = C(205, 46, 58), blue = C(0, 71, 160);
                FillRegion(px, White);
                int cx = 128, cy = 128, r = 52;
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dx = x - cx, dy = y - cy;
                        if (dx * dx + dy * dy <= r * r) px(x, y, y >= cy ? red : blue);
                    }
                int[,] q = { { 54, 202 }, { 54, 54 }, { 202, 202 }, { 202, 54 } };
                for (int i = 0; i < 4; i++)
                {
                    int bx = q[i, 0], by = q[i, 1];
                    Rect(px, bx - 22, by + 8, bx + 22, by + 13, Black);
                    Rect(px, bx - 22, by - 2, bx + 22, by + 3, Black);
                    Rect(px, bx - 22, by - 12, bx + 22, by - 7, Black);
                }
            });

            // Sri Lanka: gold border, green+orange hoist stripes, maroon panel with gold lion + 4 bo leaves.
            Add(l, "Sri Lanka", DesignTab.Nations, px =>
            {
                Color32 gold = C(255, 190, 30), green = C(0, 136, 74), orange = C(240, 124, 0), maroon = C(140, 21, 45);
                FillRegion(px, gold);
                Rect(px, 28, 22, 68, 234, green);
                Rect(px, 68, 22, 108, 234, orange);
                Rect(px, 120, 22, 236, 234, maroon);
                Disc(px, 178, 118, 24, gold);             // lion body
                Rect(px, 158, 126, 174, 150, gold);       // head/mane
                Rect(px, 168, 92, 200, 116, gold);        // haunch
                Rect(px, 196, 116, 202, 172, gold);       // sword
                Diamond(px, 130, 40, 8, 12, gold);        // bo leaves
                Diamond(px, 226, 40, 8, 12, gold);
                Diamond(px, 130, 216, 8, 12, gold);
                Diamond(px, 226, 216, 8, 12, gold);
            });

            // Syria: red/white/black horizontal triband, two green stars in the white band.
            Add(l, "Syria", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 122, 61);
                HTriband(px, C(206, 17, 38), White, Black);
                StarN(px, 100, 128, 16, 5, 0f, green);
                StarN(px, 156, 128, 16, 5, 0f, green);
            });

            // Taiwan: red field, blue canton upper-hoist, white 12-ray sun.
            Add(l, "Taiwan", DesignTab.Nations, px =>
            {
                Color32 red = C(254, 0, 0), blue = C(0, 0, 149);
                FillRegion(px, red);
                Rect(px, 0, 128, 128, 255, blue);
                Sun(px, 64, 192, 16, 14, 12, 0f, White);
                Disc(px, 64, 192, 11, blue);
                Disc(px, 64, 192, 7, White);
            });

            // Tajikistan: red/white/green bands (white wider), gold crown + 7 stars arc.
            Add(l, "Tajikistan", DesignTab.Nations, px =>
            {
                Color32 red = C(200, 16, 46), green = C(0, 109, 58), gold = C(255, 215, 0);
                HBand(px, 0, 72, green);
                HBand(px, 73, 182, White);
                HBand(px, 183, 255, red);
                Rect(px, 106, 116, 150, 124, gold);        // crown base
                Tri(px, 106, 124, 118, 124, 112, 138, gold);
                Tri(px, 122, 124, 134, 124, 128, 140, gold);
                Tri(px, 138, 124, 150, 124, 144, 138, gold);
                for (int i = 0; i < 7; i++)
                {
                    float a = Mathf.PI * (0.12f + 0.76f * i / 6f);
                    int sx = 128 + Mathf.RoundToInt(42f * Mathf.Cos(a));
                    int sy = 128 + Mathf.RoundToInt(30f * Mathf.Sin(a));
                    StarN(px, sx, sy, 5, 5, 0f, gold);
                }
            });

            // Thailand: red/white/blue(double)/white/red horizontal bands.
            Add(l, "Thailand", DesignTab.Nations, px =>
            {
                Color32 red = C(165, 25, 49), blue = C(45, 42, 74);
                HBand(px, 0, 41, red);
                HBand(px, 42, 83, White);
                HBand(px, 84, 167, blue);
                HBand(px, 168, 209, White);
                HBand(px, 210, 255, red);
            });

            // Timor-Leste: red field, yellow triangle then narrower black triangle from hoist, white star.
            Add(l, "Timor-Leste", DesignTab.Nations, px =>
            {
                Color32 red = C(218, 37, 29), yellow = C(255, 199, 44);
                FillRegion(px, red);
                Tri(px, 0, 0, 0, 255, 175, 128, yellow);
                Tri(px, 0, 0, 0, 255, 108, 128, Black);
                DrawStar(px, 42, 128, 16, White);
            });

            // Turkey: red field, white crescent + white star.
            Add(l, "Turkey", DesignTab.Nations, px =>
            {
                Color32 red = C(227, 10, 23);
                FillRegion(px, red);
                Crescent(px, 100, 128, 42, 16, red, White);
                StarN(px, 160, 128, 22, 5, 15f, White);
            });

            // Turkmenistan: green field, maroon carpet stripe with guls, white crescent + 5 stars.
            Add(l, "Turkmenistan", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 128, 58), maroon = C(122, 0, 0), orange = C(230, 120, 40);
                FillRegion(px, green);
                Rect(px, 40, 12, 84, 244, maroon);
                for (int i = 0; i < 5; i++)
                {
                    int y = 36 + i * 44;
                    Diamond(px, 62, y, 12, 16, orange);
                    Diamond(px, 62, y, 6, 9, maroon);
                }
                Crescent(px, 150, 196, 26, 11, green, White);
                for (int i = 0; i < 5; i++)
                    StarN(px, 194, 172 + i * 13, 6, 5, 0f, White);
            });

            // United Arab Emirates: red hoist band, green/white/black horizontal bands.
            Add(l, "United Arab Emirates", DesignTab.Nations, px =>
            {
                HBands(px, C(0, 115, 47), White, Black);
                VBand(px, 0, 63, C(255, 0, 0));
            });

            // Uzbekistan: blue/white/green bands with red fimbriations, white crescent + 12 stars.
            Add(l, "Uzbekistan", DesignTab.Nations, px =>
            {
                Color32 blue = C(30, 144, 220), green = C(30, 161, 79), red = C(206, 17, 38);
                HBands(px, blue, White, green);
                HBand(px, 168, 173, red);
                HBand(px, 83, 88, red);
                Crescent(px, 55, 220, 22, 10, blue, White);
                int[] rowY = { 242, 222, 202 };
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++)
                        StarN(px, 100 + c * 34, rowY[r], 6, 5, 0f, White);
            });

            // Vietnam: red field, large gold star centered.
            Add(l, "Vietnam", DesignTab.Nations, px =>
            {
                FillRegion(px, C(218, 37, 29));
                DrawStar(px, 128, 128, 60, C(255, 205, 0));
            });

            // Yemen: red/white/black horizontal triband.
            Add(l, "Yemen", DesignTab.Nations, px =>
                HTriband(px, C(206, 17, 38), White, Black));
        }
    }
}
