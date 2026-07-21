using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch7(List<Design> l)
        {
            // Antigua and Barbuda: red field, central downward "V" holding black/blue/white
            // bands (widest at top) with a gold rising sun on the black.
            Add(l, "Antigua and Barbuda", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), blue = C(0, 114, 206), gold = C(255, 206, 0);
                FillRegion(px, red);
                HBand(px, 60, 95, White);      // white (near apex, bottom)
                HBand(px, 96, 140, blue);      // blue (middle)
                HBand(px, 141, 255, Black);    // black (wide top)
                // carve red side triangles to form the downward-pointing triangle
                Tri(px, 0, 255, 128, 60, 0, 60, red);
                Tri(px, 255, 255, 128, 60, 255, 60, red);
                Sun(px, 128, 205, 15, 13, 9, 0f, gold);
            });

            // Bahamas: aqua / gold / aqua horizontal bands, black hoist triangle pointing right.
            Add(l, "Bahamas", DesignTab.Nations, px =>
            {
                HBands(px, C(0, 173, 198), C(255, 199, 44), C(0, 173, 198));
                Tri(px, 0, 0, 0, RegionH - 1, 112, RegionH / 2, Black);
            });

            // Barbados: ultramarine / gold / ultramarine vertical triband, black broken trident.
            Add(l, "Barbados", DesignTab.Nations, px =>
            {
                VTriband(px, C(0, 38, 127), C(255, 199, 0), C(0, 38, 127));
                int cx = W / 2;
                Rect(px, cx - 3, 70, cx + 3, 168, Black);        // shaft
                Rect(px, cx - 42, 168, cx + 42, 178, Black);     // crossbar
                Rect(px, cx - 42, 178, cx - 34, 210, Black);     // left prong
                Rect(px, cx - 4, 178, cx + 4, 214, Black);       // centre prong
                Rect(px, cx + 34, 178, cx + 42, 210, Black);     // right prong
                Tri(px, cx - 42, 210, cx - 34, 210, cx - 38, 224, Black);  // barbs
                Tri(px, cx - 4, 214, cx + 4, 214, cx, 230, Black);
                Tri(px, cx + 34, 210, cx + 42, 210, cx + 38, 224, Black);
            });

            // Belize: royal blue field, thin red top+bottom stripes, white coat-of-arms disc.
            Add(l, "Belize", DesignTab.Nations, px =>
            {
                Color32 blue = C(29, 42, 110), red = C(206, 17, 38), green = C(0, 122, 51);
                FillRegion(px, blue);
                HBand(px, 0, 18, red);
                HBand(px, 237, 255, red);
                Disc(px, W / 2, RegionH / 2, 80, White);
                Ring(px, W / 2, RegionH / 2, 80, 70, green);      // garland
                // central mahogany tree
                Color32 trunk = C(120, 80, 40), leaf = C(40, 120, 60);
                Rect(px, W / 2 - 4, RegionH / 2 - 24, W / 2 + 4, RegionH / 2 + 6, trunk);
                Disc(px, W / 2, RegionH / 2 + 16, 18, leaf);
                Disc(px, W / 2 - 18, RegionH / 2 + 6, 11, leaf);
                Disc(px, W / 2 + 18, RegionH / 2 + 6, 11, leaf);
                // two supporter silhouettes
                Disc(px, W / 2 - 30, RegionH / 2 - 14, 7, C(150, 105, 70));
                Disc(px, W / 2 + 30, RegionH / 2 - 14, 7, C(90, 60, 40));
            });

            // Bolivia: red / yellow / green horizontal triband, coat of arms centred on yellow.
            Add(l, "Bolivia", DesignTab.Nations, px =>
            {
                HTriband(px, C(215, 0, 21), C(247, 205, 0), C(0, 122, 51));
                int cx = W / 2, cy = RegionH / 2;
                Disc(px, cx, cy, 24, C(150, 95, 50));               // gold-brown cartouche
                Disc(px, cx, cy - 2, 18, C(120, 175, 215));         // sky field
                Tri(px, cx - 16, cy - 12, cx + 16, cy - 12, cx, cy + 10, C(120, 110, 120)); // mountain
                Disc(px, cx, cy + 14, 6, C(60, 60, 60));            // condor
                Tri(px, cx, cy + 14, cx - 12, cy + 22, cx - 2, cy + 12, C(60, 60, 60));
                Tri(px, cx, cy + 14, cx + 12, cy + 22, cx + 2, cy + 12, C(60, 60, 60));
                Ring(px, cx, cy, 30, 27, C(0, 100, 40));            // laurel wreath
            });

            // Canada: red hoist+fly bands, white centre, red maple leaf.
            Add(l, "Canada", DesignTab.Nations, px =>
            {
                Color32 red = C(216, 30, 42);
                FillRegion(px, White);
                VBand(px, 0, 63, red);
                VBand(px, 192, 255, red);
                int cx = W / 2;
                Rect(px, cx - 4, 74, cx + 4, 110, red);                          // stem
                Diamond(px, cx, 140, 34, 42, red);                               // body
                Tri(px, cx, 212, cx - 24, 150, cx + 24, 150, red);               // top point
                Tri(px, cx - 84, 150, cx - 20, 132, cx - 20, 168, red);          // left point
                Tri(px, cx + 84, 150, cx + 20, 132, cx + 20, 168, red);          // right point
                Tri(px, cx - 40, 102, cx - 18, 140, cx - 4, 118, red);           // lower-left point
                Tri(px, cx + 40, 102, cx + 18, 140, cx + 4, 118, red);           // lower-right point
            });

            // Chile: white over red, blue hoist square with white star.
            Add(l, "Chile", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, White);        // top white
                HBand(px, 0, RegionH / 2 - 1, C(213, 43, 30));     // bottom red
                Rect(px, 0, RegionH / 2, 127, RegionH - 1, C(0, 57, 166));  // canton
                DrawStar(px, 64, RegionH / 2 + 64, 30, White);
            });

            // Costa Rica: blue/white/red/white/blue horizontal bands (1:1:2:1:1).
            Add(l, "Costa Rica", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 35, 149), red = C(206, 17, 38);
                HBand(px, 0, 42, blue);
                HBand(px, 43, 85, White);
                HBand(px, 86, 170, red);
                HBand(px, 171, 213, White);
                HBand(px, 214, 255, blue);
            });

            // Cuba: 3 blue + 2 white stripes, red hoist triangle with white star.
            Add(l, "Cuba", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 42, 143), red = C(204, 42, 49);
                HBand(px, 0, 50, blue);
                HBand(px, 51, 101, White);
                HBand(px, 102, 152, blue);
                HBand(px, 153, 203, White);
                HBand(px, 204, 255, blue);
                Tri(px, 0, 0, 0, RegionH - 1, 120, RegionH / 2, red);
                DrawStar(px, 40, RegionH / 2, 26, White);
            });

            // Dominica: green field, yellow/black/white cross, red disc, ten green stars, parrot.
            Add(l, "Dominica", DesignTab.Nations, px =>
            {
                Color32 green = C(0, 106, 78), yellow = C(255, 206, 0), red = C(214, 17, 38);
                FillRegion(px, green);
                // vertical triple stripe (left->right: yellow, black, white)
                VBand(px, 108, 121, yellow); VBand(px, 122, 134, Black); VBand(px, 135, 148, White);
                // horizontal triple stripe (top->bottom: yellow, black, white); y up so yellow high
                HBand(px, 135, 148, yellow); HBand(px, 122, 134, Black); HBand(px, 108, 121, White);
                Disc(px, W / 2, RegionH / 2, 44, red);
                StarRing(px, W / 2, RegionH / 2, 40, 8, 10, green);
                // Sisserou parrot approximation
                Disc(px, W / 2, RegionH / 2 - 4, 12, C(90, 60, 140));   // purple body
                Disc(px, W / 2 - 2, RegionH / 2 + 10, 7, C(40, 120, 70)); // green head
                Rect(px, W / 2 - 2, RegionH / 2 - 22, W / 2 + 2, RegionH / 2 - 6, C(90, 60, 140)); // tail
            });

            // Dominican Republic: white cross; blue/red quarters counter-charged; centre arms.
            Add(l, "Dominican Republic", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 45, 152), red = C(206, 17, 38);
                Rect(px, 0, RegionH / 2, W / 2 - 1, RegionH - 1, blue);       // top-left blue
                Rect(px, W / 2, RegionH / 2, W - 1, RegionH - 1, red);        // top-right red
                Rect(px, 0, 0, W / 2 - 1, RegionH / 2 - 1, red);              // bottom-left red
                Rect(px, W / 2, 0, W - 1, RegionH / 2 - 1, blue);            // bottom-right blue
                PlusCross(px, W / 2, RegionH / 2, 14, White);
                Rect(px, W / 2 - 18, RegionH / 2 - 18, W / 2 + 18, RegionH / 2 + 18, White); // arms box
                Rect(px, W / 2 - 10, RegionH / 2 - 12, W / 2 + 10, RegionH / 2 + 12, C(0, 45, 152));
                Tri(px, W / 2 - 8, RegionH / 2 - 12, W / 2 + 8, RegionH / 2 - 12, W / 2, RegionH / 2 + 8, C(206, 17, 38)); // shield hint
            });

            // Ecuador: yellow (double) / blue / red horizontal bands, coat of arms centred.
            Add(l, "Ecuador", DesignTab.Nations, px =>
            {
                HBand(px, 0, 63, C(237, 28, 36));       // red bottom
                HBand(px, 64, 127, C(3, 78, 162));      // blue middle
                HBand(px, 128, 255, C(255, 221, 0));    // yellow top (double)
                int cx = W / 2, cy = RegionH / 2;
                Rect(px, cx - 14, cy - 14, cx + 14, cy + 14, C(210, 220, 235)); // shield
                Tri(px, cx - 14, cy - 14, cx + 14, cy - 14, cx, cy + 16, C(120, 120, 135)); // mountain
                Disc(px, cx, cy + 18, 6, C(70, 70, 70));  // condor body
                Tri(px, cx, cy + 18, cx - 16, cy + 26, cx - 2, cy + 16, C(70, 70, 70)); // wings
                Tri(px, cx, cy + 18, cx + 16, cy + 26, cx + 2, cy + 16, C(70, 70, 70));
            });

            // El Salvador: blue / white / blue horizontal triband, triangular emblem centred.
            Add(l, "El Salvador", DesignTab.Nations, px =>
            {
                HTriband(px, C(0, 71, 171), White, C(0, 71, 171));
                int cx = W / 2, cy = RegionH / 2;
                Tri(px, cx - 34, cy - 26, cx + 34, cy - 26, cx, cy + 34, C(0, 71, 171)); // triangle outline
                Tri(px, cx - 27, cy - 22, cx + 27, cy - 22, cx, cy + 26, White);         // inner
                // five small volcanoes
                Tri(px, cx - 20, cy - 14, cx - 6, cy - 14, cx - 13, cy + 2, C(0, 122, 60));
                Tri(px, cx - 8, cy - 14, cx + 8, cy - 14, cx, cy + 6, C(0, 122, 60));
                Tri(px, cx + 6, cy - 14, cx + 20, cy - 14, cx + 13, cy + 2, C(0, 122, 60));
                Ring(px, cx, cy + 4, 20, 17, C(240, 200, 60));   // rainbow arc hint
                Tri(px, cx - 4, cy + 12, cx + 4, cy + 12, cx, cy + 22, C(230, 170, 40)); // liberty cap
            });

            // Grenada: X-split interior (yellow top/bottom, green sides), red border w/ 6 stars,
            // central red disc + star, nutmeg on the hoist side.
            Add(l, "Grenada", DesignTab.Nations, px =>
            {
                Color32 yellow = C(253, 209, 0), green = C(0, 122, 51), red = C(206, 17, 38);
                int n = RegionH - 1;
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool aboveRising = y > x;
                        bool aboveFalling = (x + y) > n;
                        bool top = aboveRising && aboveFalling;
                        bool bottom = !aboveRising && !aboveFalling;
                        px(x, y, (top || bottom) ? yellow : green);
                    }
                // red border frame
                HBand(px, 0, 29, red); HBand(px, 226, 255, red);
                VBand(px, 0, 29, red); VBand(px, 226, 255, red);
                for (int i = 0; i < 3; i++)
                {
                    int sx = 64 + i * 64;
                    DrawStar(px, sx, 241, 11, yellow);   // top border
                    DrawStar(px, sx, 14, 11, yellow);    // bottom border
                }
                Disc(px, W / 2, RegionH / 2, 40, red);
                DrawStar(px, W / 2, RegionH / 2, 20, yellow);
                // nutmeg on left green triangle
                Disc(px, 62, RegionH / 2, 12, C(200, 40, 40));
                Disc(px, 62, RegionH / 2 + 4, 8, C(230, 180, 60));
            });

            // Guatemala: sky-blue / white / sky-blue vertical triband, coat of arms centred.
            Add(l, "Guatemala", DesignTab.Nations, px =>
            {
                VTriband(px, C(73, 151, 208), White, C(73, 151, 208));
                Color32 green = C(0, 122, 51), brown = C(120, 80, 40), scroll = C(240, 240, 235);
                int cx = W / 2, cy = RegionH / 2;
                Ring(px, cx, cy, 52, 44, green);                    // laurel wreath
                Rect(px, cx - 40, cy - 8, cx + 40, cy + 8, scroll); // scroll
                // crossed rifles + swords
                for (int t = -34; t <= 34; t++)
                {
                    Disc(px, cx + t, cy + t, 2, brown);
                    Disc(px, cx + t, cy - t, 2, C(160, 160, 160));
                }
                Disc(px, cx, cy + 20, 6, green);                    // quetzal body
                Rect(px, cx - 2, cy + 20, cx + 2, cy + 40, green);  // quetzal tail
            });

            // Guyana: green field, white-fimbriated gold arrowhead to the fly,
            // red-fimbriated black arrowhead from the hoist.
            Add(l, "Guyana", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 158, 73));
                Tri(px, 0, 0, 0, RegionH - 1, W - 1, RegionH / 2, White);
                Tri(px, 0, 9, 0, RegionH - 10, 238, RegionH / 2, C(255, 209, 0));
                Tri(px, 0, 0, 0, RegionH - 1, 150, RegionH / 2, C(206, 17, 38));
                Tri(px, 0, 9, 0, RegionH - 10, 132, RegionH / 2, Black);
            });

            // Haiti: navy over red, central white square with coat of arms (palm, flags, cannons).
            Add(l, "Haiti", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(0, 32, 145));  // blue top
                HBand(px, 0, RegionH / 2 - 1, C(210, 16, 52));       // red bottom
                int cx = W / 2, cy = RegionH / 2;
                Rect(px, cx - 40, cy - 40, cx + 40, cy + 40, White);
                Color32 green = C(0, 120, 60), trunk = C(110, 75, 40), dark = C(60, 60, 60);
                Rect(px, cx - 30, cy - 34, cx + 30, cy - 26, green);   // green mound
                Rect(px, cx - 3, cy - 30, cx + 3, cy + 6, trunk);      // palm trunk
                Disc(px, cx, cy + 12, 14, green);                      // palm fronds
                Disc(px, cx - 14, cy + 6, 8, green);
                Disc(px, cx + 14, cy + 6, 8, green);
                // flanking flags
                Tri(px, cx - 30, cy - 4, cx - 30, cy + 20, cx - 12, cy + 8, C(0, 60, 160));
                Tri(px, cx + 30, cy - 4, cx + 30, cy + 20, cx + 12, cy + 8, C(0, 60, 160));
                Rect(px, cx - 24, cy - 30, cx - 20, cy - 4, dark);     // cannon barrels
                Rect(px, cx + 20, cy - 30, cx + 24, cy - 4, dark);
            });

            // Honduras: blue / white / blue horizontal triband, five blue stars (quincunx).
            Add(l, "Honduras", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 56, 147);
                HTriband(px, blue, White, blue);
                int cx = W / 2, cy = RegionH / 2;
                DrawStar(px, cx, cy, 12, blue);
                DrawStar(px, cx - 22, cy + 22, 11, blue);
                DrawStar(px, cx + 22, cy + 22, 11, blue);
                DrawStar(px, cx - 22, cy - 22, 11, blue);
                DrawStar(px, cx + 22, cy - 22, 11, blue);
            });

            // Nicaragua: blue / white / blue horizontal triband, triangular emblem centred.
            Add(l, "Nicaragua", DesignTab.Nations, px =>
            {
                HTriband(px, C(0, 103, 177), White, C(0, 103, 177));
                int cx = W / 2, cy = RegionH / 2;
                Tri(px, cx - 30, cy - 24, cx + 30, cy - 24, cx, cy + 30, C(0, 103, 177)); // border
                Tri(px, cx - 24, cy - 20, cx + 24, cy - 20, cx, cy + 22, White);          // inner
                // five volcanoes on the base
                Tri(px, cx - 18, cy - 12, cx - 6, cy - 12, cx - 12, cy, C(0, 110, 60));
                Tri(px, cx - 8, cy - 12, cx + 8, cy - 12, cx, cy + 4, C(0, 110, 60));
                Tri(px, cx + 6, cy - 12, cx + 18, cy - 12, cx + 12, cy, C(0, 110, 60));
                Ring(px, cx, cy + 2, 16, 13, C(240, 200, 60));       // rainbow arc
                Tri(px, cx - 4, cy + 8, cx + 4, cy + 8, cx, cy + 18, C(220, 60, 60)); // liberty cap
            });

            // Panama: quartered white/red/blue/white with a blue star and a red star.
            Add(l, "Panama", DesignTab.Nations, px =>
            {
                Color32 blue = C(7, 32, 101), red = C(218, 37, 29);
                Rect(px, 0, RegionH / 2, W / 2 - 1, RegionH - 1, White);   // top-left white
                Rect(px, W / 2, RegionH / 2, W - 1, RegionH - 1, red);     // top-right red
                Rect(px, 0, 0, W / 2 - 1, RegionH / 2 - 1, blue);          // bottom-left blue
                Rect(px, W / 2, 0, W - 1, RegionH / 2 - 1, White);        // bottom-right white
                DrawStar(px, W / 4, RegionH / 2 + RegionH / 4, 30, blue);
                DrawStar(px, W / 2 + W / 4, RegionH / 4, 30, red);
            });

            // Paraguay: red / white / blue horizontal triband, central star-in-wreath emblem.
            Add(l, "Paraguay", DesignTab.Nations, px =>
            {
                HTriband(px, C(213, 43, 30), White, C(0, 56, 151));
                int cx = W / 2, cy = RegionH / 2;
                Ring(px, cx, cy, 30, 28, C(213, 43, 30));   // outer red ring
                Ring(px, cx, cy, 26, 20, C(0, 122, 51));    // green wreath
                DrawStar(px, cx, cy, 14, C(255, 206, 0));   // gold star
            });

            // Puerto Rico: 3 red + 2 white stripes, blue hoist triangle with white star.
            Add(l, "Puerto Rico", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), blue = C(0, 45, 150);
                HBand(px, 0, 50, red);
                HBand(px, 51, 101, White);
                HBand(px, 102, 152, red);
                HBand(px, 153, 203, White);
                HBand(px, 204, 255, red);
                Tri(px, 0, 0, 0, RegionH - 1, 128, RegionH / 2, blue);
                DrawStar(px, 44, RegionH / 2, 28, White);
            });
        }
    }
}
