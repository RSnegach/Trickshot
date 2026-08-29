using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Species picker: the first step of the customize flow, in both the single-player path
    /// (Stadium -> SPECIES -> Customize) and the lobby path (Lobby -> SPECIES -> Customize).
    ///
    /// Species gets its own screen rather than a sub-menu inside Customize because it changes that
    /// screen's shape: which appearance tabs exist, what the sliders measure and in what range,
    /// whether adult mode is offered, and whether there is an Instinct skill tab. Choosing it first
    /// means CustomizeUI reads the species once on Init and never re-derives its tab set mid-screen.
    ///
    /// Geometry deliberately matches CustomizeUI: a preview column on the left and a 560px panel on
    /// the right, 600px tall, so the model does not jump across the screen on Next. Both read the
    /// column width from PlayerPreview rather than hardcoding it, because it now widens with the
    /// display so a horse or an elephant fits side-on.
    ///
    /// Picking a row goes through Species.ApplySelection, which re-clamps height/weight, the style
    /// indices and the species-gated skill nodes. IMGUI, no Canvas wiring.
    ///
    /// The rows are NAMES ONLY, and there is no spec block under them. Both used to be here: a
    /// one-line descriptor per species and a panel listing body plan, height and weight ranges,
    /// the customize tabs the pick unlocks and whether the model was finished. All of it either
    /// restated what the 3D preview already shows or previewed a screen the player reaches by
    /// pressing Next, and it turned a four-item choice into a page of reading.
    /// </summary>
    public class SpeciesSelectUI : MonoBehaviour
    {
        System.Action _onPicked;
        System.Action _onBack;

        PlayerPreview _preview;
        bool  _draggingModel;
        float _lastDragX;
        byte  _shownSpecies = 255;   // last species the preview was built for; 255 = never

        public void Init(System.Action onPicked, System.Action onBack)
        {
            _onPicked = onPicked;
            _onBack = onBack;
            GameInput.CaptureCursor(false);

            // Re-apply the current selection on entry so a profile that was left in a stale state
            // (a species byte from a save, a height from another species) is coherent before
            // anything reads it.
            Species.ApplySelection(Species.SelectedId);

            var pg = new GameObject("PlayerPreview");
            _preview = pg.AddComponent<PlayerPreview>();
            _preview.Setup();
            _shownSpecies = Species.SelectedId;
        }

        void OnDestroy()
        {
            if (_preview != null) _preview.Teardown();
        }

        void OnGUI()
        {
            // Scale the whole screen up on big displays (see MenuScale). Wrapped so the early
            // returns below can't leak the scaled GUI matrix.
            MenuScale.Begin();
            Draw();
            MenuScale.End();
        }

        void Draw()
        {
            var all = Species.All;
            // The preview column widens with the display (PlayerPreview.ColumnWidth) so a quadruped
            // fits side-on; the panel is fixed. CustomizeUI reads the same source, which is the only
            // reason the model stays put across Next.
            float previewW = PlayerPreview.ColumnWidth;
            const float gap = PlayerPreview.ColumnGap, panelW = PlayerPreview.PanelW, panelH = PlayerPreview.PanelH;
            float totalW = previewW + gap + panelW;
            float ox = MenuScale.Width * 0.5f - totalW * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.38f, totalW + 260f);

            // ---- Live 3D preview (left) ----
            var previewRect = new Rect(ox, y, previewW, panelH);
            UITheme.Frame(previewRect, UITheme.Blue);
            if (_preview != null)
            {
                // Rebuild only on an actual species change: Rebuild() tears down and re-creates a
                // whole ragdoll, so doing it per frame would be wasteful and would fight the drag.
                if (_shownSpecies != Species.SelectedId)
                {
                    _shownSpecies = Species.SelectedId;
                    _preview.Rebuild();
                }
                // The preview camera wants REAL device pixels, but previewRect is in the scaled GUI
                // space - convert, or the model renders in the wrong place on a big screen.
                _preview.ViewportPx = MenuScale.ToScreen(previewRect);
                _preview.AutoRotate = false;   // the player turns the model by dragging it
                HandleModelDrag(previewRect);
            }
            UITheme.Hint(new Rect(previewRect.x, previewRect.yMax - 26f, previewW, 20f), "Drag the model to spin it");

            // ---- Species list (right) ----
            float x = ox + previewW + gap;
            UITheme.Panel(new Rect(x, y, panelW, panelH), UITheme.Gold);

            var title = new GUIStyle(GUI.skin.label)
            { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 28f, y + 14f, panelW - 56f, 36f), "SELECT SPECIES", title, UITheme.Ink, 0.75f, 2f);
            UITheme.Fill(new Rect(x + 28f, y + 46f, 48f, 2.5f), UITheme.Gold);

            var nameSt = new GUIStyle(GUI.skin.button)
            { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 8, 8, 4) };
            float lx = x + 28f, lw = panelW - 56f, rowH = 62f, rowGap = 8f;
            float row = y + 58f;
            for (int i = 0; i < all.Length; i++)
            {
                var def = all[i];
                // Hidden until its rig lands (Species.Selectable). Skipping rather than greying the
                // row keeps the list gap-free; the SpeciesDef entry stays so its wire id still
                // resolves for saves and peers. Species.ApplySelection guards the selection itself.
                if (!Species.Selectable(def.Id)) continue;
                bool selected = def.Id == Species.SelectedId;
                var r = new Rect(lx, row, lw, rowH);
                // Row click SELECTS only (same convention as the stadium picker); Next advances.
                if (UITheme.Toggle(r, "    " + def.Name, selected, nameSt))
                    Species.ApplySelection(def.Id);
                // Gold spine on the leading edge marks the pick (replaces the old text arrow).
                if (selected) UITheme.Fill(new Rect(r.x + 6f, r.y + 7f, 3f, r.height - 14f), UITheme.Gold);
                row += rowH + rowGap;
            }

            // Teaser card for the species whose rigs are still coming (the ModelReady = false
            // entries the loop above skips). Hovers like a row, never clicks: UITheme.Tease builds
            // no control, and GUI.enabled = false would have killed the highlight too.
            var soon = new Rect(lx, row, lw, rowH);
            bool soonHot = UITheme.Tease(soon, "    Coming Soon...", nameSt);
            if (soonHot) UITheme.Fill(new Rect(soon.x + 6f, soon.y + 7f, 3f, soon.height - 14f), UITheme.Dim);

            // ---- Back / Next anchored to the screen edges (same as the customize screen) ----
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            float bw = 150f, edge = 24f, by = MenuScale.Height - 72f;
            // Hide the preview before handing off: the next screen builds its own preview in this
            // same frame, and this one's Destroy does not land until the end of it. See
            // PlayerPreview.Hide. Teardown still runs from OnDestroy.
            if (UITheme.Button(new Rect(edge, by, bw, 44f), "Back", btn))
            {
                enabled = false; if (_preview != null) _preview.Hide(); _onBack?.Invoke(); return;
            }
            var keep = GUI.backgroundColor; GUI.backgroundColor = UITheme.GoodTint;
            bool next = UITheme.Button(new Rect(MenuScale.Width - edge - bw, by, bw, 44f), "Next", btn);
            GUI.backgroundColor = keep;
            if (next)
            {
                enabled = false; if (_preview != null) _preview.Hide(); _onPicked?.Invoke(); return;
            }
        }

        // Click-drag anywhere on the preview to turn the model. Only grabs when the press lands
        // inside the preview rect, so the species rows are unaffected. Same shape as
        // CustomizeUI.HandleModelDrag.
        void HandleModelDrag(Rect previewRect)
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && previewRect.Contains(e.mousePosition))
            {
                _draggingModel = true; _lastDragX = e.mousePosition.x; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingModel)
            {
                _preview.AddYaw((e.mousePosition.x - _lastDragX) * 0.6f);
                _lastDragX = e.mousePosition.x; e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingModel && e.button == 0)
            {
                _draggingModel = false; e.Use();
            }
        }
    }
}
