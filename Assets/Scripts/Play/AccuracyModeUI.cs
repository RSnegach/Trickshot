using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The fork single-player ACCURACY opens on: PRACTICE or CHALLENGE.
    ///
    /// CHALLENGE is the scored game (AccuracyGame): a random spot every round, the difficulty ladder
    /// in SimConfig.AccuracyTier, three strikes, and the career high score. It has nothing to set, so
    /// picking it SKIPS the pre-match screen entirely and starts the run.
    ///
    /// PRACTICE goes on to the pre-match screen, where the goal, the keeper and the target are all
    /// yours to dial and the ball is placed on the free-kick map - which stays available on M during
    /// the session, so a spot can be moved without leaving the mode. Nothing is scored and there are
    /// no strikes.
    ///
    /// Kept as its own screen rather than a row on the pre-match panel, because the whole point is
    /// that Challenge never shows that panel. The two buttons are just the words - what each mode
    /// does is the first thing either one shows you, so a blurb here only delays the choice.
    /// </summary>
    public class AccuracyModeUI : MonoBehaviour
    {
        System.Action _onPractice, _onChallenge, _onBack;

        public void Init(System.Action onPractice, System.Action onChallenge, System.Action onBack)
        {
            _onPractice = onPractice;
            _onChallenge = onChallenge;
            _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            MenuScale.Begin();
            // One exit point, so no branch can leak the scaled GUI matrix (MenuScale.Begin pushes a
            // matrix that End pops; an early return inside a card's click handler would strand it).
            System.Action fire = null;

            float w = MenuScale.Width, h = MenuScale.Height;
            UITheme.Scrim(w, h, 0.40f, 900f);

            const float cardW = 380f, cardH = 110f, gap = 22f;
            float total = cardH * 2f + gap;
            float top = h * 0.5f - total * 0.5f + 26f;
            float cx = w * 0.5f - cardW * 0.5f;

            UITheme.Title(new Rect(0f, top - 108f, w, 70f), "ACCURACY", 44, showRule: false);

            if (Card(new Rect(cx, top, cardW, cardH), "PRACTICE"))
                fire = () => { SimConfig.AccuracyPractice = true; _onPractice?.Invoke(); };

            if (Card(new Rect(cx, top + cardH + gap, cardW, cardH), "CHALLENGE"))
                fire = () => { SimConfig.AccuracyPractice = false; _onChallenge?.Invoke(); };

            // No keeper row here: BOTH modes have their own setup screen now, and that is where the
            // keeper is chosen (a ladder in practice, a yes/no in the challenge). This screen is the
            // fork and nothing else.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(w * 0.5f - 90f, top + total + 22f, 180f, 42f), "Back", btn))
                fire = () => _onBack?.Invoke();

            MenuScale.End();
            fire?.Invoke();   // may destroy this object; nothing after it touches `this`
        }

        // A panel carrying ONE big centred word, clickable as a whole. The control is allocated
        // LAST and unconditionally, exactly as UITheme.ModeCard does it: adding or removing controls
        // between the layout and repaint passes desynchronises IMGUI's ids and breaks every click.
        static GUIStyle _titleSt;
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
