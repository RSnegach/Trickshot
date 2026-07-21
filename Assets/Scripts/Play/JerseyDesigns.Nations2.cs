using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch2(List<Design> l)
        {
            // Guinea-Bissau: red hoist band w/ black star, yellow (top) + green (bottom) fly
            Add(l, "Guinea-Bissau", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 158, 73));
                Rect(px, 85, 128, 255, 255, C(252, 209, 22));
                VBand(px, 0, 84, C(206, 17, 38));
                DrawStar(px, 42, 128, 46, C(0, 0, 0));
            });

            // Kenya: black/red/green w/ white fimbriations, Maasai shield + spears (approx)
            Add(l, "Kenya", DesignTab.Nations, px =>
            {
                HBand(px, 171, 255, C(0, 0, 0));
                HBand(px, 86, 170, C(187, 0, 0));
                HBand(px, 0, 85, C(0, 110, 51));
                HBand(px, 167, 171, C(255, 255, 255));
                HBand(px, 86, 90, C(255, 255, 255));
                // crossed spears (white shafts)
                Rect(px, 60, 124, 196, 132, C(240, 240, 240));
                Diamond(px, 128, 128, 30, 54, C(255, 255, 255));
                Diamond(px, 128, 128, 22, 46, C(187, 0, 0));
                Rect(px, 122, 82, 134, 174, C(0, 0, 0));
            });

            // Lesotho: blue/white/green w/ black mokorotlo hat (approx)
            Add(l, "Lesotho", DesignTab.Nations, px =>
            {
                HBand(px, 171, 255, C(0, 32, 91));
                HBand(px, 86, 170, C(255, 255, 255));
                HBand(px, 0, 85, C(0, 144, 54));
                Tri(px, 128, 162, 100, 118, 156, 118, C(20, 20, 20));
                Rect(px, 92, 110, 164, 118, C(20, 20, 20));
            });

            // Liberia: 11 red/white stripes, blue canton w/ white star
            Add(l, "Liberia", DesignTab.Nations, px =>
            {
                for (int i = 0; i < 11; i++)
                {
                    int y0 = i * 256 / 11, y1 = (i + 1) * 256 / 11 - 1;
                    HBand(px, y0, y1, (i % 2 == 0) ? C(191, 10, 48) : C(255, 255, 255));
                }
                Rect(px, 0, 162, 100, 255, C(0, 32, 91));
                DrawStar(px, 50, 208, 34, C(255, 255, 255));
            });

            // Libya: red/black(wide)/green w/ white crescent + star
            Add(l, "Libya", DesignTab.Nations, px =>
            {
                Rect(px, 0, 192, 255, 255, C(213, 43, 30));
                Rect(px, 0, 64, 255, 191, C(0, 0, 0));
                Rect(px, 0, 0, 255, 63, C(35, 158, 88));
                Crescent(px, 122, 128, 36, 12, C(0, 0, 0), C(255, 255, 255));
                DrawStar(px, 156, 128, 16, C(255, 255, 255));
            });

            // Madagascar: white hoist band, red (top) + green (bottom) fly
            Add(l, "Madagascar", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 124, 66));
                Rect(px, 85, 128, 255, 255, C(252, 54, 54));
                VBand(px, 0, 84, C(255, 255, 255));
            });

            // Malawi: black/red/green w/ red rising sun on black
            Add(l, "Malawi", DesignTab.Nations, px =>
            {
                HBand(px, 171, 255, C(0, 0, 0));
                HBand(px, 86, 170, C(206, 17, 38));
                HBand(px, 0, 85, C(0, 158, 73));
                Sun(px, 128, 203, 18, 18, 31, 0f, C(206, 17, 38));
            });

            // Mali: vertical green/yellow/red
            Add(l, "Mali", DesignTab.Nations, px =>
            {
                VTriband(px, C(20, 158, 79), C(252, 209, 22), C(206, 17, 38));
            });

            // Mauritania: green w/ red top+bottom bands, yellow crescent (up) + star
            Add(l, "Mauritania", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 127, 74));
                Rect(px, 0, 216, 255, 255, C(207, 20, 43));
                Rect(px, 0, 0, 255, 39, C(207, 20, 43));
                Disc(px, 128, 118, 46, C(255, 205, 0));
                Disc(px, 128, 140, 46, C(0, 127, 74));
                DrawStar(px, 128, 132, 18, C(255, 205, 0));
            });

            // Mauritius: red/blue/yellow/green horizontal bands
            Add(l, "Mauritius", DesignTab.Nations, px =>
            {
                HBands(px, C(234, 32, 39), C(26, 32, 106), C(255, 209, 0), C(0, 106, 78));
            });

            // Morocco: red field w/ green pentagram (approx as filled star)
            Add(l, "Morocco", DesignTab.Nations, px =>
            {
                FillRegion(px, C(193, 39, 45));
                StarN(px, 128, 128, 62, 5, 0f, C(0, 98, 51));
            });

            // Mozambique: green/black/yellow w/ white fimbriations, red hoist triangle + emblem (approx)
            Add(l, "Mozambique", DesignTab.Nations, px =>
            {
                Rect(px, 0, 171, 255, 255, C(0, 122, 51));
                Rect(px, 0, 90, 255, 165, C(0, 0, 0));
                Rect(px, 0, 0, 255, 84, C(255, 209, 0));
                Rect(px, 0, 165, 255, 171, C(255, 255, 255));
                Rect(px, 0, 84, 255, 90, C(255, 255, 255));
                Tri(px, 0, 0, 0, 255, 128, 128, C(215, 10, 20));
                DrawStar(px, 44, 128, 26, C(255, 209, 0));
            });

            // Namibia: blue (UL) + green (LR) triangles, red rising band w/ white edges, gold sun
            Add(l, "Namibia", DesignTab.Nations, px =>
            {
                FillRegion(px, C(0, 48, 143));
                Tri(px, 0, 0, 255, 0, 255, 255, C(0, 145, 84));
                DiagStripe(px, C(255, 255, 255), 62, true);
                DiagStripe(px, C(210, 16, 52), 34, true);
                Sun(px, 74, 186, 16, 14, 12, 0f, C(255, 205, 0));
            });

            // Niger: orange/white/green w/ orange disc
            Add(l, "Niger", DesignTab.Nations, px =>
            {
                HTriband(px, C(224, 82, 6), C(255, 255, 255), C(15, 120, 58));
                Disc(px, 128, 128, 30, C(224, 82, 6));
            });

            // Rwanda: blue (top half)/yellow/green w/ gold sun top-fly
            Add(l, "Rwanda", DesignTab.Nations, px =>
            {
                Rect(px, 0, 128, 255, 255, C(0, 161, 228));
                Rect(px, 0, 64, 255, 127, C(250, 209, 45));
                Rect(px, 0, 0, 255, 63, C(32, 161, 71));
                Sun(px, 202, 190, 15, 13, 24, 0f, C(230, 182, 0));
            });
        }
    }
}
