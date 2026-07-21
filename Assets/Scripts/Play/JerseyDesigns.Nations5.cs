using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch5(List<Design> l)
        {
            // Albania: red field with a black double-headed eagle (approximated silhouette).
            Add(l, "Albania", DesignTab.Nations, px =>
            {
                Color32 red = C(228, 30, 32), blk = Black;
                FillRegion(px, red);
                int cx = 128;
                Diamond(px, cx, 120, 15, 24, blk);                     // body
                Tri(px, cx, 138, 70, 168, 116, 118, blk);              // left wing
                Tri(px, cx, 138, 186, 168, 140, 118, blk);             // right wing
                Disc(px, 112, 168, 8, blk);                            // left head
                Tri(px, 108, 170, 90, 176, 108, 160, blk);             // left beak
                Disc(px, 144, 168, 8, blk);                            // right head
                Tri(px, 148, 170, 166, 176, 148, 160, blk);            // right beak
                Tri(px, 118, 100, 138, 100, 128, 76, blk);             // tail
            });

            // Andorra: blue-yellow-red vertical triband with a central coat of arms (approx shield).
            Add(l, "Andorra", DesignTab.Nations, px =>
            {
                VTriband(px, C(16, 6, 159), C(254, 223, 0), C(213, 0, 50));
                Color32 shRed = C(200, 20, 40), shYel = C(240, 200, 30);
                Rect(px, 110, 126, 127, 150, shRed);   // TL
                Rect(px, 128, 126, 146, 150, shYel);   // TR
                Rect(px, 110, 100, 127, 125, shYel);   // BL
                Rect(px, 128, 100, 146, 125, shRed);   // BR
            });

            // Belarus: red (top 2/3) over green (bottom 1/3) with red-on-white hoist ornament.
            Add(l, "Belarus", DesignTab.Nations, px =>
            {
                Color32 red = C(210, 39, 48), grn = C(0, 151, 57);
                HBand(px, 0, RegionH / 3 - 1, grn);
                HBand(px, RegionH / 3, RegionH - 1, red);
                VBand(px, 0, 27, White);                               // hoist band
                for (int y = 20; y < RegionH; y += 44)                 // red ornament diamonds
                {
                    Diamond(px, 14, y, 9, 14, red);
                    Rect(px, 12, y - 2, 16, y + 2, White);
                }
            });

            // Bosnia and Herzegovina: blue field, yellow triangle, diagonal row of white stars.
            Add(l, "Bosnia and Herzegovina", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 20, 137));
                Tri(px, 60, 255, 255, 255, 255, 0, C(255, 205, 0));    // yellow triangle
                int[,] s = { { 103, 217 }, { 127, 186 }, { 150, 156 }, { 173, 125 },
                             { 197, 94 }, { 220, 64 }, { 244, 33 } };
                for (int i = 0; i < s.GetLength(0); i++)
                    StarN(px, s[i, 0], s[i, 1], 9, 5, 0f, White);
            });

            // Bulgaria: white-green-red horizontal triband.
            Add(l, "Bulgaria", DesignTab.Nations, px =>
                HBands(px, White, C(0, 150, 110), C(214, 38, 18)));

            // Croatia: red-white-blue triband with central red/white checker shield.
            Add(l, "Croatia", DesignTab.Nations, px =>
            {
                HBands(px, C(217, 16, 35), White, C(1, 33, 105));
                Color32 chkR = C(217, 16, 35);
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 5; gx++)
                    {
                        Color32 c = ((gx + gy) % 2 == 0) ? chkR : White;
                        int x0 = 100 + gx * 11, y0 = 98 + gy * 11;
                        Rect(px, x0, y0, x0 + 10, y0 + 10, c);
                    }
            });

            // Czechia: white (top) over red (bottom) with a blue hoist triangle.
            Add(l, "Czechia", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 2 - 1, C(215, 20, 26));         // red bottom
                HBand(px, RegionH / 2, RegionH - 1, White);           // white top
                Tri(px, 0, 0, 0, 255, 128, 128, C(17, 69, 126));      // blue triangle
            });

            // Estonia: blue-black-white horizontal triband.
            Add(l, "Estonia", DesignTab.Nations, px =>
                HBands(px, C(0, 114, 206), Black, White));

            // Hungary: red-white-green horizontal triband.
            Add(l, "Hungary", DesignTab.Nations, px =>
                HBands(px, C(205, 42, 62), White, C(67, 111, 77)));

            // Iceland: blue field with white-bordered red Nordic cross.
            Add(l, "Iceland", DesignTab.Nations, px =>
            {
                NordicCross(px, C(2, 82, 156), White, 100, 128, 40);
                Color32 red = C(220, 30, 53);
                VBand(px, 90, 110, red);
                HBand(px, 118, 138, red);
            });

            // Kosovo: blue field, gold map silhouette, six white stars in an arc above.
            Add(l, "Kosovo", DesignTab.Nations, px =>
            {
                FillRegion(px, C(36, 74, 165));
                Color32 gold = C(208, 166, 80);
                Diamond(px, 128, 118, 42, 34, gold);                   // map (approx blob)
                Disc(px, 106, 128, 14, gold);
                Disc(px, 152, 108, 12, gold);
                int[,] st = { { 56, 196 }, { 84, 206 }, { 114, 211 },
                              { 142, 211 }, { 172, 206 }, { 200, 196 } };
                for (int i = 0; i < st.GetLength(0); i++)
                    StarN(px, st[i, 0], st[i, 1], 10, 5, 0f, White);
            });

            // Latvia: carmine field with a central white stripe (2:1:2).
            Add(l, "Latvia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(158, 48, 57));
                HBand(px, 102, 153, White);
            });

            // Liechtenstein: blue (top) over red (bottom) with a gold crown in the canton.
            Add(l, "Liechtenstein", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 2 - 1, C(206, 17, 38));         // red bottom
                HBand(px, RegionH / 2, RegionH - 1, C(0, 43, 127));   // blue top
                Color32 gold = C(255, 216, 60);
                Rect(px, 52, 178, 92, 194, gold);                     // crown band
                Tri(px, 52, 194, 60, 210, 68, 194, gold);
                Tri(px, 64, 194, 72, 214, 80, 194, gold);
                Tri(px, 76, 194, 84, 210, 92, 194, gold);
                Disc(px, 60, 210, 4, gold);
                Disc(px, 72, 214, 4, gold);
                Disc(px, 84, 210, 4, gold);
            });

            // Lithuania: yellow-green-red horizontal triband.
            Add(l, "Lithuania", DesignTab.Nations, px =>
                HBands(px, C(253, 185, 19), C(0, 106, 68), C(193, 39, 45)));

            // Luxembourg: red-white-lightblue horizontal triband.
            Add(l, "Luxembourg", DesignTab.Nations, px =>
                HBands(px, C(237, 41, 57), White, C(0, 161, 222)));

            // Malta: white (hoist) and red (fly) with a George Cross emblem in the canton.
            Add(l, "Malta", DesignTab.Nations, px =>
            {
                Color32 red = C(207, 20, 43);
                VBand(px, 0, W / 2 - 1, White);
                VBand(px, W / 2, W - 1, red);
                Rect(px, 40, 172, 84, 216, C(160, 160, 160));         // grey border
                Rect(px, 44, 176, 80, 212, White);                    // white field
                Rect(px, 58, 176, 66, 212, red);                      // cross vertical
                Rect(px, 44, 190, 80, 198, red);                      // cross horizontal
            });

            // Moldova: blue-yellow-red vertical triband with a central eagle emblem (approx).
            Add(l, "Moldova", DesignTab.Nations, px =>
            {
                VTriband(px, C(0, 70, 174), C(255, 213, 0), C(204, 9, 47));
                Color32 eag = C(150, 110, 30);
                Tri(px, 128, 132, 100, 150, 120, 118, eag);           // left wing
                Tri(px, 128, 132, 156, 150, 136, 118, eag);           // right wing
                Disc(px, 128, 128, 9, eag);                           // body
                Disc(px, 128, 140, 6, eag);                           // head
                Rect(px, 122, 116, 134, 127, C(0, 70, 174));          // shield upper (blue)
                Rect(px, 122, 106, 134, 115, C(204, 9, 47));          // shield lower (red)
            });

            // Monaco: red (top) over white (bottom).
            Add(l, "Monaco", DesignTab.Nations, px =>
            {
                FillRegion(px, White);
                HBand(px, RegionH / 2, RegionH - 1, C(206, 17, 38));
            });
        }
    }
}
