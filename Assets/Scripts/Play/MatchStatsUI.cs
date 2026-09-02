using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// The one post-match stats window for Match mode (single-player and networked): two Home/
    /// Away tabs, one player shown at a time with a body preview on the left and their stats on
    /// the right, flipped through with '&#8249;'/'&#8250;' or A/D/Left/Right, and a Man of the Match banner.
    /// Replaces the old MatchGame.DrawStatsBoard/DrawTeamCard two-card table.
    ///
    /// A real human's row (the local player, or any connected networked player) shows their true
    /// appearance and jersey - both are genuinely stored/replicated data. An AI-controlled row has
    /// no stored appearance at all (GameBootstrap/NetMatch build every AI body as a plain generic
    /// ragdoll), so it previews as a plain body in the team's kit color - matching exactly what it
    /// already looks like on the pitch, not a simplification specific to this window.
    ///
    /// One PlayerPreview instance, owned here and reused for every player flipped to (cheap - no
    /// RenderTexture, same small extra viewport render CustomizeUI already pays). Its target is
    /// only rebuilt when the shown (team, shirt) pair actually changes, not every frame.
    ///
    /// Keyboard input is polled from TickInput(), which the owner (MatchGame/NetMatch - both
    /// MonoBehaviours with their own Update()) must call once per frame while this window is up.
    /// IMGUI's OnGUI runs multiple times per rendered frame (Layout, Repaint, once per queued input
    /// event), so polling Keyboard.current directly inside Draw() would fire a single physical key
    /// press 2-3+ times in one frame - PauseMenu avoids exactly this the same way, by polling from
    /// Update() and only reading the resolved state during OnGUI.
    /// </summary>
    public class MatchStatsUI
    {
        enum Tab { Home, Away }
        Tab _tab = Tab.Home;
        int _idxHome, _idxAway;

        PlayerPreview _preview;
        int _shownTeam = -1, _shownShirt = -1;   // last rebuilt preview target - skip redundant rebuilds
        bool _shownIsMe;

        static GUIStyle _bannerSt, _nameSt, _labelSt, _valSt, _motmTagSt;

        /// <summary>Poll A/D and Left/Right once per frame - call from the owner's own Update()
        /// while this window is being shown. Never call from inside Draw()/OnGUI.</summary>
        public void TickInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Step(-1);
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Step(1);
        }

        void Step(int dir)
        {
            if (_tab == Tab.Home) _idxHome += dir; else _idxAway += dir;
        }

        /// <summary>
        /// Draw the window. `stats` is the full roster (Home + Away, AI + human) exactly as
        /// MatchGame.Stats or MatchGame.FromWire(...) already produce it. `myRow` is the local
        /// human's own row from THIS SAME `stats` list this frame (MatchGame.MyRow() for
        /// single-player; a slot==_localSlot scan over `stats` for the networked driver) - passed
        /// in fresh every call rather than cached, since a client's `stats` list is rebuilt from
        /// the wire every frame and holding a stale reference across frames would compare wrong.
        /// Call inside the caller's own Hud.Begin()/End() bracket, same as the old board.
        /// </summary>
        public void Draw(List<MatchGame.PlayerStat> stats, int homeScore, int awayScore,
                         MatchGame.PlayerStat myRow)
        {
            if (stats == null) return;
            _preview ??= BuildPreview();

            EnsureStyles();

            float w = MenuScale.Width * 0.88f, h = MenuScale.Height * 0.85f;
            float x = MenuScale.Width * 0.5f - w * 0.5f, y = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.55f, w + 200f);
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Gold);

            float cy = y + 16f;

            // MOTM banner - only if somebody actually beat a neutral performance (FinalizeRatings'
            // own rule: a nothing-happened match names nobody, and then the line is simply absent).
            MatchGame.PlayerStat motm = null;
            for (int i = 0; i < stats.Count; i++) if (stats[i].motm) { motm = stats[i]; break; }
            if (motm != null)
            {
                var bannerRect = new Rect(x + 24f, cy, w - 48f, 36f);
                UITheme.Glow(bannerRect, UITheme.Gold);
                UITheme.Shadowed(bannerRect,
                    "MAN OF THE MATCH  ·  " + motm.name + "  ·  " + motm.rating.ToString("0.0"),
                    _bannerSt, UITheme.Gold, 0.7f, 2f);
                cy += 46f;
            }

            // Home/Away tabs.
            float tabW = (w - 48f - 8f) * 0.5f;
            if (Hud.Seg(new Rect(x + 24f, cy, tabW, 34f), "HOME  " + homeScore, _tab == Tab.Home)) _tab = Tab.Home;
            if (Hud.Seg(new Rect(x + 24f + tabW + 8f, cy, tabW, 34f), "AWAY  " + awayScore, _tab == Tab.Away)) _tab = Tab.Away;
            cy += 34f + 12f;
            UITheme.Divider(x + 24f, cy, w - 48f);
            cy += 16f;

            int team = _tab == Tab.Home ? 0 : 1;
            var roster = new List<MatchGame.PlayerStat>();
            for (int i = 0; i < stats.Count; i++) if (stats[i].team == team) roster.Add(stats[i]);

            float bodyY = cy;
            float bodyH = (y + h - 58f) - bodyY;

            if (roster.Count > 0)
            {
                int raw = _tab == Tab.Home ? _idxHome : _idxAway;
                int idx = ((raw % roster.Count) + roster.Count) % roster.Count;
                if (_tab == Tab.Home) _idxHome = idx; else _idxAway = idx;
                var shown = roster[idx];

                bool isMe = myRow != null && ReferenceEquals(shown, myRow);
                UpdatePreview(shown, isMe);

                float leftW = w * 0.38f;
                var previewRect = new Rect(x + 24f, bodyY, leftW, bodyH);
                UITheme.Frame(previewRect, UITheme.Gold);
                _preview.ViewportPx = MenuScale.ToScreen(previewRect);
                _preview.Show();   // first render only after this frame's UI has drawn (no entry flash)

                float rightX = previewRect.xMax + 20f;
                float rightW = (x + w - 24f) - rightX;
                DrawPlayerCard(shown, rightX, bodyY, rightW, bodyH);

                // Flip arrows flank the right pane's header - text glyphs, matching CareerStatsUI's
                // own just-shipped precedent (no arrow icon assets exist anywhere in the project).
                var arrowSt = _arrowSt ??= new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
                if (UITheme.Button(new Rect(rightX, bodyY, 40f, 36f), "‹", arrowSt)) Step(-1);
                if (UITheme.Button(new Rect(rightX + rightW - 40f, bodyY, 40f, 36f), "›", arrowSt)) Step(1);
            }

            Hud.Legend("A/D or ‹›  flip player   Esc menu");
        }

        void DrawPlayerCard(MatchGame.PlayerStat s, float x, float y, float w, float h)
        {
            float cx = x + 56f;   // clear the flip-arrow buttons at the header
            var nameRect = new Rect(cx, y, w - 112f, 36f);
            UITheme.Shadowed(nameRect, s.name, _nameSt, UITheme.Ink, 0.7f, 2f);
            if (s.motm)
            {
                var tagSt = _motmTagSt;
                UITheme.Label(new Rect(cx, y + 34f, w - 112f, 20f), "MAN OF THE MATCH", tagSt);
            }

            float ry = y + 66f;
            const float rowH = 34f, gap = 8f;
            DrawStatRow(ref ry, x, w, rowH, gap, "Rating", s.rating.ToString("0.0"),
                s.rating >= 7.5f ? UITheme.Green : s.rating <= 6.2f ? UITheme.Red : UITheme.Ink);
            DrawStatRow(ref ry, x, w, rowH, gap, "Goals", s.goals.ToString());
            DrawStatRow(ref ry, x, w, rowH, gap, "Assists", s.assists.ToString());
            // Same dash rules DrawTeamCard always used: a keeper's shots/tackles are not a keeper
            // stat, and TKL is unreachable for a networked human (net-host skips the local tackle
            // block entirely), so a number there would only ever be an AI's.
            DrawStatRow(ref ry, x, w, rowH, gap, "Shots", s.keeper ? "-" : s.shots.ToString());
            DrawStatRow(ref ry, x, w, rowH, gap, "Passes", s.passesDone + " / " + s.passes);
            DrawStatRow(ref ry, x, w, rowH, gap, "Tackles", s.keeper || s.netControlled ? "-" : s.tackles.ToString());
            DrawStatRow(ref ry, x, w, rowH, gap, "Saves", s.keeper ? s.saves.ToString() : "-");
        }

        static GUIStyle _arrowSt, _valTintSt;
        void DrawStatRow(ref float ry, float x, float w, float rowH, float gap, string label, string value, Color? valColor = null)
        {
            UITheme.Label(new Rect(x, ry, w * 0.55f, rowH), label, _labelSt);
            var vs = _valSt;
            // One reusable tinted copy, recoloured per call, instead of a fresh GUIStyle per row per
            // OnGUI pass (this board draws over a still-running networked match).
            if (valColor.HasValue) { vs = _valTintSt ??= new GUIStyle(_valSt); vs.normal.textColor = valColor.Value; }
            UITheme.Label(new Rect(x + w * 0.55f, ry, w * 0.45f, rowH), value, vs);
            UITheme.Divider(x, ry + rowH, w);
            ry += rowH + gap;
        }

        // ---- preview target ----

        PlayerPreview BuildPreview()
        {
            var go = new GameObject("MatchStatsPreview");
            var p = go.AddComponent<PlayerPreview>();
            p.AutoRotate = true;
            p.Setup();
            return p;
        }

        void UpdatePreview(MatchGame.PlayerStat s, bool isMe)
        {
            if (s.team == _shownTeam && s.shirt == _shownShirt && isMe == _shownIsMe) return;
            _shownTeam = s.team; _shownShirt = s.shirt; _shownIsMe = isMe;

            if (isMe)
            {
                // The local human's real customized appearance - height/weight/jersey included.
                _preview.Rebuild();
                return;
            }

            Color kitTint = s.team == 0 ? new Color(0.24f, 0.42f, 0.78f) : new Color(0.72f, 0.22f, 0.20f);
            var session = Trickshot.Net.Multiplayer.Session;
            if (s.slot != 255 && session != null)
            {
                // A connected (or formerly connected) networked human: their real replicated
                // cosmetics and their real painted jersey, both genuinely stored per slot.
                var appearance = session.RosterSlot(s.slot).appearance;
                var jersey = session.JerseyForSlot(s.slot);
                _preview.RebuildOther(appearance, jersey, kitTint);
            }
            else
            {
                // AI: no stored appearance at all (GameBootstrap/NetMatch build every AI body as a
                // plain generic ragdoll) - a plain body in the team's kit color, matching exactly
                // what it already looks like on the pitch.
                _preview.RebuildOther(null, null, kitTint);
            }
        }

        /// <summary>Torn down when the owning MatchGame/NetMatch is destroyed.</summary>
        public void Teardown()
        {
            if (_preview != null) _preview.Teardown();
            _preview = null;
        }

        static void EnsureStyles()
        {
            if (_bannerSt != null) return;
            _bannerSt = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _nameSt = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _motmTagSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Gold } };
            _labelSt = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            _valSt = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Ink } };
        }
    }
}
