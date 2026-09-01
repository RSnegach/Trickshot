using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Esc pause menu. Freezes the game (Time.timeScale = 0) and frees the cursor while paused;
    /// exposes a global Paused flag the controllers check so no input is processed behind it.
    ///
    /// Entries, in order, each shown only when it applies:
    ///   Resume          - unpause
    ///   Restart Match   - rebuild the same mode with the same settings (single-player only)
    ///   Match Setup     - tear down and reopen the pre-match slider config for this mode
    ///   Options         - keybindings / audio / quickchat / camera + display
    ///   Leave Match     - client only: drop out without ending the match for everyone else
    ///   End Match       - host: ends it for everyone. Single-player: quit to the main menu
    ///   Quit to Desktop - close the game
    ///
    /// Destructive entries (End Match, Quit to Desktop) go through a confirm card, so a misclick
    /// while paused can't throw away a match. Mouse and keyboard drive the same selection: arrows
    /// or W/S move, Enter or Space activates, Esc backs out one level.
    ///
    /// The mode HUDs keep drawing behind this (only their Update is gated on Paused), so the
    /// scrim is deliberately partial: the live score, clock, and stat panel stay readable.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public static bool Paused { get; private set; }

        System.Action _onMainMenu;
        System.Action _onMatchSetup;
        System.Action _onRestart;  // single-player only: rebuild this mode as-is
        System.Action _onLeave;    // client-only: leave a net match without ending it for others
        OptionsMenu _options;
        bool _optionsOpen;
        float _savedTimeScale = 1f;
        string _modeLabel;

        // Layout - unchanged from the original so the menu keeps its footprint.
        const float BtnW = 300f, BtnH = 60f, Gap = 16f;

        enum Kind { Normal, Bad }
        struct Entry
        {
            public string Label;
            public System.Action Act;
            public Kind Kind;
            public string ConfirmTitle, ConfirmBody;   // non-null -> route through the confirm card
        }
        readonly List<Entry> _entries = new List<Entry>();
        int _sel;

        // Pending destructive action awaiting confirmation (null when not confirming).
        System.Action _confirmAct;
        string _confirmTitle, _confirmBody;
        bool _confirmYes;   // which of Cancel/Confirm the keyboard has highlighted

        public void Init(System.Action onMainMenu, System.Action onMatchSetup = null, GameInput input = null,
                         System.Action onLeave = null, System.Action onRestart = null, GameMode? mode = null)
        {
            _onMainMenu = onMainMenu;
            _onMatchSetup = onMatchSetup;
            _onLeave = onLeave;
            _onRestart = onRestart;
            if (input != null) _options = new OptionsMenu(input);
            _modeLabel = mode.HasValue ? ModeName(mode.Value) : null;
            Paused = false;
        }

        public static string ModeName(GameMode m)
        {
            switch (m)
            {
                case GameMode.Striker:    return "Striker";
                case GameMode.Goalkeeper: return "Goalkeeper";
                case GameMode.Accuracy:   return "Accuracy";
                case GameMode.FreeKick:   return SimConfig.PenaltyMode ? "Penalties" : "Free Kick";
                case GameMode.Match:  return "Match";
                case GameMode.SetPieces:  return "Set Pieces";
            }
            return "Match";
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                // A quickchat text field owns Escape while open (and for one frame after it closes, to
                // swallow the raw key read that lands a frame after the IMGUI close). Escape closes the
                // field and must NOT also open the pause menu.
                if (QuickChatFeed.EscapeOwned) return;
                // Same reasoning: the cross-targeting map (GameManager, single-player striker) closes
                // on Escape too, and must not ALSO open the pause menu on that same press.
                if (GameManager.CrossMapEscapeOwned) return;

                // Back out one level at a time: confirm card -> options -> buttons -> unpause.
                if (_confirmAct != null) { ClearConfirm(); return; }
                if (_optionsOpen)
                {
                    // If a rebind is listening, the rebind op consumes Esc itself, so ignore it here.
                    if (_options != null && _options.IsRebinding) return;
                    _optionsOpen = false;
                    return;
                }
                if (Paused) Resume(); else Pause();
                return;
            }

            // Keyboard navigation. Polled here rather than off IMGUI key events so it works
            // regardless of GUI focus; the Input System is unaffected by timeScale = 0.
            if (!Paused || _optionsOpen) return;

            bool up = kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
            bool down = kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            bool go = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                      || kb.spaceKey.wasPressedThisFrame;

            if (_confirmAct != null)
            {
                if (up || down || kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                    _confirmYes = !_confirmYes;
                if (go)
                {
                    if (_confirmYes) { var act = _confirmAct; ClearConfirm(); act?.Invoke(); }
                    else ClearConfirm();
                }
                return;
            }

            BuildEntries();
            if (_entries.Count == 0) return;
            if (up) _sel = (_sel - 1 + _entries.Count) % _entries.Count;
            if (down) _sel = (_sel + 1) % _entries.Count;
            if (go) Activate(_entries[Mathf.Clamp(_sel, 0, _entries.Count - 1)]);
        }

        void Pause()
        {
            Paused = true;
            _savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            GameInput.CaptureCursor(false);
            _sel = 0;
            ClearConfirm();
        }

        void Resume()
        {
            Paused = false;
            Time.timeScale = _savedTimeScale <= 0f ? 1f : _savedTimeScale;
            GameInput.CaptureCursor(true);
            ClearConfirm();
        }

        void ClearConfirm() { _confirmAct = null; _confirmTitle = null; _confirmBody = null; _confirmYes = false; }

        // Restore time and clear the paused flag before any teardown callback runs, so a torn-down
        // match can never leave the game frozen.
        void Unfreeze()
        {
            Time.timeScale = 1f;
            Paused = false;
        }

        void Activate(Entry e)
        {
            if (e.ConfirmTitle != null)
            {
                _confirmAct = e.Act;
                _confirmTitle = e.ConfirmTitle;
                _confirmBody = e.ConfirmBody;
                _confirmYes = false;
                return;
            }
            e.Act?.Invoke();
        }

        void BuildEntries()
        {
            _entries.Clear();

            _entries.Add(new Entry { Label = "Resume", Act = Resume });

            // Rebuild the same mode with the same settings. Single-player only: there is no host
            // migration or match reset in the net protocol, so a restart mid-session would strand
            // every client. GameBootstrap passes null when a session is active.
            if (_onRestart != null)
                _entries.Add(new Entry { Label = "Restart Match", Act = () => { Unfreeze(); _onRestart?.Invoke(); } });

            if (_onMatchSetup != null)
                _entries.Add(new Entry { Label = "Match Setup", Act = () => { Unfreeze(); _onMatchSetup?.Invoke(); } });

            if (_options != null)
                _entries.Add(new Entry { Label = "Options", Act = () => _optionsOpen = true });

            // Client in a networked match: leave without ending it for everyone else. The host
            // keeps running the sim and this player's slot reverts to AI.
            if (_onLeave != null)
                _entries.Add(new Entry
                {
                    Label = "Leave Match", Kind = Kind.Bad,
                    Act = () => { Unfreeze(); _onLeave?.Invoke(); },
                    ConfirmTitle = "LEAVE MATCH?", ConfirmBody = "Your slot goes back to AI."
                });

            // For a networked HOST this ends the match for everyone (no host migration); in
            // single-player it's just quit-to-menu. Label and confirm text reflect that.
            bool isHost = Trickshot.Net.Multiplayer.IsHost;
            _entries.Add(new Entry
            {
                Label = isHost ? "End Match" : "Main Menu", Kind = Kind.Bad,
                Act = () => { Unfreeze(); _onMainMenu?.Invoke(); },
                ConfirmTitle = isHost ? "END MATCH?" : "QUIT TO MENU?",
                ConfirmBody = isHost ? "Ends the match for everyone." : "Match progress is lost."
            });

            _entries.Add(new Entry
            {
                Label = "Quit to Desktop", Kind = Kind.Bad,
                Act = () => { Unfreeze(); Trickshot.Net.Multiplayer.End(); Application.Quit(); },
                ConfirmTitle = "QUIT TO DESKTOP?",
                ConfirmBody = "Closes the game."
            });
        }

        void OnGUI()
        {
            if (!Paused) return;

            // Fit to the window (see MenuScale): virtual coordinates from here on, so use
            // MenuScale.Width/Height instead of Screen.*.
            MenuScale.Begin();

            // Options overlay takes over the pause screen while open.
            if (_optionsOpen && _options != null)
            {
                _options.Draw(() => _optionsOpen = false);
                MenuScale.End();
                return;
            }

            float sw = MenuScale.Width, sh = MenuScale.Height;
            BuildEntries();

            int rows = _entries.Count;
            float cx = sw * 0.5f - BtnW * 0.5f;
            float cy = sh * 0.5f - (rows * BtnH + (rows - 1) * Gap) * 0.5f;

            // Partial scrim: dark enough to lift the menu off the pitch, light enough that the
            // live scoreboard and stat panel behind it stay readable.
            UITheme.Scrim(sw, sh, 0.52f, BtnW + 300f);

            // --- header: PAUSED + what match this is ---
            UITheme.Title(new Rect(0f, cy - 104f, sw, 74f), "PAUSED");
            string ctx = MatchContext();
            if (!string.IsNullOrEmpty(ctx))
                UITheme.Hint(new Rect(0f, cy - 34f, sw, 24f), ctx);

            if (_confirmAct != null) { DrawConfirm(sw, sh); MenuScale.End(); return; }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            var e = Event.current;

            for (int i = 0; i < rows; i++)
            {
                var r = new Rect(cx, cy + i * (BtnH + Gap), BtnW, BtnH);

                // Mouse and keyboard share one selection, so hovering moves the highlight and
                // Enter always fires whatever is highlighted.
                if (r.Contains(e.mousePosition)) _sel = i;
                bool sel = i == _sel;

                var keep = GUI.backgroundColor;
                if (sel) GUI.backgroundColor = _entries[i].Kind == Kind.Bad ? UITheme.BadTint : UITheme.SelTint;

                // Selection glow: the plate tint can read subtle on a bright pitch, so the
                // selected row also carries a soft ambient bloom. (The old bright bar off the
                // leading edge is gone - selection reads from the tint + glow alone.)
                if (sel)
                {
                    Color bar = _entries[i].Kind == Kind.Bad ? UITheme.Red : UITheme.Gold;
                    UITheme.Glow(new Rect(r.x - 26f, r.y - 6f, r.width + 52f, r.height + 12f),
                                 new Color(bar.r, bar.g, bar.b, 0.10f));
                }

                bool hit = GUI.Button(r, _entries[i].Label, btn);
                GUI.backgroundColor = keep;
                if (hit) { Activate(_entries[i]); break; }   // list may be rebuilt by the action
            }

            UITheme.Hint(new Rect(0f, cy + rows * (BtnH + Gap) + 10f, sw, 24f),
                         "Esc resume   Up/Down select   Enter confirm");

            MenuScale.End();
        }

        // Mode + venue, so a paused screenshot says what you were playing. Live score and clock
        // are already on screen: the mode HUDs draw through the scrim.
        string MatchContext()
        {
            string venue = StadiumStyle.Active != null ? StadiumStyle.Active.Name : null;
            string net = Trickshot.Net.Multiplayer.IsHost ? "Hosting"
                       : Trickshot.Net.Multiplayer.IsClient ? "Connected" : null;

            string s = _modeLabel;
            if (!string.IsNullOrEmpty(venue)) s = string.IsNullOrEmpty(s) ? venue : s + "   ·   " + venue;
            if (net != null) s = string.IsNullOrEmpty(s) ? net : s + "   ·   " + net;
            return s;
        }

        void DrawConfirm(float sw, float sh)
        {
            const float w = 440f, h = 200f;
            var r = new Rect(sw * 0.5f - w * 0.5f, sh * 0.5f - h * 0.5f, w, h);
            UITheme.Panel(r, UITheme.Red);

            var t = new GUIStyle(GUI.skin.label)
            { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            UITheme.Shadowed(new Rect(r.x, r.y + 22f, r.width, 38f), _confirmTitle, t, UITheme.Ink, 0.7f, 2f);
            UITheme.Hint(new Rect(r.x + 20f, r.y + 66f, r.width - 40f, 24f), _confirmBody);

            const float bw = 176f, bh = 48f;
            float by = r.yMax - bh - 24f;
            var cancel = new Rect(r.center.x - bw - 8f, by, bw, bh);
            var ok = new Rect(r.center.x + 8f, by, bw, bh);
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };

            var e = Event.current;
            if (cancel.Contains(e.mousePosition)) _confirmYes = false;
            if (ok.Contains(e.mousePosition)) _confirmYes = true;

            var keep = GUI.backgroundColor;
            if (!_confirmYes) GUI.backgroundColor = UITheme.SelTint;
            bool no = GUI.Button(cancel, "Cancel", btn);
            GUI.backgroundColor = _confirmYes ? UITheme.BadTint : keep;
            bool yes = GUI.Button(ok, "Confirm", btn);
            GUI.backgroundColor = keep;

            if (no) ClearConfirm();
            else if (yes) { var act = _confirmAct; ClearConfirm(); act?.Invoke(); }
        }

        void OnDestroy()
        {
            // Never leave the game frozen if this object is destroyed while paused.
            if (Paused) { Time.timeScale = 1f; Paused = false; }
            _options?.Dispose();   // abort any in-flight rebind operation
        }
    }
}
