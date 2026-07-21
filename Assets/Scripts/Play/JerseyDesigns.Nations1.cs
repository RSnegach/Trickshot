using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch1(List<Design> l)
        {
            // Algeria: green (hoist) / white (fly) vertical bicolour, red crescent + star centred.
            Add(l, "Algeria", DesignTab.Nations, px =>
            {
                VBand(px, 0, W / 2 - 1, C(0, 98, 51));
                VBand(px, W / 2, W - 1, White);
                Color32 red = C(210, 16, 52);
                Crescent(px, W / 2, RegionH / 2, 48, 18, White, red);
                StarN(px, W / 2 + 24, RegionH / 2, 18, 5, 0f, red);
            });

            // Angola: red over black, central gear-half + machete + star in yellow.
            Add(l, "Angola", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(204, 9, 47));   // red top
                HBand(px, 0, RegionH / 2 - 1, Black);                 // black bottom
                Color32 y = C(255, 206, 0);
                Ring(px, W / 2, RegionH / 2, 38, 29, y);              // cog wheel
                // machete blade across the gear (thin diagonal)
                Tri(px, W / 2 - 30, RegionH / 2 - 34, W / 2 - 22, RegionH / 2 - 40, W / 2 + 34, RegionH / 2 + 30, y);
                Tri(px, W / 2 - 30, RegionH / 2 - 34, W / 2 + 26, RegionH / 2 + 36, W / 2 + 34, RegionH / 2 + 30, y);
                StarN(px, W / 2, RegionH / 2 + 4, 15, 5, 0f, y);      // star at centre
            });

            // Benin: green vertical hoist band, then yellow (top) over red (bottom) on the fly.
            Add(l, "Benin", DesignTab.Nations, px =>
            {
                int gw = (int)(W * 0.38f);
                VBand(px, 0, gw - 1, C(0, 135, 81));
                Rect(px, gw, RegionH / 2, W - 1, RegionH - 1, C(253, 209, 0));   // yellow top
                Rect(px, gw, 0, W - 1, RegionH / 2 - 1, C(230, 0, 0));           // red bottom
            });

            // Botswana: light blue field with a white-bordered black horizontal stripe.
            Add(l, "Botswana", DesignTab.Nations, px =>
            {
                FillRegion(px, C(117, 190, 220));
                HBand(px, RegionH / 2 - 28, RegionH / 2 + 28, White);
                HBand(px, RegionH / 2 - 18, RegionH / 2 + 18, Black);
            });

            // Burkina Faso: red over green with a central yellow star.
            Add(l, "Burkina Faso", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(239, 43, 45));  // red top
                HBand(px, 0, RegionH / 2 - 1, C(0, 158, 73));         // green bottom
                DrawStar(px, W / 2, RegionH / 2, 34, C(252, 209, 22));
            });

            // Burundi: white saltire; red top+bottom, green hoist+fly; white disc with 3 red stars.
            Add(l, "Burundi", DesignTab.Nations, px =>
            {
                Color32 red = C(206, 17, 38), green = C(0, 155, 72);
                int n = RegionH - 1;
                for (int y = 0; y < RegionH; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool aboveRising = y > x;
                        bool aboveFalling = (x + y) > n;
                        bool top = aboveRising && aboveFalling;
                        bool bottom = !aboveRising && !aboveFalling;
                        px(x, y, (top || bottom) ? red : green);
                    }
                Saltire(px, White, 15);
                Disc(px, W / 2, RegionH / 2, 42, White);
                StarN(px, W / 2, RegionH / 2 + 20, 12, 6, 0f, red);
                StarN(px, W / 2 - 18, RegionH / 2 - 12, 12, 6, 0f, red);
                StarN(px, W / 2 + 18, RegionH / 2 - 12, 12, 6, 0f, red);
            });

            // Cabo Verde: blue field, white-red-white band below centre, ring of 10 yellow stars.
            Add(l, "Cabo Verde", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 56, 147));
                int by = (int)(RegionH * 0.40f);
                HBand(px, by, by + 11, White);
                HBand(px, by + 12, by + 23, C(230, 0, 0));
                HBand(px, by + 24, by + 35, White);
                StarRing(px, (int)(W * 0.375f), by + 18, 40, 8, 10, C(255, 209, 0));
            });

            // Cameroon: green/red/yellow vertical triband, yellow star on the centre band.
            Add(l, "Cameroon", DesignTab.Nations, px =>
            {
                VTriband(px, C(0, 135, 81), C(206, 17, 38), C(252, 209, 22));
                DrawStar(px, W / 2, RegionH / 2, 26, C(252, 209, 22));
            });

            // Central African Republic: 4 horizontal bands + red vertical stripe + hoist star.
            Add(l, "Central African Republic", DesignTab.Nations, px =>
            {
                HBands(px, C(0, 51, 160), White, C(40, 150, 40), C(255, 206, 0));
                VBand(px, W / 2 - 14, W / 2 + 14, C(210, 16, 52));
                DrawStar(px, 34, RegionH - 34, 18, C(255, 206, 0));
            });

            // Chad: blue/yellow/red vertical triband.
            Add(l, "Chad", DesignTab.Nations, px =>
                VTriband(px, C(0, 38, 127), C(254, 205, 0), C(198, 12, 48)));

            // Comoros: 4 horizontal bands, green hoist triangle with white crescent + 4 stars.
            Add(l, "Comoros", DesignTab.Nations, px =>
            {
                HBands(px, C(255, 209, 0), White, C(206, 17, 38), C(0, 56, 168));
                Tri(px, 0, 0, 0, RegionH - 1, (int)(W * 0.52f), RegionH / 2, C(0, 150, 60));
                Crescent(px, 58, RegionH / 2, 42, 14, C(0, 150, 60), White);
                DrawStar(px, 74, RegionH / 2 + 33, 8, White);
                DrawStar(px, 74, RegionH / 2 + 11, 8, White);
                DrawStar(px, 74, RegionH / 2 - 11, 8, White);
                DrawStar(px, 74, RegionH / 2 - 33, 8, White);
            });

            // Congo (Republic): green upper-left, red lower-right, yellow rising diagonal band.
            Add(l, "Congo (Republic)", DesignTab.Nations, px =>
            {
                DiagHalf(px, C(220, 30, 40), C(0, 155, 72), true);
                DiagStripe(px, C(255, 213, 0), 22, true);
            });

            // Congo (DR): sky blue, yellow-bordered red diagonal stripe (rising), yellow hoist star.
            Add(l, "Congo (DR)", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 122, 201));
                DiagStripe(px, C(247, 209, 22), 27, true);
                DiagStripe(px, C(206, 17, 38), 14, true);
                DrawStar(px, 42, RegionH - 42, 22, C(247, 209, 22));
            });

            // Cote d'Ivoire: orange/white/green vertical triband.
            Add(l, "Cote d'Ivoire", DesignTab.Nations, px =>
                VTriband(px, C(255, 130, 0), White, C(0, 158, 96)));

            // Djibouti: light blue over green, white hoist triangle with a red star.
            Add(l, "Djibouti", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(108, 190, 230));  // light blue top
                HBand(px, 0, RegionH / 2 - 1, C(18, 138, 92));          // green bottom
                Tri(px, 0, 0, 0, RegionH - 1, (int)(W * 0.55f), RegionH / 2, White);
                DrawStar(px, 46, RegionH / 2, 22, C(210, 16, 52));
            });

            // Egypt: red/white/black triband, golden Eagle of Saladin approximated in the centre.
            Add(l, "Egypt", DesignTab.Nations, px =>
            {
                HTriband(px, C(206, 17, 38), White, Black);
                Color32 gold = C(197, 164, 73);
                int cx = W / 2, cy = RegionH / 2;
                Disc(px, cx, cy, 12, gold);                                   // body
                Tri(px, cx, cy + 4, cx - 26, cy + 16, cx - 4, cy - 4, gold);  // left wing
                Tri(px, cx, cy + 4, cx + 26, cy + 16, cx + 4, cy - 4, gold);  // right wing
                Disc(px, cx, cy + 16, 5, gold);                              // head
                Rect(px, cx - 7, cy - 20, cx + 7, cy - 8, gold);             // shield/tail
            });

            // Equatorial Guinea: green/white/red triband, blue hoist triangle, central tree.
            Add(l, "Equatorial Guinea", DesignTab.Nations, px =>
            {
                HTriband(px, C(0, 133, 62), White, C(224, 60, 49));
                Tri(px, 0, 0, 0, RegionH - 1, (int)(W * 0.28f), RegionH / 2, C(0, 115, 206));
                Color32 trunk = C(120, 80, 40), leaf = C(90, 140, 70);
                Rect(px, W / 2 - 3, RegionH / 2 - 14, W / 2 + 3, RegionH / 2 + 2, trunk);
                Disc(px, W / 2, RegionH / 2 + 8, 13, leaf);
            });

            // Eritrea: red horizontal triangle (hoist base -> fly apex); green top, blue bottom; gold wreath.
            Add(l, "Eritrea", DesignTab.Nations, px =>
            {
                HBand(px, RegionH / 2, RegionH - 1, C(62, 142, 60));   // green top
                HBand(px, 0, RegionH / 2 - 1, C(65, 155, 220));        // blue bottom
                Tri(px, 0, RegionH - 1, 0, 0, W - 1, RegionH / 2, C(224, 16, 45));
                Color32 gold = C(247, 209, 22);
                Ring(px, 62, RegionH / 2, 22, 17, gold);
                Rect(px, 60, RegionH / 2 - 22, 64, RegionH / 2 + 22, gold);
            });

            // Eswatini: blue/yellow/crimson/yellow/blue bands with a central shield emblem.
            Add(l, "Eswatini", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 51, 153));
                int cy = RegionH / 2;
                HBand(px, cy - 52, cy - 45, C(255, 216, 0));
                HBand(px, cy + 45, cy + 52, C(255, 216, 0));
                HBand(px, cy - 44, cy + 44, C(178, 34, 52));
                Rect(px, 40, cy - 2, W - 40, cy + 2, Black);           // staff
                Diamond(px, W / 2, cy, 42, 20, Black);                 // Nguni shield
                Diamond(px, W / 2, cy, 31, 13, White);
                Diamond(px, W / 2, cy, 12, 13, Black);
            });

            // Ethiopia: green/yellow/red triband, blue disc with a yellow pentagram.
            Add(l, "Ethiopia", DesignTab.Nations, px =>
            {
                HTriband(px, C(7, 141, 62), C(252, 221, 9), C(218, 18, 26));
                Disc(px, W / 2, RegionH / 2, 44, C(0, 135, 189));
                StarN(px, W / 2, RegionH / 2, 30, 5, 0f, C(252, 221, 9));
            });

            // Gabon: green/yellow/blue horizontal triband.
            Add(l, "Gabon", DesignTab.Nations, px =>
                HTriband(px, C(0, 158, 96), C(252, 209, 22), C(0, 107, 179)));

            // Gambia: red / white / blue / white / green horizontal bands (6:1:4:1:6).
            Add(l, "Gambia", DesignTab.Nations, px =>
            {
                int u = 14;
                HBand(px, 0, 6 * u - 1, C(60, 158, 73));               // green bottom
                HBand(px, 6 * u, 7 * u - 1, White);
                HBand(px, 7 * u, 11 * u - 1, C(0, 60, 165));           // blue centre
                HBand(px, 11 * u, 12 * u - 1, White);
                HBand(px, 12 * u, RegionH - 1, C(206, 17, 38));        // red top
            });

            // Ghana: red/yellow/green triband with a central black star.
            Add(l, "Ghana", DesignTab.Nations, px =>
            {
                HTriband(px, C(206, 17, 38), C(252, 209, 22), C(0, 107, 63));
                DrawStar(px, W / 2, RegionH / 2, 30, Black);
            });

            // Guinea: red/yellow/green vertical triband.
            Add(l, "Guinea", DesignTab.Nations, px =>
                VTriband(px, C(206, 17, 38), C(252, 209, 22), C(0, 158, 96)));
        }
    }
}
