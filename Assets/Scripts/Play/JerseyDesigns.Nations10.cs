using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    public static partial class JerseyDesigns
    {
        static void BuildNationsBatch10(List<Design> l)
        {
            Add(l, "Sao Tome and Principe", DesignTab.Nations, px => {
                Color32 g = C(18,135,71), y = C(255,206,0), r = C(210,22,44), blk = C(0,0,0);
                FillRegion(px, y);
                HBand(px, 179, 255, g);   // top green
                HBand(px, 0, 76, g);      // bottom green
                Tri(px, 0,0, 0,255, 96,128, r); // hoist red triangle
                DrawStar(px, 150,128, 14, blk);
                DrawStar(px, 200,128, 14, blk);
            });

            Add(l, "Senegal", DesignTab.Nations, px => {
                Color32 g = C(0,133,63), y = C(253,209,0), r = C(224,0,38);
                VBands(px, g, y, r);
                DrawStar(px, 128,128, 34, g);
            });

            Add(l, "Seychelles", DesignTab.Nations, px => {
                // five oblique bands radiating from bottom-left corner
                Color32 blue = C(0,52,120), y = C(252,209,22), r = C(215,25,32),
                        w = C(255,255,255), g = C(0,122,51);
                for (int yy = 0; yy < RegionH; yy++)
                    for (int xx = 0; xx < W; xx++)
                    {
                        float a = Mathf.Atan2(yy, xx) * Mathf.Rad2Deg; // 0 (right) .. 90 (up)
                        Color32 c = a < 18 ? g : a < 36 ? w : a < 54 ? r : a < 72 ? y : blue;
                        px(xx, yy, c);
                    }
            });

            Add(l, "Sierra Leone", DesignTab.Nations, px => {
                HBands(px, C(30,145,70), C(255,255,255), C(0,114,206)); // green/white/blue
            });

            Add(l, "Somalia", DesignTab.Nations, px => {
                FillRegion(px, C(65,141,222));
                DrawStar(px, 128,128, 48, C(255,255,255));
            });

            Add(l, "South Africa", DesignTab.Nations, px => {
                // approximate pall (Y): red top, blue bottom, green pall w/ white + gold, black hoist tri
                Color32 red = C(224,60,49), blue = C(0,20,137), grn = C(0,122,77),
                        gold = C(255,182,18), blk = C(0,0,0), w = C(255,255,255);
                Rect(px, 0,128,255,255, red);
                Rect(px, 0,0,255,127, blue);
                // white pall (fimbriation) arms + centre bar
                Tri(px, 0,255, 0,185, 150,128, w);
                Tri(px, 0,0,   0,70,  150,128, w);
                Rect(px, 95,105,255,150, w);
                // green pall inside
                Tri(px, 0,243, 0,197, 132,128, grn);
                Tri(px, 0,12,  0,58,  132,128, grn);
                Rect(px, 108,112,255,143, grn);
                // hoist gold then black triangle
                Tri(px, 0,0,0,255, 96,128, gold);
                Tri(px, 0,14,0,241, 80,128, blk);
            });

            Add(l, "South Sudan", DesignTab.Nations, px => {
                Color32 blk = C(0,0,0), r = C(218,37,29), g = C(0,122,77),
                        w = C(255,255,255), blue = C(0,71,140), y = C(252,193,0);
                HBand(px, 172,255, blk);
                HBand(px, 88,167, r);
                HBand(px, 0,83, g);
                HBand(px, 168,171, w); // fimbriation
                HBand(px, 84,87, w);
                Tri(px, 0,0,0,255, 104,128, blue); // hoist triangle
                DrawStar(px, 40,128, 20, y);
            });

            Add(l, "Sudan", DesignTab.Nations, px => {
                HBands(px, C(215,20,26), C(255,255,255), C(0,0,0)); // red/white/black
                Tri(px, 0,0,0,255, 96,128, C(0,122,61)); // green hoist triangle
            });

            Add(l, "Tanzania", DesignTab.Nations, px => {
                // green upper-left, blue lower-right, black diagonal band w/ yellow fimbriation
                Color32 g = C(30,151,79), blue = C(0,163,224), blk = C(0,0,0), y = C(252,209,22);
                for (int yy = 0; yy < RegionH; yy++)
                    for (int xx = 0; xx < W; xx++)
                    {
                        int d = yy - xx, ad = d < 0 ? -d : d;
                        Color32 c = ad <= 18 ? blk : ad <= 30 ? y : d > 0 ? g : blue;
                        px(xx, yy, c);
                    }
            });

            Add(l, "Togo", DesignTab.Nations, px => {
                Color32 g = C(0,109,58), y = C(255,206,0), r = C(210,22,44), w = C(255,255,255);
                HBands(px, g, y, g, y, g);
                Rect(px, 0,103,152,255, r); // red canton top hoist
                DrawStar(px, 76,179, 40, w);
            });

            Add(l, "Tunisia", DesignTab.Nations, px => {
                Color32 r = C(206,17,38), w = C(255,255,255);
                FillRegion(px, r);
                Disc(px, 128,128, 72, w);
                Crescent(px, 128,128, 46, 18, w, r); // red crescent opens right
                DrawStar(px, 146,128, 20, r);
            });

            Add(l, "Uganda", DesignTab.Nations, px => {
                Color32 blk = C(0,0,0), y = C(252,209,22), r = C(215,25,32),
                        w = C(255,255,255), gy = C(120,120,120);
                HBands(px, blk, y, r, blk, y, r); // six bands
                Disc(px, 128,128, 34, w);
                // grey crowned crane approx
                Rect(px, 110,120,128,126, gy);  // back
                Disc(px, 128,124, 10, gy);      // body
                Rect(px, 126,124,133,150, gy);  // neck
                Disc(px, 130,150, 5, gy);       // head
                DrawStar(px, 134,152, 4, r);    // red crest
            });

            Add(l, "Zambia", DesignTab.Nations, px => {
                Color32 g = C(31,138,76), r = C(222,32,44), blk = C(0,0,0), o = C(239,125,0);
                FillRegion(px, g);
                Rect(px, 198,0,215,150, r);   // fly stripes red/black/orange
                Rect(px, 216,0,233,150, blk);
                Rect(px, 234,0,251,150, o);
                // orange eagle approx above stripes
                Disc(px, 225,185, 12, o);
                Tri(px, 205,185, 245,185, 225,205, o);
            });

            Add(l, "Zimbabwe", DesignTab.Nations, px => {
                Color32 g = C(0,122,61), y = C(255,213,0), r = C(239,42,53), blk = C(0,0,0), w = C(255,255,255);
                HBands(px, g, y, r, blk, r, y, g); // seven bands
                Tri(px, 0,0,0,255, 100,128, blk); // black-edged triangle
                Tri(px, 0,10,0,245, 86,128, w);   // white triangle
                DrawStar(px, 42,128, 30, r);      // red star
                // yellow Zimbabwe bird approx
                Disc(px, 42,128, 9, y);
                Tri(px, 34,120, 50,120, 42,145, y);
            });
        }
    }
}
