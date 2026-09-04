using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The Solo cup's fork screen (design 3.3): two cards, PENALTIES and FREE KICKS, and Back.
    /// Shaped exactly like AccuracyModeUI - the cup has no settings (no sliders, no goal editor:
    /// regulation goal, the stage ramp owns the keeper, the field is always 32), so the format is
    /// the only choice and the words are the whole screen. Picking a card is the start of the cup:
    /// the caller rolls the seed (CupDirector.RollSeed), builds the match and Launches the
    /// director, which opens on CHOOSE YOUR NATION. Back returns to Customize.
    ///
    /// Multiplayer never shows this: the host setup screen carries Play style + Format instead.
    /// </summary>
    public class CupSetupUI : MonoBehaviour
    {
        System.Action<CupFormat> _onPick;
        System.Action _onBack;

        public void Init(System.Action<CupFormat> onPick, System.Action onBack)
        {
            _onPick = onPick;
            _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            MenuScale.Begin();
            // One exit point, so no branch can leak the scaled GUI matrix (MenuScale.Begin pushes a
            // matrix that End pops; an early return inside a card's click handler would strand it).
            // The callbacks fire AFTER End: any of them may destroy this object.
            System.Action fire = null;

            float w = MenuScale.Width, h = MenuScale.Height;
            UITheme.Scrim(w, h, 0.40f, 900f);

            const float cardW = 380f, cardH = 110f, gap = 22f;
            float total = cardH * 2f + gap;
            float top = h * 0.5f - total * 0.5f + 26f;
            float cx = w * 0.5f - cardW * 0.5f;

            UITheme.Title(new Rect(0f, top - 108f, w, 70f), CupText.Title, 44, showRule: false);

            if (Card(new Rect(cx, top, cardW, cardH), CupText.PenaltiesName.ToUpperInvariant()))
                fire = () => _onPick?.Invoke(CupFormat.Penalties);

            if (Card(new Rect(cx, top + cardH + gap, cardW, cardH), CupText.FreeKicksName.ToUpperInvariant()))
                fire = () => _onPick?.Invoke(CupFormat.FreeKicks);

            // Cached, not built per pass: OnGUI runs several times a frame, and every other cup
            // screen keeps its styles in statics (the card's _titleSt right below does the same).
            _backSt ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(w * 0.5f - 90f, top + total + 22f, 180f, 42f), "Back", _backSt))
                fire = () => _onBack?.Invoke();

            // Esc = Back. A key event allocates no control, so handling it here cannot shift ids
            // between the Layout and Repaint passes.
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                e.Use();
                fire = () => _onBack?.Invoke();
            }

            MenuScale.End();
            fire?.Invoke();   // may destroy this object; nothing after it touches `this`
        }

        // A panel carrying ONE big centred word, clickable as a whole. The control is allocated
        // LAST and unconditionally, exactly as UITheme.ModeCard does it: adding or removing controls
        // between the layout and repaint passes desynchronises IMGUI's ids and breaks every click.
        static GUIStyle _titleSt, _backSt;
        static bool Card(Rect r, string title)
        {
            var e = Event.current;
            bool hot = GUI.enabled && e != null && r.Contains(e.mousePosition);

            UITheme.Panel(r, hot ? UITheme.Gold : (Color?)null);
            if (hot)
                UITheme.Glow(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f),
                             new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.10f));

            _titleSt ??= new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold,
                                                       alignment = TextAnchor.MiddleCenter };
            var col = hot ? UITheme.Gold : UITheme.Ink;
            _titleSt.normal.textColor = col;
            UITheme.Shadowed(r, title, _titleSt, col, 0.6f, 2f);

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }
    }
}
