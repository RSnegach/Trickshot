using System;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The bracket screen (design 6.2): the header ("THE DRAW" on the first showing, then the
    /// stage), the nation count and format under it, the full tree, and a gold bar along the
    /// bottom that fills over CupTuning.BracketScreenSeconds. No buttons.
    ///
    /// Who advances: the DIRECTOR's flow. This screen only animates. While the director is in
    /// CupPhase.Bracket the bar reads director.PhaseTime - the host's timer on a client, since
    /// SetPhase carries it - so every peer's bar agrees with the phase change; outside that phase
    /// (the KNOCKED OUT card re-showing the tree, say) it runs on unscaled local time or, with
    /// runTimer false, shows no bar at all. <see cref="OnElapsed"/> fires once when the bar fills,
    /// for a flow that would rather be told than poll PhaseTime.
    ///
    /// Reveal: on the first showing the Round of 32 names fade in over CupTuning.RevealSeconds in
    /// tree order; later showings draw the (shrunken) tree at once.
    /// </summary>
    public class CupBracketScreen : MonoBehaviour
    {
        public static CupBracketScreen Create(Transform root, CupDirector director)
        {
            var go = new GameObject("CupBracketScreen");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupBracketScreen>();
            ui.Init(director);
            return ui;
        }

        /// <summary>Fired once per Show when the timer bar fills (only while the timer runs).</summary>
        public Action OnElapsed { get; set; }
        /// <summary>The first stage drawn (the tree collapses to it).</summary>
        public CupStage UpTo { get; private set; }
        /// <summary>"THE DRAW" header + the Round of 32 reveal.</summary>
        public bool First { get; private set; }
        public bool TimerRunning { get; private set; }
        /// <summary>How long the bar takes to fill (default CupTuning.BracketScreenSeconds).</summary>
        public float Seconds { get; set; } = CupTuning.BracketScreenSeconds;

        /// <summary>Seconds since Show: the director's phase timer while in the Bracket phase, else local unscaled time.</summary>
        public float Elapsed
        {
            get
            {
                if (_director != null && _director.Phase == CupPhase.Bracket) return _director.PhaseTime;
                return Time.unscaledTime - _shownAt;
            }
        }

        CupDirector _director;
        Action _draw;
        bool _hooked, _closed, _elapsedFired, _wasPaused;
        float _shownAt;

        void Init(CupDirector director)
        {
            _director = director;
            var stage = director != null ? director.Stage : CupStage.RoundOf32;
            // First showing = the draw itself. A flow showing a later stage (or re-showing the
            // Round of 32 after Play Again) passes its own flags through Show.
            Show(stage, stage == CupStage.RoundOf32, true);
            GameInput.CaptureCursor(false);
            _draw = Draw;
            if (_director != null) { _director.AddGuiHook(_draw); _hooked = true; }
        }

        /// <summary>Re-point the screen: the first stage to draw, the header/reveal mode, and whether the bar runs.</summary>
        public void Show(CupStage upTo, bool first, bool runTimer)
        {
            UpTo = CupStages.IsValid(upTo) ? upTo : CupStage.RoundOf32;
            First = first;
            TimerRunning = runTimer;
            _shownAt = Time.unscaledTime;
            _elapsedFired = false;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
        }

        void Update()
        {
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;

            if (TimerRunning && !_elapsedFired && Seconds > 0f && Elapsed >= Seconds)
            {
                _elapsedFired = true;
                OnElapsed?.Invoke();
            }
        }

        void OnGUI()
        {
            if (!_hooked) Draw();
        }

        void Draw()
        {
            if (_closed) return;
            MenuScale.Begin();
            float w = MenuScale.Width, h = MenuScale.Height;

            UITheme.Scrim(w, h, 0.55f, 1100f);

            var b = _director != null ? _director.Bracket : null;
            var format = _director != null ? _director.Format : CupFormat.Penalties;

            UITheme.Title(new Rect(0f, 14f, w, 60f), CupBracketView.Header(UpTo, First), 40, showRule: false);
            string sub = CupStages.EntrantsIn(UpTo) + " NATIONS   -   " + CupText.FormatName(format).ToUpperInvariant();
            UITheme.Hint(new Rect(0f, 74f, w, 20f), sub);

            float reveal = First ? Mathf.Clamp01(Elapsed / CupTuning.RevealSeconds) : 1f;
            int mine = _director != null ? _director.LocalEntrant : -1;
            bool coop = _director != null && _director.Style == CupStyle.Coop;
            CupBracketView.DrawFull(new Rect(24f, 100f, w - 48f, h - 160f), b, UpTo, mine, coop ? mine : -1, coop, reveal,
                                    _director != null ? _director.Players : null);

            if (TimerRunning && Seconds > 0f)
            {
                // The 5-second bar (gold), no button: the flow advances on its own timer.
                float t = Mathf.Clamp01(Elapsed / Seconds);
                UITheme.Bar(new Rect(w * 0.5f - 300f, h - 34f, 600f, 8f), t, UITheme.Gold, UITheme.Gold);
            }

            MenuScale.End();
        }
    }
}
