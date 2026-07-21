using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch6(List<Design> l)
        {
            // Montenegro: crimson field with a gold border and a gold double-headed eagle + crown (approx).
            Add(l, "Montenegro", DesignTab.Nations, px =>
            {
                Color32 gold = C(196, 156, 62), red = C(200, 30, 40);
                FillRegion(px, gold);
                Rect(px, 12, 12, W - 13, RegionH - 13, red);           // crimson field inside gold border
                int cx = 128;
                Diamond(px, cx, 118, 16, 26, gold);                    // eagle body
                Tri(px, cx, 138, 66, 166, 114, 116, gold);             // left wing
                Tri(px, cx, 138, 190, 166, 142, 116, gold);            // right wing
                Disc(px, 110, 168, 9, gold); Tri(px, 106, 170, 86, 176, 106, 160, gold);   // left head+beak
                Disc(px, 146, 168, 9, gold); Tri(px, 150, 170, 170, 176, 150, 160, gold);  // right head+beak
                Tri(px, 116, 96, 140, 96, 128, 70, gold);              // tail
                Rect(px, 116, 182, 140, 190, gold);                    // crown band
                Tri(px, 118, 190, 124, 202, 130, 190, gold);
                Tri(px, 126, 190, 132, 204, 138, 190, gold);
            });

            // North Macedonia: red field with a golden sun (broadening rays) in the centre.
            Add(l, "North Macedonia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(210, 16, 52));
                Sun(px, W / 2, RegionH / 2, 26, 120, 8, 22.5f, C(255, 209, 0));
            });

            // Norway: red field, white-bordered blue Nordic cross (offset to hoist).
            Add(l, "Norway", DesignTab.Nations, px =>
            {
                NordicCross(px, C(186, 12, 47), White, 100, 128, 44);
                Color32 blue = C(0, 32, 91);
                VBand(px, 89, 111, blue);
                HBand(px, 117, 139, blue);
            });

            // Romania: blue-yellow-red vertical triband.
            Add(l, "Romania", DesignTab.Nations, px =>
                VTriband(px, C(0, 43, 127), C(252, 209, 22), C(206, 17, 38)));

            // San Marino: white (top) over light blue (bottom) with a central crest (approx towers).
            Add(l, "San Marino", DesignTab.Nations, px =>
            {
                FillRegion(px, C(93, 188, 224));                       // light blue bottom
                HBand(px, RegionH / 2, RegionH - 1, White);            // white top
                Color32 gold = C(214, 178, 70), grn = C(70, 150, 90), stone = C(225, 225, 225);
                Rect(px, 98, 96, 158, 150, gold);                      // shield border
                Rect(px, 104, 100, 152, 148, C(150, 205, 235));        // shield field
                // three towers (Mount Titano)
                Rect(px, 108, 108, 122, 132, stone);
                Rect(px, 121, 116, 135, 140, stone);
                Rect(px, 134, 108, 148, 132, stone);
                Disc(px, 115, 138, 4, grn); Disc(px, 128, 146, 4, grn); Disc(px, 141, 138, 4, grn);
                // crown above
                Rect(px, 110, 152, 146, 160, gold);
                Tri(px, 112, 160, 118, 170, 124, 160, gold);
                Tri(px, 124, 160, 128, 172, 132, 160, gold);
                Tri(px, 132, 160, 138, 170, 144, 160, gold);
            });

            // Serbia: red-blue-white horizontal triband, double-headed eagle crest left of centre (approx).
            Add(l, "Serbia", DesignTab.Nations, px =>
            {
                HTriband(px, C(198, 54, 60), C(12, 64, 118), White);
                int cx = 110, cy = RegionH / 2;
                Color32 shR = C(198, 54, 60), wht = White, gold = C(230, 190, 70);
                Diamond(px, cx, cy - 4, 14, 22, wht);                  // eagle body
                Tri(px, cx, cy + 12, cx - 30, cy + 26, cx - 6, cy - 4, wht);   // left wing
                Tri(px, cx, cy + 12, cx + 30, cy + 26, cx + 6, cy - 4, wht);   // right wing
                Disc(px, cx - 12, cy + 30, 6, wht); Disc(px, cx + 12, cy + 30, 6, wht);  // two heads
                Rect(px, cx - 12, cy - 26, cx + 12, cy - 6, shR);      // shield on breast
                Rect(px, cx - 1, cy - 26, cx + 1, cy - 6, wht);        // white cross vertical (bounded to shield)
                Rect(px, cx - 12, cy - 18, cx + 12, cy - 14, wht);     // white cross horizontal (bounded to shield)
                Rect(px, cx - 16, cy + 34, cx + 16, cy + 40, gold);    // crown
            });

            // Slovakia: white-blue-red triband, red shield with white double cross on three blue hills.
            Add(l, "Slovakia", DesignTab.Nations, px =>
            {
                HTriband(px, White, C(11, 78, 162), C(238, 28, 37));
                int cx = 108, cy = RegionH / 2;
                Color32 shR = C(238, 28, 37), blue = C(11, 78, 162);
                Rect(px, cx - 30, cy - 34, cx + 30, cy + 30, White);   // shield border
                Rect(px, cx - 26, cy - 30, cx + 26, cy + 30, shR);     // red shield
                Tri(px, cx - 26, cy - 30, cx - 8, cy - 10, cx + 4, cy - 30, blue);   // hills
                Tri(px, cx - 4, cy - 30, cx + 8, cy - 10, cx + 26, cy - 30, blue);
                Disc(px, cx, cy - 10, 12, blue);
                Rect(px, cx - 3, cy - 12, cx + 3, cy + 22, White);     // double-cross vertical stem (bounded to shield)
                Rect(px, cx - 16, cy + 6, cx + 16, cy + 12, White);    // upper bar
                Rect(px, cx - 12, cy - 8, cx + 12, cy - 2, White);     // lower bar
            });

            // Slovenia: white-blue-red triband, upper-hoist crest (Triglav + stars + waves).
            Add(l, "Slovenia", DesignTab.Nations, px =>
            {
                HTriband(px, White, C(0, 51, 153), C(237, 41, 57));
                int cx = 78, cy = 170;
                Color32 blue = C(0, 51, 153), gold = C(255, 209, 0);
                Rect(px, cx - 26, cy - 34, cx + 26, cy + 34, White);   // shield border
                Rect(px, cx - 22, cy - 30, cx + 22, cy + 30, blue);    // blue shield
                Tri(px, cx - 20, cy - 8, cx, cy + 22, cx + 20, cy - 8, White);   // Triglav main peak
                Tri(px, cx - 20, cy - 8, cx - 8, cy + 8, cx + 4, cy - 8, White); // side peaks
                Tri(px, cx - 4, cy - 8, cx + 8, cy + 8, cx + 20, cy - 8, White);
                Rect(px, cx - 20, cy - 24, cx + 20, cy - 21, White);   // wavy sea lines (bounded to shield)
                Rect(px, cx - 20, cy - 18, cx + 20, cy - 15, White);
                DrawStar(px, cx, cy + 26, 7, gold);                    // three gold stars
                DrawStar(px, cx - 12, cy + 16, 7, gold);
                DrawStar(px, cx + 12, cy + 16, 7, gold);
            });

            // Vatican City: yellow (hoist) / white (fly) vertical bicolour, papal tiara + crossed keys.
            Add(l, "Vatican City", DesignTab.Nations, px =>
            {
                VBand(px, 0, W / 2 - 1, C(255, 205, 0));
                VBand(px, W / 2, W - 1, White);
                int cx = 3 * W / 4, cy = RegionH / 2;
                Color32 gold = C(212, 175, 55), silver = C(170, 170, 175);
                // crossed keys (thin diagonal quads)
                Tri(px, cx - 34, cy - 34, cx - 30, cy - 38, cx + 30, cy + 34, gold);
                Tri(px, cx - 34, cy - 34, cx + 26, cy + 30, cx + 30, cy + 34, gold);
                Tri(px, cx + 34, cy - 34, cx + 30, cy - 38, cx - 30, cy + 34, silver);
                Tri(px, cx + 34, cy - 34, cx - 26, cy + 30, cx - 30, cy + 34, silver);
                Ring(px, cx - 28, cy - 30, 8, 4, gold);                // key bows (bottom)
                Ring(px, cx + 28, cy - 30, 8, 4, silver);
                // tiara above
                Rect(px, cx - 12, cy + 34, cx + 12, cy + 42, gold);
                Tri(px, cx - 12, cy + 42, cx, cy + 58, cx + 12, cy + 42, gold);
                Disc(px, cx, cy + 58, 4, gold);
            });

            // Scotland: blue field with a white saltire (St Andrew's Cross).
            Add(l, "Scotland", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 90, 160));
                Saltire(px, White, 30);
            });

            // Wales: white (top) over green (bottom) with a red dragon (approx silhouette).
            Add(l, "Wales", DesignTab.Nations, px =>
            {
                HBand(px, 0, RegionH / 2 - 1, C(0, 122, 61));           // green bottom
                HBand(px, RegionH / 2, RegionH - 1, White);            // white top
                Color32 red = C(200, 16, 46);
                int cx = 128, cy = 128;
                Diamond(px, cx, cy, 46, 22, red);                      // body
                Tri(px, cx + 30, cy + 6, cx + 78, cy + 20, cx + 40, cy - 14, red);   // head/neck
                Tri(px, cx + 66, cy + 12, cx + 84, cy + 18, cx + 72, cy + 2, red);   // snout
                Tri(px, cx - 30, cy, cx - 78, cy + 34, cx - 40, cy - 20, red);       // tail
                Tri(px, cx - 4, cy + 18, cx + 6, cy + 52, cx + 16, cy + 18, red);    // wing up
                Rect(px, cx - 30, cy - 40, cx - 22, cy - 18, red);     // front legs
                Rect(px, cx + 10, cy - 40, cx + 18, cy - 18, red);
                Disc(px, cx + 70, cy + 16, 3, White);                  // eye
            });

            // Northern Ireland (Ulster Banner): white field, red cross, central star + red hand + crown.
            Add(l, "Northern Ireland", DesignTab.Nations, px =>
            {
                FillRegion(px, White);
                Color32 red = C(206, 17, 38);
                VBand(px, W / 2 - 10, W / 2 + 10, red);                // St George cross
                HBand(px, RegionH / 2 - 10, RegionH / 2 + 10, red);
                int cx = W / 2, cy = RegionH / 2;
                StarN(px, cx, cy, 40, 6, 0f, White);                   // six-pointed white star
                Disc(px, cx, cy, 22, White);
                Disc(px, cx, cy - 2, 12, red);                         // Red Hand of Ulster (approx)
                Rect(px, cx - 8, cy - 14, cx + 8, cy - 2, red);
                Color32 gold = C(230, 190, 70);
                Rect(px, cx - 12, cy + 22, cx + 12, cy + 28, gold);    // crown above star
                Tri(px, cx - 12, cy + 28, cx - 6, cy + 38, cx, cy + 28, gold);
                Tri(px, cx - 4, cy + 28, cx, cy + 40, cx + 4, cy + 28, gold);
                Tri(px, cx, cy + 28, cx + 6, cy + 38, cx + 12, cy + 28, gold);
            });

            // Catalonia (Senyera): golden field with four red horizontal stripes (9 bands).
            Add(l, "Catalonia", DesignTab.Nations, px =>
            {
                Color32 y = C(252, 209, 22), r = C(218, 17, 42);
                HBands(px, y, r, y, r, y, r, y, r, y);
            });
        }
    }
}
