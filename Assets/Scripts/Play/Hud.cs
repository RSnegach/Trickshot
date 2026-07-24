using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Shared IMGUI HUD toolkit for all the non-scrimmage modes, so every mode's
    /// scoreboard, clock, callout, and control legend look consistent and polished.
    ///
    /// Draw order per mode's OnGUI:
    ///   Hud.Begin();                                   // once, sets up styles
    ///   var p = Hud.PanelStart(title);                 // top-left titled panel
    ///     Hud.Stat(ref p, "Goals", goals.ToString());  // one stat row per line
    ///   Hud.Clock(seconds);                            // optional big top-centre clock
    ///   Hud.Flash(text, alpha);                        // centre callout (fades)
    ///   Hud.Banner("TIME!", "Goals: 7", "Press R");    // centre end-of-round card
    ///   Hud.Legend(lines);                             // bottom control legend
    ///
    /// Colours: dark translucent panels, white text, gold accents. Purely visual - the
    /// modes own all scoring/logic.
    /// </summary>
    public static class Hud
    {
        public static readonly Color Ink   = Color.white;
        public static readonly Color Dim   = new Color(0.78f, 0.80f, 0.85f);
        public static readonly Color Gold  = new Color(1f, 0.86f, 0.32f);
        public static readonly Color Panel = new Color(0.07f, 0.08f, 0.11f, 0.82f);
        public static readonly Color Accent = new Color(0.16f, 0.55f, 0.95f, 0.9f);

        static GUIStyle _title, _statKey, _statVal, _clock, _flash, _bannerBig, _bannerSub, _legend;
        static Texture2D _px;
        static bool _ready;

        public static void Begin()
        {
            if (_ready) return;
            _ready = true;
            _px = Texture2D.whiteTexture;

            _title    = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Gold } };
            _statKey  = new GUIStyle { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = Dim } };
            _statVal  = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Ink } };
            _clock    = new GUIStyle { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Ink } };
            _flash    = new GUIStyle { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Ink } };
            _bannerBig = new GUIStyle { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            _bannerSub = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } };
            _legend   = new GUIStyle { fontSize = 12, alignment = TextAnchor.MiddleLeft, normal = { textColor = Dim } };
        }

        static void Fill(Rect r, Color c) { var p = GUI.color; GUI.color = c; GUI.DrawTexture(r, _px); GUI.color = p; }

        // ---- top-left titled panel + stat rows ----
        public struct P { public float x, y, w, row; }

        public static P PanelStart(string title, int stats)
        {
            const float w = 232f, pad = 12f, head = 30f, rowH = 22f;
            float h = head + stats * rowH + pad;
            var p = new P { x = 14f, y = 14f, w = w, row = 0f };
            Fill(new Rect(p.x, p.y, w, h), Panel);
            Fill(new Rect(p.x, p.y, w, 3f), Accent);   // accent strip on top
            GUI.Label(new Rect(p.x + pad, p.y + 6f, w - pad * 2f, 22f), title, _title);
            p.row = p.y + head;
            return p;
        }

        public static void Stat(ref P p, string key, string val)
        {
            const float pad = 12f;
            GUI.Label(new Rect(p.x + pad, p.row, p.w - pad * 2f, 20f), key, _statKey);
            GUI.Label(new Rect(p.x + pad, p.row, p.w - pad * 2f, 20f), val, _statVal);
            p.row += 22f;
        }

        // ---- big centred clock along the top ----
        public static void Clock(float seconds, bool urgent = false)
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(seconds));
            var prev = _clock.normal.textColor;
            _clock.normal.textColor = urgent ? new Color(1f, 0.4f, 0.35f) : Ink;
            // Subtle plate behind the clock.
            Fill(new Rect(Screen.width * 0.5f - 70f, 12f, 140f, 58f), Panel);
            GUI.Label(new Rect(0, 16f, Screen.width, 58f), $"{t / 60:0}:{t % 60:00}", _clock);
            _clock.normal.textColor = prev;
        }

        // Callout colours keyed off the text so GOAL / SAVE / MISS etc. read at a glance.
        // GOAL + EPIC = gold, SAVE/BLOCK = blue accent, MISS = warm red, else white.
        static Color FlashTint(string text)
        {
            if (string.IsNullOrEmpty(text)) return Ink;
            string t = text.ToUpperInvariant();
            if (t.Contains("EPIC"))  return new Color(1f, 0.55f, 0.15f);   // fiery orange-gold
            if (t.Contains("GOAL"))  return new Color(1f, 0.84f, 0.28f);   // gold
            if (t.Contains("SAVE"))  return new Color(0.35f, 0.72f, 1f);   // keeper blue
            if (t.Contains("BLOCK")) return new Color(0.55f, 0.80f, 1f);   // lighter blue
            if (t.Contains("MISS"))  return new Color(1f, 0.42f, 0.38f);   // warm red
            return Ink;
        }

        // ---- centre callout that fades with alpha (0..1) ----
        // Big, colour-coded, with a soft backing plate, drop shadow, and a quick pop-in scale as it
        // appears (alpha runs 1 -> 0 over the flash's life, so 1-alpha drives the settle). One
        // renderer for every mode's GOAL / SAVE / BLOCKED / MISS callout.
        public static void Flash(string text, float alpha)
        {
            if (alpha <= 0f || string.IsNullOrEmpty(text)) return;
            alpha = Mathf.Clamp01(alpha);

            // Pop-in: overshoot to ~1.12x at spawn, settle to 1x within the first ~20% of life.
            float appear = Mathf.Clamp01((1f - alpha) / 0.2f);
            float scale = Mathf.Lerp(1.12f, 1f, appear);
            // Ease the fade so it lingers readable then drops off quickly at the end.
            float a = alpha * alpha * (3f - 2f * alpha);   // smoothstep

            _flash.fontSize = Mathf.RoundToInt(72f * scale);
            Color tint = FlashTint(text);

            const float bandTop = 84f, bandH = 96f;
            // Soft dark plate behind the text for legibility over any pitch/crowd.
            Fill(new Rect(0f, bandTop, Screen.width, bandH), new Color(0f, 0f, 0f, 0.34f * a));
            // Thin accent rules top and bottom of the band, in the callout's colour.
            var rule = tint; rule.a = 0.55f * a;
            Fill(new Rect(0f, bandTop, Screen.width, 2f), rule);
            Fill(new Rect(0f, bandTop + bandH - 2f, Screen.width, 2f), rule);

            var rect = new Rect(0, bandTop + 10f, Screen.width, 80f);

            // Drop shadow first (offset, dark), then the coloured text on top.
            var shadow = new Color(0f, 0f, 0f, 0.55f * a);
            _flash.normal.textColor = shadow;
            GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height), text, _flash);

            var c = tint; c.a = a; _flash.normal.textColor = c;
            GUI.Label(rect, text, _flash);

            // Restore defaults for any other caller.
            _flash.normal.textColor = Ink;
            _flash.fontSize = 72;
        }

        // ---- centre end-of-round card ----
        public static void Banner(string big, string sub, string hint)
        {
            float w = 520f, h = 200f;
            float x = Screen.width * 0.5f - w * 0.5f, y = Screen.height * 0.5f - h * 0.5f;
            Fill(new Rect(x, y, w, h), new Color(0.05f, 0.06f, 0.09f, 0.9f));
            Fill(new Rect(x, y, w, 4f), Gold);
            GUI.Label(new Rect(x, y + 32f, w, 54f), big, _bannerBig);
            if (!string.IsNullOrEmpty(sub)) GUI.Label(new Rect(x, y + 96f, w, 30f), sub, _bannerSub);
            if (!string.IsNullOrEmpty(hint))
            {
                var h2 = new GUIStyle(_legend) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Dim } };
                GUI.Label(new Rect(x, y + 150f, w, 24f), hint, h2);
            }
        }

        // ---- bottom-left control legend ----
        public static void Legend(string line)
        {
            float h = 26f;
            Fill(new Rect(0f, Screen.height - h, Screen.width, h), new Color(0f, 0f, 0f, 0.4f));
            GUI.Label(new Rect(12f, Screen.height - h, Screen.width - 24f, h), line, _legend);
        }
    }
}
