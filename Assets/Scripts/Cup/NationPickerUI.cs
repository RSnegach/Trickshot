using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// CHOOSE YOUR NATION (design 6.1), for all three styles.
    ///
    ///   Solo          one strip cell (you), the list, Back / Esc returns to the fork screen.
    ///   Head to Head  the top strip shows every player with a 40 px flag slot that pops in the
    ///                 moment they pick; picking IS the ready; a taken nation dims with
    ///                 "taken by Bob" and does not respond; no Back (the lobby already committed).
    ///   Co-op         no strip; everyone votes; a nation with several votes wears a counter disc
    ///                 on its flag; the leading nation carries the gold spine; "majority reached"
    ///                 replaces the hint; CAPTAIN DECIDES appears for the Captain when everyone has
    ///                 picked without a majority.
    ///
    /// The screen only READS the director (Players, NationVotes, LeadingNation, MajorityReached,
    /// NationTaken) and calls two intents: PickNation(i) and CaptainDecides(). Host and clients
    /// draw the same thing because both draw from the director's read model, which is the host's
    /// CupState on a client. A client's own pick shows at once as "pending" (the lit row) and
    /// snaps back if the host's echo never comes - the race loser in Head to Head.
    ///
    /// IMGUI discipline (the house rules): every control is allocated on every pass. The filtered
    /// list is rebuilt in Update, never during a GUI pass, so the row count cannot change between
    /// IMGUI's Layout and event passes. Keys are handled before the search field draws (the field
    /// eats Return/Escape/arrows otherwise) and every navigation callback fires after
    /// MenuScale.End(). Nothing here is skipped while paused - controls are disabled instead.
    /// </summary>
    public class NationPickerUI : MonoBehaviour
    {
        // ---- layout ---------------------------------------------------------------------------
        const float TitleY = 12f, TitleH = 64f;
        const float StripH = 64f;
        // The list is a GRID of cards (owner's call): four columns across the width, every nation
        // on its own plate with a big flag, everything alphabetical (novelty kits included, no
        // section of their own).
        const float GridMaxW = 1180f;      // four columns at the 1280 design width
        const int Cols = 4;
        const float CardH = 84f;
        const float CardGap = 10f;
        const float FlagSz = 56f;          // the flag on a card
        const float ThumbSz = 40f;         // the strip's flag slot
        const float SearchH = 32f;
        const float FooterH = 96f;         // hint + buttons under the list
        const float PendingSnapBack = 2.5f;   // a client pick with no echo snaps back after this long

        /// <summary>Create the screen under `root` for the director's current style.</summary>
        public static NationPickerUI Create(Transform root, CupDirector director, CupStyle variant)
        {
            return Create(root, director, variant, null);
        }

        /// <summary>The same, with the Solo Back / Esc callback (ignored in the multiplayer variants).</summary>
        public static NationPickerUI Create(Transform root, CupDirector director, CupStyle variant, Action onBack)
        {
            var go = new GameObject("NationPickerUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<NationPickerUI>();
            ui.Init(director, variant, onBack);
            return ui;
        }

        /// <summary>Solo only: Back / Esc. Fired after the GUI pass; it may destroy this screen.</summary>
        public Action OnBack { get; set; }

        /// <summary>
        /// A Solo picker is up: Esc is Back, so the pause menu must not ALSO open on that press
        /// (PauseMenu checks CupEscape.Owned). The multiplayer variants have no Back and leave Esc
        /// to the pause menu. Kept one frame past Close for the raw key read that can land a frame
        /// after the IMGUI event (QuickChatFeed's precedent).
        /// </summary>
        public static bool EscapeOwned => s_soloOpen > 0 || Time.frameCount <= s_escGraceFrame;
        static int s_soloOpen;
        static int s_escGraceFrame = -1;
        public CupStyle Variant => _variant;
        /// <summary>The nation the local player currently has lit (their pick, or a pending one), -1 if none.</summary>
        public int LocalSelection => _pendingPick >= 0 ? _pendingPick : (_director != null && _director.LocalPlayer != null ? _director.LocalPlayer.Nation : -1);

        CupDirector _director;
        CupStyle _variant;
        Action _draw;
        bool _hooked, _closed;

        // The list: every resolved nation, table (alphabetical) order, novelty kits interleaved.
        readonly List<int> _main = new List<int>();
        // What is on screen this frame: nation indices in grid order (row-major, Cols per row).
        readonly List<int> _visible = new List<int>();
        float _contentH;
        string _search = "", _appliedSearch = null;
        Vector2 _scroll;
        int _cursor = -1;
        bool _focusSearch = true;

        // Head to Head strip: when each slot's flag popped in (unscaled time).
        readonly Dictionary<int, float> _popStart = new Dictionary<int, float>();
        readonly Dictionary<int, int> _lastNation = new Dictionary<int, int>();

        int _pendingPick = -1;
        float _pendingAt;
        bool _wasPaused;

        static GUIStyle _nameSt, _takenSt, _stripNameSt, _countSt, _searchSt, _placeholderSt, _btnSt, _bigBtnSt, _gateSt;

        void Init(CupDirector director, CupStyle variant, Action onBack)
        {
            _director = director;
            _variant = variant;
            OnBack = onBack;

            // The picker list is every table row that resolves to a jersey design, in table order
            // (which is alphabetical - CupNationTable mirrors the Nations tab). Novelty kits sit
            // in the same alphabetical run as everyone else (owner's call); a human may pick one,
            // only the AI draw excludes them (design 2.4).
            var all = CupNations.Resolved();
            for (int i = 0; i < all.Count; i++) _main.Add(all[i]);
            Refilter();

            // The results screen's per-kick ledger has to be listening from the first round, and
            // this is the first screen of every cup (Play Again lands here too), so attach it now.
            if (_director != null) CupStatsLedger.Attach(_director);

            // Start the keyboard cursor on the current pick (a rejoin / Play Again keeps it).
            var me = _director != null ? _director.LocalPlayer : null;
            _cursor = me != null && me.Nation >= 0 ? _visible.IndexOf(me.Nation) : FirstPickable(0, +1);

            GameInput.CaptureCursor(false);
            if (_variant == CupStyle.Solo) { s_soloOpen++; _ownsEsc = true; }
            _draw = Draw;
            if (_director != null) { _director.AddGuiHook(_draw); _hooked = true; }
        }

        bool _ownsEsc;

        /// <summary>Unregister and destroy. Safe to call twice.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            ReleaseEsc();
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            ReleaseEsc();
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
        }

        void ReleaseEsc()
        {
            if (!_ownsEsc) return;
            _ownsEsc = false;
            if (s_soloOpen > 0) s_soloOpen--;
            s_escGraceFrame = Time.frameCount + 1;
        }

        void Update()
        {
            // PauseMenu.Resume re-captures the cursor unconditionally; a menu screen wants it free.
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;

            // The list is rebuilt HERE, once per frame, never inside a GUI pass: the search text
            // changes during a KeyDown pass, and a row count that differed between that pass and
            // the next Repaint would shift every control id after the field.
            if (!string.Equals(_appliedSearch, _search, StringComparison.Ordinal)) Refilter();

            if (_director == null) return;

            // Strip flag pops: a slot whose nation went from none to something pops in now. A
            // nation already there when the screen opened (rejoin, Play Again race) does not pop.
            var players = _director.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                int last;
                bool seen = _lastNation.TryGetValue(p.Slot, out last);
                if (!seen) { _lastNation[p.Slot] = p.Nation; continue; }
                if (p.Nation != last)
                {
                    if (p.Nation >= 0) _popStart[p.Slot] = Time.unscaledTime;
                    _lastNation[p.Slot] = p.Nation;
                }
            }

            // A pending pick clears on the echo (the director's copy now says so) or snaps back.
            if (_pendingPick >= 0)
            {
                var me = _director.LocalPlayer;
                if (me != null && me.Nation == _pendingPick) _pendingPick = -1;
                else if (Time.unscaledTime - _pendingAt > PendingSnapBack) _pendingPick = -1;
            }
        }

        // ---- the list model --------------------------------------------------------------------

        void Refilter()
        {
            _appliedSearch = _search ?? "";
            string q = _appliedSearch.Trim();
            _visible.Clear();
            for (int i = 0; i < _main.Count; i++)
                if (Matches(_main[i], q)) _visible.Add(_main[i]);
            int rows = (_visible.Count + Cols - 1) / Cols;
            _contentH = rows > 0 ? rows * (CardH + CardGap) - CardGap : 0f;
            if (_cursor >= _visible.Count) _cursor = FirstPickable(0, +1);
        }

        // ---- grid geometry (content space, inside the scroll view) ----
        static float CardW(float viewW) => (viewW - (Cols - 1) * CardGap) / Cols;
        static float CardY(int i) => (i / Cols) * (CardH + CardGap);
        static float CardX(int i, float viewW) => (i % Cols) * (CardW(viewW) + CardGap);

        static bool Matches(int nation, string q)
        {
            if (string.IsNullOrEmpty(q)) return true;
            string name = CupNations.Name(nation);
            if (name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string code = CupNations.Code(nation);
            return code.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The first row at or after `from` (dir +1) / at or before (dir -1) that is a nation, or -1.</summary>
        int FirstPickable(int from, int dir)
        {
            for (int i = from; i >= 0 && i < _visible.Count; i += dir) if (_visible[i] >= 0) return i;
            return -1;
        }

        bool IsTaken(int nation, out CupPlayer by)
        {
            by = null;
            if (_director == null || _variant != CupStyle.HeadToHead) return false;
            int slot;
            if (!_director.NationTaken(nation, out slot)) return false;
            if (slot == _director.LocalSlot) return false;
            by = _director.PlayerAt(slot);
            return true;
        }

        /// <summary>Move the keyboard cursor by `dir` cards (+-1 along a row, +-Cols between rows), clamped to the grid; keeps the card on screen.</summary>
        void MoveCursor(int dir, float listH)
        {
            if (_visible.Count == 0) return;
            _cursor = _cursor < 0 ? 0 : Mathf.Clamp(_cursor + dir, 0, _visible.Count - 1);
            float cy = CardY(_cursor);
            if (cy < _scroll.y) _scroll.y = cy;
            else if (cy + CardH > _scroll.y + listH) _scroll.y = cy + CardH - listH;
        }

        void Pick(int nation)
        {
            if (_director == null || nation < 0) return;
            CupPlayer by;
            if (IsTaken(nation, out by)) return;
            _pendingPick = nation;
            _pendingAt = Time.unscaledTime;
            _director.PickNation(nation);
        }

        // ---- drawing ---------------------------------------------------------------------------

        void OnGUI()
        {
            if (!_hooked) Draw();   // no director hook: draw on our own (never both)
        }

        void Draw()
        {
            if (_closed) return;
            EnsureStyles();
            MenuScale.Begin();
            Action fire = null;   // navigation fires AFTER End; it may destroy this object

            float w = MenuScale.Width, h = MenuScale.Height;
            bool paused = PauseMenu.Paused;
            var e = Event.current;

            UITheme.Scrim(w, h, 0.40f, 900f);

            // ---- keys, before any control (the search field would eat them) ----
            // Esc is only ours in Solo (Back); in multiplayer it belongs to the pause menu, which
            // polls it in Update - consuming the IMGUI event here would change nothing there.
            bool enterPick = false, esc = false, up = false, down = false, left = false, right = false;
            if (!paused && e != null && e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter: enterPick = true; e.Use(); break;
                    case KeyCode.Escape: if (_variant == CupStyle.Solo) { esc = true; e.Use(); } break;
                    case KeyCode.UpArrow: up = true; e.Use(); break;
                    case KeyCode.DownArrow: down = true; e.Use(); break;
                    case KeyCode.LeftArrow: left = true; e.Use(); break;
                    case KeyCode.RightArrow: right = true; e.Use(); break;
                }
            }

            GUI.enabled = !paused;

            // ---- title + tag ----
            UITheme.Title(new Rect(0f, TitleY, w, TitleH), CupText.ChooseYourNation, 44, showRule: false);

            float y = TitleY + TitleH + 30f;
            if (_variant != CupStyle.Coop)
            {
                DrawStrip(w, y);
                y += StripH + 12f;
            }

            // ---- search (as wide as the grid) ----
            float listW = Mathf.Min(GridMaxW, w - 48f);
            float lx = w * 0.5f - listW * 0.5f;
            var sr = new Rect(lx, y, listW, SearchH);
            UITheme.Fill(sr, CupUiKit.Well);
            UITheme.Fill(new Rect(sr.x, sr.yMax - 1f, sr.width, 1f), new Color(1f, 1f, 1f, 0.12f));
            GUI.SetNextControlName("CupNationSearch");
            _search = GUI.TextField(new Rect(sr.x + 10f, sr.y + 4f, sr.width - 20f, sr.height - 8f), _search ?? "", 24, _searchSt);
            if (string.IsNullOrEmpty(_search))
                UITheme.Label(new Rect(sr.x + 12f, sr.y, sr.width - 24f, sr.height), "Search nations", _placeholderSt);
            if (_focusSearch && !paused && e != null && e.type == EventType.Repaint)
            {
                // Typing filters straight away; done once so a click elsewhere keeps its focus.
                GUI.FocusControl("CupNationSearch");
                _focusSearch = false;
            }
            y += SearchH + 10f;

            // ---- the grid ----
            float listH = Mathf.Max(CardH * 2f, h - FooterH - y);
            if (up) MoveCursor(-Cols, listH);
            if (down) MoveCursor(+Cols, listH);
            if (left) MoveCursor(-1, listH);
            if (right) MoveCursor(+1, listH);
            DrawList(lx, y, listW, listH, e, ref fire);

            if (enterPick && _cursor >= 0 && _cursor < _visible.Count && _visible[_cursor] >= 0)
            {
                int nation = _visible[_cursor];
                fire = () => Pick(nation);
            }

            // ---- footer ----
            DrawFooter(w, h, ref fire);

            if (esc) fire = () => OnBack?.Invoke();   // Solo only (see the key block above)

            GUI.enabled = true;
            MenuScale.End();
            fire?.Invoke();   // may destroy this object; nothing after it touches `this`
        }

        /// <summary>The Head to Head strip (Solo shows its single cell): name, and a flag slot that pops in on the pick.</summary>
        void DrawStrip(float w, float y)
        {
            var players = _director != null ? _director.Players : null;
            int n = 0;
            if (players != null) for (int i = 0; i < players.Count; i++) if (!players[i].Left) n++;
            if (n == 0) return;
            const float gap = 10f;
            float cellW = Mathf.Min(236f, (w - 60f - (n - 1) * gap) / n);
            float total = n * cellW + (n - 1) * gap;
            float x = w * 0.5f - total * 0.5f;
            float now = Time.unscaledTime;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p.Left) continue;
                bool isMe = _director != null && p.Slot == _director.LocalSlot;
                var c = new Rect(x, y, cellW, StripH);
                UITheme.Fill(c, CupUiKit.Well);
                if (isMe)
                {
                    UITheme.Fill(c, CupUiKit.LitBand);
                    CupUiKit.Spine(c, Hud.SlotColor(p.Slot));
                }
                UITheme.Fill(new Rect(c.x, c.y, c.width, 1f), new Color(1f, 1f, 1f, 0.10f));

                _stripNameSt.normal.textColor = p.ReplacedByAi ? UITheme.Dim : UITheme.Ink;
                UITheme.Label(new Rect(c.x + 14f, c.y, c.width - 70f, c.height), p.DisplayName, _stripNameSt);

                // The flag slot: an empty well until they pick, then the flag pops in (scale 0 -> 1
                // with an overshoot over FlagPopSeconds, unscaled so a Solo pause never freezes it).
                var slot = new Rect(c.xMax - 12f - ThumbSz, c.y + (StripH - ThumbSz) * 0.5f, ThumbSz, ThumbSz);
                UITheme.Fill(slot, new Color(0f, 0f, 0f, 0.35f));
                UITheme.FrameOutline(slot, new Color(1f, 1f, 1f, 0.10f));
                if (p.Nation >= 0)
                {
                    float s = 1f;
                    float started;
                    if (_popStart.TryGetValue(p.Slot, out started))
                        s = CupUiKit.EaseOutBack((now - started) / CupTuning.FlagPopSeconds);
                    if (s > 0.01f)
                    {
                        float sz = ThumbSz * s;
                        CupUiKit.Flag(new Rect(slot.center.x - sz * 0.5f, slot.center.y - sz * 0.5f, sz, sz), p.Nation);
                    }
                }
                x += cellW + gap;
            }
        }

        void DrawList(float lx, float ly, float lw, float lh, Event e, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            bool needScroll = _contentH > lh;
            float viewW = lw - (needScroll ? 16f : 0f);
            var view = new Rect(0f, 0f, viewW, Mathf.Max(_contentH, 1f));
            UITheme.Fill(new Rect(lx, ly, lw, lh), new Color(0f, 0f, 0f, 0.22f));
            _scroll = GUI.BeginScrollView(new Rect(lx, ly, lw, lh), _scroll, view, false, needScroll);

            int myNation = _director != null && _director.LocalPlayer != null ? _director.LocalPlayer.Nation : -1;
            int leading = -1, leadVotes = 0;
            if (_director != null && _variant == CupStyle.Coop) leading = _director.LeadingNation(out leadVotes);

            float cw = CardW(viewW);
            for (int i = 0; i < _visible.Count; i++)
            {
                int nation = _visible[i];
                // Cards well outside the window still ALLOCATE their control (below) - only the
                // decoration is skipped, so the id sequence is the same whatever the scroll.
                var row = new Rect(CardX(i, viewW), CardY(i), cw, CardH);
                bool onScreen = row.yMax >= _scroll.y - CardH && row.y <= _scroll.y + lh + CardH;
                CupPlayer takenBy;
                bool taken = IsTaken(nation, out takenBy);
                bool selected = nation == myNation || nation == _pendingPick;
                bool leadRow = _variant == CupStyle.Coop && leadVotes > 0 && nation == leading;
                bool hot = outerEnabled && !taken && e != null && row.Contains(e.mousePosition);
                bool cursor = i == _cursor;

                if (onScreen)
                {
                    // Every nation on its own plate: a well, lit when picked / hovered, a gold
                    // spine for the pick (and the Co-op leader), a hairline frame otherwise.
                    UITheme.Fill(row, CupUiKit.Well);
                    if (selected) { UITheme.Fill(row, CupUiKit.LitBand); CupUiKit.Spine(row, UITheme.Gold); }
                    else if (hot || cursor) UITheme.Fill(row, CupUiKit.HoverBand);
                    if (leadRow && !selected) CupUiKit.Spine(row, UITheme.Gold);
                    UITheme.FrameOutline(row, selected ? UITheme.Gold : new Color(1f, 1f, 1f, 0.08f));

                    float alpha = taken ? 0.45f : 1f;
                    var fr = new Rect(row.x + 14f, row.y + (CardH - FlagSz) * 0.5f, FlagSz, FlagSz);
                    CupUiKit.Flag(fr, nation, alpha);

                    float tx = fr.xMax + 12f, tw = row.xMax - 12f - tx;
                    _nameSt.normal.textColor = taken ? UITheme.Faint : (selected ? UITheme.Gold : UITheme.Ink);
                    if (taken)
                    {
                        UITheme.Label(new Rect(tx, row.y + 12f, tw, 30f), CupNations.Name(nation), _nameSt);
                        UITheme.Label(new Rect(tx, row.y + 44f, tw, 24f),
                                      CupText.TakenBy(takenBy != null ? takenBy.DisplayName : "another player"), _takenSt);
                    }
                    else UITheme.Label(new Rect(tx, row.y, tw, CardH), CupNations.Name(nation), _nameSt);

                    if (!taken && _variant == CupStyle.Coop && _director != null)
                    {
                        // Vote counter: a 20 px disc in the flag's bottom-right corner, only when
                        // more than one player is on the nation (design 6.1).
                        int votes = nation < _director.NationVotes.Length ? _director.NationVotes[nation] : 0;
                        if (votes > 1)
                        {
                            var dr = new Rect(fr.xMax - 12f, fr.yMax - 12f, 20f, 20f);
                            UITheme.Disc(new Rect(dr.x - 1f, dr.y - 1f, 22f, 22f), new Color(0f, 0f, 0f, 0.6f));
                            UITheme.Disc(dr, UITheme.Gold);
                            UITheme.Label(dr, votes.ToString(), _countSt);
                        }
                    }
                }

                // The click control, allocated LAST and always; a taken row is disabled, not skipped.
                GUI.enabled = outerEnabled && !taken;
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    int picked = nation;
                    _cursor = i;
                    fire = () => Pick(picked);
                }
                GUI.enabled = outerEnabled;
            }

            GUI.EndScrollView();

            if (_visible.Count == 0)
                UITheme.Hint(new Rect(lx, ly + lh * 0.5f - 12f, lw, 24f), "No nation matches \"" + (_appliedSearch ?? "") + "\"");
        }

        void DrawFooter(float w, float h, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            float hintY = h - 40f;
            float btnY = h - 82f;

            if (_variant == CupStyle.Solo)
            {
                if (UITheme.Button(new Rect(24f, btnY, 150f, 44f), "Back", _btnSt))
                    fire = () => OnBack?.Invoke();
                UITheme.Hint(new Rect(0f, hintY, w, 24f), "Click a nation to pick it   -   Arrows move   Enter pick   Esc back");
                return;
            }

            if (_director == null)
            {
                UITheme.Hint(new Rect(0f, hintY, w, 24f), "");
                return;
            }

            if (_variant == CupStyle.HeadToHead)
            {
                // Picking is the ready: name who is still to pick.
                var waiting = new List<CupPlayer>();
                var players = _director.Players;
                for (int i = 0; i < players.Count; i++) if (players[i].Active && !players[i].HasPicked) waiting.Add(players[i]);
                string line = waiting.Count == 0
                    ? "Everyone has picked"
                    : "Picking is your ready   -   " + CupText.WaitingForPlayers(CupUiKit.Names(waiting));
                UITheme.Hint(new Rect(0f, hintY, w, 24f), line);
                return;
            }

            // ---- Co-op: the vote gate and CAPTAIN DECIDES ----
            int mv;
            int majority = _director.MajorityNation(out mv);
            bool allPicked = _director.AllPicked;
            bool captainShows = _director.LocalIsCaptain && allPicked && majority < 0;

            if (majority >= 0)
            {
                _gateSt.normal.textColor = UITheme.Gold;
                UITheme.Label(new Rect(0f, hintY, w, 24f), CupText.MajorityReached + "   -   " + CupNations.Name(majority).ToUpperInvariant(), _gateSt);
            }
            else
            {
                int voted = 0, active = 0;
                var players = _director.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    if (!players[i].Active) continue;
                    active++;
                    if (players[i].HasPicked) voted++;
                }
                string line = allPicked
                    ? (captainShows ? "No majority - your call, Captain" : "No majority - " + CupText.CaptainIsChoosing)
                    : "Vote for the team's nation   -   " + voted + " of " + active + " voted, a majority decides";
                UITheme.Hint(new Rect(0f, hintY, w, 24f), line);
            }

            // Allocated every pass; parked off-screen and disabled when it does not apply, so the
            // control list is identical whether or not the Captain has a decision to make.
            var br = captainShows ? new Rect(w * 0.5f - 120f, btnY, 240f, 44f) : new Rect(-1000f, -1000f, 10f, 10f);
            GUI.enabled = outerEnabled && captainShows;
            var keep = GUI.backgroundColor;
            GUI.backgroundColor = UITheme.WarnTint;
            if (UITheme.Button(br, CupText.CaptainDecides, _bigBtnSt))
                fire = () => _director.CaptainDecides();
            GUI.backgroundColor = keep;
            GUI.enabled = outerEnabled;
        }

        static void EnsureStyles()
        {
            if (_nameSt != null) return;
            _nameSt = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = true, clipping = TextClipping.Clip };
            _takenSt = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _takenSt.normal.textColor = UITheme.Dim;
            _stripNameSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _countSt = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _countSt.normal.textColor = new Color(0.08f, 0.07f, 0.03f);
            _searchSt = new GUIStyle(GUI.skin.textField) { fontSize = 15 };
            _placeholderSt = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            _placeholderSt.normal.textColor = UITheme.Faint;
            _btnSt = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            _bigBtnSt = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            _gateSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
    }
}
