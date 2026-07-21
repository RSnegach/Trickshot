using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch9(List<Design> l)
        {
            // European Union: reflex-blue field, ring of 12 gold stars.
            Add(l, "European Union", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 51, 153));
                StarRing(px, W / 2, RegionH / 2, 76, 15, 12, C(255, 204, 0));
            });

            // Antarctica: light blue field, white continent approximated as a blobby cluster.
            Add(l, "Antarctica", DesignTab.Nations, px =>
            {
                FillRegion(px, C(62, 116, 170));
                Disc(px, 128, 122, 50, White);
                Disc(px, 96, 132, 28, White);
                Disc(px, 162, 112, 32, White);
                Disc(px, 126, 152, 24, White);
                Disc(px, 108, 100, 20, White);
                Disc(px, 152, 148, 18, White);
            });

            // Olympic: white field, five interlocking rings.
            Add(l, "Olympic", DesignTab.Nations, px =>
            {
                FillRegion(px, White);
                Color32 obl = C(0, 129, 200), obk = Black, ord = C(238, 51, 78),
                        oyl = C(252, 177, 49), ogr = C(0, 166, 81);
                int ty = 150, by = 122, rr = 27, ri = 20;
                Ring(px, 72, ty, rr, ri, obl);
                Ring(px, 128, ty, rr, ri, obk);
                Ring(px, 184, ty, rr, ri, ord);
                Ring(px, 100, by, rr, ri, oyl);
                Ring(px, 156, by, rr, ri, ogr);
            });

            // Soviet Union: red field, gold hammer-and-sickle + star emblem in the TOP-HOIST
            // corner (matches the classic emblem: star on top, sickle crescent embracing a
            // crossing hammer). Colours match the reference: gold on red.
            Add(l, "Soviet Union", DesignTab.Nations, px =>
            {
                Color32 red = C(204, 0, 0), gold = C(255, 208, 38);
                FillRegion(px, red);
                // Prefer a real PNG (Assets/Resources/flags/soviet_emblem.png, Read/Write on) in
                // the TOP-HOIST corner; fall back to a drawn hammer-sickle-star if it's absent.
                int ex = 74, ey = 176;
                if (!OverlayImage(px, "soviet_emblem", ex, ey, 120, 150))
                {
                    StarN(px, ex, ey + 52, 20, 5, 0f, gold);
                    Disc(px, ex + 6, ey, 34, gold);
                    Disc(px, ex + 16, ey + 4, 27, red);
                    Rect(px, ex - 34, ey - 34, ex + 6, ey + 40, red);
                    Rect(px, ex - 34, ey + 6, ex + 40, ey + 40, red);
                    for (int i = 0; i <= 20; i++) Disc(px, ex - 30 + i, ey - 34 + i, 4, gold);
                    for (int i = 0; i <= 46; i++) Disc(px, ex - 30 + i, ey - 34 + i, 4, gold);
                    Tri(px, ex + 8, ey + 8, ex + 34, ey + 2, ex + 30, ey + 20, gold);
                    Tri(px, ex + 8, ey + 8, ex + 30, ey + 20, ex + 4, ey + 24, gold);
                }
            });

            // Pride Rainbow: 6 stripes red/orange/yellow/green/blue/violet, red on top.
            Add(l, "Pride Rainbow", DesignTab.Nations, px =>
                HBands(px, C(228, 3, 3), C(255, 140, 0), C(255, 237, 0),
                           C(0, 128, 38), C(0, 77, 255), C(117, 7, 135)));

            // Hong Kong: red field, white 5-petal Bauhinia flower.
            Add(l, "Hong Kong", DesignTab.Nations, px =>
            {
                Color32 red = C(222, 41, 16);
                FillRegion(px, red);
                int cx = 128, cy = 128;
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.PI / 2f + i * (Mathf.PI * 2f / 5f);
                    int sx = cx + Mathf.RoundToInt(46f * Mathf.Cos(a));
                    int sy = cy + Mathf.RoundToInt(46f * Mathf.Sin(a));
                    Disc(px, sx, sy, 27, White);
                }
                Disc(px, cx, cy, 20, red);       // recarve red centre
                for (int i = 0; i < 5; i++)      // small red mark near each petal base
                {
                    float a = Mathf.PI / 2f + i * (Mathf.PI * 2f / 5f);
                    int sx = cx + Mathf.RoundToInt(40f * Mathf.Cos(a));
                    int sy = cy + Mathf.RoundToInt(40f * Mathf.Sin(a));
                    Disc(px, sx, sy, 4, red);
                }
            });

            // Greenland: white top half, red bottom half, offset counter-charged disc.
            Add(l, "Greenland", DesignTab.Nations, px =>
            {
                Color32 red = C(209, 26, 42);
                HBand(px, RegionH / 2, RegionH - 1, White);   // top white
                HBand(px, 0, RegionH / 2 - 1, red);           // bottom red
                int dcx = 96, dcy = RegionH / 2, dr = 58;
                int r2 = dr * dr;
                for (int y = dcy - dr; y <= dcy + dr; y++)
                    for (int x = dcx - dr; x <= dcx + dr; x++)
                    {
                        int dx = x - dcx, dy = y - dcy;
                        if (dx * dx + dy * dy <= r2)
                            px(x, y, (y >= dcy) ? red : White);  // top of disc red, bottom white
                    }
            });

            // Faroe Islands: white field, red Nordic cross fimbriated (outlined) in blue.
            Add(l, "Faroe Islands", DesignTab.Nations, px =>
            {
                Color32 blue = C(0, 101, 171), red = C(237, 41, 57);
                FillRegion(px, White);
                int vC = 100, hC = 128;
                VBand(px, vC - 20, vC + 20, blue);   // blue outer
                HBand(px, hC - 20, hC + 20, blue);
                VBand(px, vC - 11, vC + 11, red);    // red inner
                HBand(px, hC - 11, hC + 11, red);
            });

            // Cook Islands: blue field, Union Jack canton, ring of 15 white stars in the fly.
            Add(l, "Cook Islands", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 40, 140));
                UnionJack(px, 0, RegionH - 1 - 128, 127, RegionH - 1);
                StarRing(px, 190, 128, 54, 9, 15, White);
            });

            // Aruba: light blue field, two thin yellow stripes near bottom, red 4-point star.
            Add(l, "Aruba", DesignTab.Nations, px =>
            {
                Color32 yellow = C(249, 208, 35), red = C(224, 52, 63);
                FillRegion(px, C(65, 141, 203));
                HBand(px, 40, 48, yellow);
                HBand(px, 58, 66, yellow);
                StarN(px, 62, 198, 30, 4, 0f, White);   // white fimbriation
                StarN(px, 62, 198, 23, 4, 0f, red);
            });

            // Bermuda: red ensign, Union Jack canton, coat of arms disc in the fly.
            Add(l, "Bermuda", DesignTab.Nations, px =>
            {
                FillRegion(px, C(206, 17, 38));
                UnionJack(px, 0, RegionH - 1 - 128, 127, RegionH - 1);
                int cx = 192, cy = 118;
                Disc(px, cx, cy, 34, White);              // arms roundel
                Disc(px, cx, cy + 16, 12, C(200, 40, 40)); // red lion (approx)
                Rect(px, cx - 15, cy - 26, cx + 15, cy + 2, White);   // shield
                Rect(px, cx - 15, cy - 26, cx + 15, cy - 18, C(0, 120, 80)); // green base
                Tri(px, cx - 10, cy - 10, cx + 10, cy - 10, cx, cy - 20, C(120, 70, 40)); // ship
            });

            // Gibraltar: white top two-thirds, red bottom third, red castle + gold key.
            Add(l, "Gibraltar", DesignTab.Nations, px =>
            {
                Color32 castle = C(218, 37, 29), key = C(255, 206, 0);
                HBand(px, 0, RegionH / 3 - 1, castle);       // bottom third red
                HBand(px, RegionH / 3, RegionH - 1, White);  // top two-thirds white
                int bx = 128, by = 150;
                Rect(px, bx - 40, by, bx + 40, by + 38, castle);       // keep body
                Rect(px, bx - 40, by + 38, bx - 20, by + 60, castle);  // left tower
                Rect(px, bx - 10, by + 42, bx + 10, by + 68, castle);  // centre tower
                Rect(px, bx + 20, by + 38, bx + 40, by + 60, castle);  // right tower
                Rect(px, bx - 8, by, bx + 8, by + 22, White);          // gate
                // gold key hanging below the castle
                Ring(px, bx, by - 20, 9, 4, key);
                Rect(px, bx - 2, by - 20, bx + 2, by - 2, key);        // shaft
                Rect(px, bx + 2, by - 8, bx + 9, by - 4, key);         // teeth
                Rect(px, bx + 2, by - 15, bx + 7, by - 11, key);
            });

            // Jolly Roger: black field, white skull + crossed bones.
            Add(l, "Jolly Roger", DesignTab.Nations, px =>
            {
                FillRegion(px, Black);
                // crossed bones (behind the skull), thick diagonal lines with knob ends
                for (int t = 0; t <= 100; t++)
                {
                    int xa = 72 + (184 - 72) * t / 100;
                    int ya = 60 + (150 - 60) * t / 100;   // rising
                    Disc(px, xa, ya, 6, White);
                    int xb = 72 + (184 - 72) * t / 100;
                    int yb = 150 + (60 - 150) * t / 100;  // falling
                    Disc(px, xb, yb, 6, White);
                }
                Disc(px, 72, 60, 10, White); Disc(px, 184, 150, 10, White);
                Disc(px, 72, 150, 10, White); Disc(px, 184, 60, 10, White);
                // skull
                int scx = 128, scy = 170;
                Disc(px, scx, scy, 40, White);                 // cranium
                Rect(px, scx - 18, scy - 46, scx + 18, scy - 6, White);  // jaw block
                Disc(px, scx - 13, scy - 40, 11, White);       // rounded jaw corners
                Disc(px, scx + 13, scy - 40, 11, White);
                Disc(px, scx - 15, scy + 8, 10, Black);        // eyes
                Disc(px, scx + 15, scy + 8, 10, Black);
                Tri(px, scx, scy - 4, scx - 7, scy - 16, scx + 7, scy - 16, Black); // nose
                Rect(px, scx - 2, scy - 46, scx + 2, scy - 20, Black);  // teeth gap
                Rect(px, scx - 12, scy - 46, scx - 9, scy - 22, Black);
                Rect(px, scx + 9, scy - 46, scx + 12, scy - 22, Black);
            });
        }
    }
}
