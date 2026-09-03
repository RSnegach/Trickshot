using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The paged grid of live-scene mode panels shared by the Single Player list and the
    /// Multiplayer hub: title at the top of the screen, up to 6 big panels in the middle, Back at
    /// the bottom, and arrows plus A/D or Left/Right when there is more than one page.
    ///
    /// It owns the MenuSceneStage for the modes it shows and tells it which panel the mouse is
    /// over. Hover is latched on the REPAINT pass only: OnGUI runs several passes per frame and a
    /// live scene must start and reset once, not two or three times.
    ///
    /// A plain class, not a MonoBehaviour - the owning screen draws it from its own OnGUI inside
    /// the MenuScale block.
    /// </summary>
    public class ModeGrid
    {
        public const int Cols = 3, Rows = 2, PerPage = Cols * Rows;

        readonly List<GameMode> _modes = new List<GameMode>();
        readonly string _title;
        MenuSceneStage _stage;
        int _page;
        GameMode? _hover;

        /// <summary>The page currently shown, so a screen rebuilt after a Back returns to it.</summary>
        public int Page { get => _page; set => _page = value; }

        public ModeGrid(string title, IList<GameMode> modes, Transform owner, int page = 0)
        {
            _title = title;
            _modes.AddRange(modes);
            _page = page;

            var go = new GameObject("MenuSceneStage");
            go.transform.SetParent(owner, false);
            _stage = go.AddComponent<MenuSceneStage>();
            _stage.Setup(_modes);
        }

        public int Pages => Mathf.Max(1, (_modes.Count + PerPage - 1) / PerPage);

        /// <summary>
        /// Draw the grid. Returns the picked mode, or null. `back` is set when Back was pressed.
        /// The caller invokes its callbacks AFTER MenuScale.End(), since they destroy the screen.
        /// </summary>
        public GameMode? Draw(out bool back)
        {
            back = false;
            GameMode? picked = null;

            int pages = Pages;
            _page = Mathf.Clamp(_page, 0, pages - 1);

            var e = Event.current;
            if (pages > 1 && e.type == EventType.KeyDown)
            {
                if ((e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.A) && _page > 0) { _page--; e.Use(); }
                else if ((e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.D) && _page < pages - 1) { _page++; e.Use(); }
            }

            float W = MenuScale.Width, H = MenuScale.Height;
            UITheme.Scrim(W, H, 0.26f, 720f, 0.30f, 0f);

            // Title at the TOP of the screen, Back at the BOTTOM, panels filling what is between.
            const float titleTop = 26f, titleH = 56f, backH = 48f, backGap = 14f;
            UITheme.Title(new Rect(0, titleTop, W, titleH), _title, 40, showRule: false);

            float marginX = W * 0.05f;
            float gridTop = titleTop + titleH + 22f;
            float backY = H - 26f - backH;
            float gridBottom = backY - backGap - (pages > 1 ? 22f : 0f);
            float gridH = Mathf.Max(120f, gridBottom - gridTop);

            int first = _page * PerPage;
            int count = Mathf.Min(PerPage, _modes.Count - first);
            int cols = Mathf.Min(Cols, Mathf.Max(1, count));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)cols));

            // Arrows live outside the grid, so the panels lose that width when there is paging.
            float arrowBand = pages > 1 ? 64f : 0f;
            // Real gutters: Panel draws a drop shadow ~20 px past its rect and the hover glow
            // reaches further still, so tight rows visibly bleed into each other.
            float gapX = W * 0.026f, gapY = H * 0.055f;
            float availW = W - marginX * 2f - arrowBand * 2f;
            float panelW = (availW - gapX * (cols - 1)) / cols;
            float panelH = (gridH - gapY * (rows - 1)) / rows;
            // A scene panel wants to be roughly square-ish, not a letterbox: the figure is TALL
            // and the camera's fov is vertical, so a short panel crops the body rather than
            // showing more of it. Trade width away rather than height.
            panelW = Mathf.Min(panelW, panelH * 1.35f);
            float usedH = panelH * rows + gapY * (rows - 1);
            float gy = gridTop + Mathf.Max(0f, (gridH - usedH) * 0.5f);

            GameMode? hoverNow = null;
            for (int i = 0; i < count; i++)
            {
                int row = i / cols, col = i % cols;
                int inRow = Mathf.Min(cols, count - row * cols);
                float rowW = panelW * inRow + gapX * (inRow - 1);
                float rx = W * 0.5f - rowW * 0.5f + col * (panelW + gapX);
                var r = new Rect(rx, gy + row * (panelH + gapY), panelW, panelH);

                var mode = _modes[first + i];
                bool hot = r.Contains(e.mousePosition);
                if (hot) hoverNow = mode;

                var inner = UITheme.SceneRect(r);
                var px = MenuScale.ToScreen(inner);
                var tex = _stage.Texture(mode, Mathf.RoundToInt(px.width), Mathf.RoundToInt(px.height));
                if (UITheme.ScenePanel(r, PauseMenu.ModeName(mode), tex, hot)) picked = mode;
            }

            // Latch hover ONCE a frame. Every pass carries a valid mousePosition, so doing this on
            // all of them would start and reset a scene several times per frame.
            if (e.type == EventType.Repaint && hoverNow != _hover)
            {
                _hover = hoverNow;
                _stage.SetHover(_hover);
            }

            if (pages > 1)
            {
                float ah = 74f, ay = gy + usedH * 0.5f - ah * 0.5f;
                var arrow = new GUIStyle(GUI.skin.button) { fontSize = 26, fontStyle = FontStyle.Bold };
                GUI.enabled = _page > 0;
                if (UITheme.Button(new Rect(marginX, ay, 46f, ah), "<", arrow)) _page--;
                GUI.enabled = _page < pages - 1;
                if (UITheme.Button(new Rect(W - marginX - 46f, ay, 46f, ah), ">", arrow)) _page++;
                GUI.enabled = true;
                UITheme.Hint(new Rect(0, gy + usedH + 4f, W, 18f),
                             (_page + 1) + " / " + pages + "   -   A / D or arrow keys");
            }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(W * 0.5f - 160f, backY, 320f, backH), "Back", btn)) back = true;

            return picked;
        }

        /// <summary>Tear the stage down. Called by the owning screen when it goes away; Destroy is
        /// deferred to end of frame, so stop simulating now rather than leaving a scene live.</summary>
        public void Teardown()
        {
            if (_stage == null) return;
            _stage.Teardown();   // synchronous: releases the globals before the next screen builds
            _stage = null;
        }
    }
}
