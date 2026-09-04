using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// The Co-op shooting-order screen (design 6.8), shown before every stage.
    ///
    ///   Title "SHOOTING ORDER - ROUND OF 16"; the Captain's name with the "decides" tag, or
    ///   "Captain is choosing" for everyone else.
    ///   Slots: a horizontal row of N tall slots (140x190, gap 14): slot 0 is the KEEPER slot
    ///   (green frame, a blocky gloved keeper drawn in it), slots 1..N-1 are the numbered shooter
    ///   slots. A filled slot shows the player's chip (name, their slot colour, "1st" / "2nd"...);
    ///   an empty slot shows a dashed frame.
    ///   Bench: the unplaced players' chips (96x40) beneath. The Captain drags a chip into a slot;
    ///   dropping on an occupied slot swaps the two (the occupant takes the chip's old place - its
    ///   slot, or the bench); dropping outside every slot returns the chip to the bench.
    ///   Slot machine: a 60x220 lever plate at the left with a knob. Click it (or press Space) to
    ///   pull: the knob arcs down over CupTuning.LeverSeconds, every slot's face spins through the
    ///   roster names for CupTuning.ReelSpinSeconds, then the slots stop left to right
    ///   CupTuning.ReelStopGap apart, each landing on its assigned player. The permutation is the
    ///   HOST's (CupDirector.RollOrder, seeded), installed in CoopOrder and broadcast in CupState
    ///   BEFORE the reels animate, so every peer's reels land on the same faces.
    ///   Rules: exactly one keeper (the one keeper slot), every slot filled, nobody twice - the
    ///   model can only hold that (CupDirector.ApplyOrder). Ready for everyone, enabled once every
    ///   slot is filled; the director's gate is all filled and all ready.
    ///
    /// The screen only READS the director (Players, ActiveSlots, CoopOrder, LeverPulls,
    /// CaptainSlot, CoopKeeperLeft) and calls three intents: SetOrder(int[]) and PullLever() (the
    /// Captain's, host-local by construction) and SetReady(bool). Host and clients draw the same
    /// thing because both draw the director's model, which is the host's CupState on a client.
    ///
    /// IMGUI (the house rules): the drag is hand-rolled on Event.current (a MouseDown over a
    /// chip latches it, the chip follows the mouse on every event, a MouseUp drops it) and
    /// consumes the events it handles, so no control is ever conditionally allocated; the lever
    /// and Ready are real controls allocated every pass and disabled with GUI.enabled; the row
    /// model (team, order, bench) is rebuilt in Update, never in a GUI pass; every intent fires
    /// after MenuScale.End(). Esc is left to the pause menu (no modal of its own).
    /// </summary>
    public class CupOrderUI : MonoBehaviour
    {
        // ---- layout (design 6.8) -------------------------------------------------------------
        const float TitleY = 12f, TitleH = 60f;
        const float SlotW = 140f, SlotH = 190f, SlotGap = 14f;
        const float SlotMinW = 112f, SlotMinGap = 6f;   // eight slots plus the lever must fit the 1280 canvas
        const float ChipW = 96f, ChipH = 40f, ChipGap = 10f;
        const float LeverW = 60f, LeverH = 220f, LeverGap = 28f;
        const float SlotsY = 150f;
        const float ReelFaceStep = 0.07f;   // seconds per name while a reel spins
        const float KnobReturn = 0.45f;     // the knob eases back up after the pull
        const float LandPop = 0.22f;        // a landed face pops in over this long
        const float FooterH = 72f;

        public static CupOrderUI Create(Transform root, CupDirector director)
        {
            var go = new GameObject("CupOrderUI");
            if (root != null) go.transform.SetParent(root, false);
            var ui = go.AddComponent<CupOrderUI>();
            ui.Init(director);
            return ui;
        }

        /// <summary>The slot machine's reels are turning (drag and Ready are held off).</summary>
        public bool Spinning => _reelStart >= 0f && Time.unscaledTime - _reelStart < ReelTotal;
        /// <summary>A chip is being dragged by the Captain.</summary>
        public bool Dragging => _dragSlot >= 0;

        CupDirector _director;
        Action _draw;
        bool _hooked, _closed, _wasPaused;

        // The model, rebuilt in Update.
        readonly List<CupPlayer> _team = new List<CupPlayer>();   // active players, by slot
        int[] _order = new int[0];                                // one entry per slot, -1 empty
        readonly List<int> _bench = new List<int>();              // slots not in the order
        bool _complete;

        // The reel.
        int _pullsSeen = -1;
        float _reelStart = -1f;
        int _reelSlots;
        float ReelTotal => CupDirector.CoopReelSeconds(_reelSlots);

        // The drag.
        int _dragSlot = -1;      // the player slot on the chip
        int _dragFrom = -1;      // the order index it came from, -1 = the bench
        Vector2 _dragOffset;
        Vector2 _mouse;

        // Per-pass geometry (virtual coordinates), rebuilt every Draw.
        readonly List<Rect> _slotRects = new List<Rect>();
        readonly List<Rect> _benchRects = new List<Rect>();

        static GUIStyle _subSt, _slotHeadSt, _chipNameSt, _chipBigSt, _ordinalSt, _faceSt, _btnSt, _leverSt, _promptSt, _gateSt;

        void Init(CupDirector director)
        {
            _director = director;
            if (_director != null)
            {
                CupStatsLedger.Attach(_director);   // idempotent; the results table listens from the first whistle
                _pullsSeen = _director.LeverPulls;  // a pull from before this screen opened is not a reel
            }
            RebuildModel();
            GameInput.CaptureCursor(false);
            _draw = Draw;
            if (_director != null) { _director.AddGuiHook(_draw); _hooked = true; }
        }

        /// <summary>Unregister and destroy. Safe to call twice.</summary>
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
            _closed = true;
            if (_hooked && _director != null) _director.RemoveGuiHook(_draw);
            _hooked = false;
        }

        void Update()
        {
            // PauseMenu.Resume re-captures the cursor unconditionally; a menu screen wants it free.
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;
            if (paused) _dragSlot = -1;   // a drag cannot survive the menu (the click is the menu's now)

            if (_director == null) return;
            RebuildModel();

            // The lever: LeverPulls changed to a non-zero count = a pull landed (the host's own,
            // or the CupState echo of it); the order is already installed, so the reels can run.
            int pulls = _director.LeverPulls;
            if (pulls != _pullsSeen)
            {
                if (pulls > 0) StartReel();
                _pullsSeen = pulls;
            }

            // A drag whose MouseUp never reached IMGUI (the pointer left the window) is dropped
            // where it stands rather than following the mouse forever.
            if (Dragging)
            {
                var mouse = Mouse.current;
                if (mouse != null && !mouse.leftButton.isPressed) _dragSlot = -1;
                if (!_director.LocalIsCaptain || Spinning) _dragSlot = -1;
                // The team shrank under the drag (a leaver): the chip, or the slot it came from, may be gone.
                if (_dragFrom >= _order.Length || !IsTeam(_dragSlot)) _dragSlot = -1;
            }
        }

        // ---- the model -------------------------------------------------------------------------

        void RebuildModel()
        {
            _team.Clear();
            _bench.Clear();
            _complete = false;
            if (_director == null) { _order = new int[0]; return; }
            var players = _director.Players;
            for (int i = 0; i < players.Count; i++) if (players[i].Active) _team.Add(players[i]);

            // The director's order, normalised to one entry per team member: a shorter order is
            // padded with empty slots, a longer one (a leaver mid-collapse) trimmed - the screen
            // never shows a slot nobody can fill or hides one somebody could.
            int n = _team.Count;
            var src = _director.CoopOrder;
            if (_order.Length != n) _order = new int[n];
            for (int i = 0; i < n; i++)
            {
                int v = src != null && i < src.Length ? src[i] : -1;
                if (v >= 0 && !IsTeam(v)) v = -1;
                _order[i] = v;
            }
            for (int i = 0; i < _team.Count; i++)
                if (Array.IndexOf(_order, _team[i].Slot) < 0) _bench.Add(_team[i].Slot);
            _complete = n > 0 && _bench.Count == 0 && Array.IndexOf(_order, -1) < 0;
        }

        bool IsTeam(int slot)
        {
            for (int i = 0; i < _team.Count; i++) if (_team[i].Slot == slot) return true;
            return false;
        }

        CupPlayer Player(int slot) => _director != null ? _director.PlayerAt(slot) : null;

        void StartReel()
        {
            _reelStart = Time.unscaledTime;
            _reelSlots = Mathf.Max(1, _order.Length);
            _dragSlot = -1;
        }

        /// <summary>When slot i's reel stops (from the reel start).</summary>
        float StopAt(int i) => CupTuning.LeverSeconds + CupTuning.ReelSpinSeconds + CupTuning.ReelStopGap * i;

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
            Action fire = null;   // intents fire AFTER End; SetOrder / Ready never destroy this object, but the rule holds

            float w = MenuScale.Width, h = MenuScale.Height;
            bool paused = PauseMenu.Paused;
            var e = Event.current;
            var d = _director;
            bool captain = d != null && d.LocalIsCaptain;
            bool spinning = Spinning;
            float now = Time.unscaledTime;
            if (e != null) _mouse = e.mousePosition;

            UITheme.Scrim(w, h, 0.42f, 1100f);

            // ---- geometry: the slot row centred, the lever to its left ----
            int n = _order.Length;
            float slotW = SlotW, gap = SlotGap;
            float avail = w - 48f - LeverW - LeverGap;
            if (n > 0 && n * slotW + (n - 1) * gap > avail)
            {
                gap = SlotMinGap;
                slotW = Mathf.Max(SlotMinW, (avail - (n - 1) * gap) / n);
            }
            float rowW = n > 0 ? n * slotW + (n - 1) * gap : 0f;
            float blockW = rowW + LeverGap + LeverW;
            float x0 = w * 0.5f - blockW * 0.5f + LeverW + LeverGap;
            _slotRects.Clear();
            for (int i = 0; i < n; i++) _slotRects.Add(new Rect(x0 + i * (slotW + gap), SlotsY, slotW, SlotH));
            var leverRect = new Rect(x0 - LeverGap - LeverW, SlotsY + (SlotH - LeverH) * 0.5f, LeverW, LeverH);

            float benchY = SlotsY + SlotH + 34f;
            _benchRects.Clear();
            {
                int bn = _bench.Count;
                float bw = bn * ChipW + Mathf.Max(0, bn - 1) * ChipGap;
                float bx = w * 0.5f - bw * 0.5f;
                for (int i = 0; i < bn; i++) _benchRects.Add(new Rect(bx + i * (ChipW + ChipGap), benchY + 22f, ChipW, ChipH));
            }

            // ---- keys and the drag, BEFORE any control (they consume their events) ----
            bool spacePull = false;
            if (!paused && captain && !spinning && e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Space)
            {
                spacePull = true;
                e.Use();
            }
            if (captain && !paused && !spinning) HandleDrag(e, ref fire);
            else if (Dragging) _dragSlot = -1;

            GUI.enabled = !paused;

            // ---- title, the Captain line ----
            var stage = d != null ? d.Stage : CupStage.RoundOf32;
            UITheme.Title(new Rect(0f, TitleY, w, TitleH), CupText.OrderTitle(stage), 40, showRule: false);
            var cap = d != null ? d.Captain : null;
            string capName = cap != null ? cap.DisplayName : "Captain";
            _subSt.normal.textColor = captain ? UITheme.Gold : UITheme.Dim;
            UITheme.Label(new Rect(0f, TitleY + TitleH + 4f, w, 26f),
                          captain ? CupText.Decides(capName) + "   -   drag the chips, or pull the lever" : CupText.CaptainIsChoosing + "   -   " + capName,
                          _subSt);

            // ---- the keeper-left prompt (design 5: the Captain is prompted at the next order screen) ----
            if (d != null && d.CoopKeeperLeft)
            {
                var keeper = _order.Length > 0 && _order[0] >= 0 ? Player(_order[0]) : null;
                string line = captain
                    ? "Your keeper left" + (keeper != null ? " - " + keeper.DisplayName + " keeps unless you pick another" : " - pick a new keeper")
                    : "The keeper left" + (keeper != null ? " - " + keeper.DisplayName + " keeps for now" : "");
                float pulse = 0.75f + 0.25f * Mathf.Sin(now * 5f);
                _promptSt.normal.textColor = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, pulse);
                UITheme.Label(new Rect(0f, SlotsY - 34f, w, 24f), line, _promptSt);
            }

            // ---- the lever ----
            DrawLever(leverRect, captain, spinning, now);
            bool leverEnabled = !paused && captain && !spinning && !Dragging;
            GUI.enabled = leverEnabled;
            bool pulled = GUI.Button(leverRect, GUIContent.none, GUIStyle.none) && leverEnabled;
            GUI.enabled = !paused;
            if ((pulled || spacePull) && d != null) fire = () => d.PullLever();

            // ---- the slots ----
            for (int i = 0; i < n; i++) DrawSlot(i, _slotRects[i], spinning, now, captain);

            // ---- the bench ----
            UITheme.Section(new Rect(w * 0.5f - 200f, benchY - 4f, 400f, 18f), "BENCH");
            if (_bench.Count == 0)
                UITheme.Hint(new Rect(0f, benchY + 24f, w, ChipH), spinning ? "" : (_complete ? "Everyone has a slot" : ""));
            for (int i = 0; i < _bench.Count; i++)
            {
                int slot = _bench[i];
                bool ghost = Dragging && _dragSlot == slot;
                DrawChip(_benchRects[i], Player(slot), false, null, ghost ? 0.25f : 1f, captain && !spinning);
            }

            // ---- gate line + footer: Ready and the hint ----
            DrawFooter(w, h, d, captain, spinning, ref fire);

            // ---- the dragged chip, on top of everything ----
            if (Dragging)
            {
                var p = Player(_dragSlot);
                var r = new Rect(_mouse.x - _dragOffset.x, _mouse.y - _dragOffset.y, ChipW, ChipH);
                UITheme.Glow(new Rect(r.x - 10f, r.y - 8f, r.width + 20f, r.height + 16f), new Color(1f, 0.82f, 0.29f, 0.25f));
                DrawChip(r, p, false, null, 1f, true);
            }

            GUI.enabled = true;
            MenuScale.End();
            fire?.Invoke();
        }

        // ---- the drag (hand-rolled IMGUI: mouse events only, no controls) ----------------------

        void HandleDrag(Event e, ref Action fire)
        {
            if (e == null) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || Dragging) return;
                    // A chip on the bench, or a chip sitting in a slot.
                    for (int i = 0; i < _bench.Count; i++)
                    {
                        if (!_benchRects[i].Contains(e.mousePosition)) continue;
                        _dragSlot = _bench[i];
                        _dragFrom = -1;
                        _dragOffset = e.mousePosition - _benchRects[i].position;
                        e.Use();
                        return;
                    }
                    for (int i = 0; i < _order.Length; i++)
                    {
                        if (_order[i] < 0) continue;
                        var chip = SlotChipRect(_slotRects[i]);
                        if (!chip.Contains(e.mousePosition)) continue;
                        _dragSlot = _order[i];
                        _dragFrom = i;
                        _dragOffset = new Vector2(ChipW * 0.5f, ChipH * 0.5f);
                        e.Use();
                        return;
                    }
                    break;

                case EventType.MouseDrag:
                    if (Dragging) e.Use();
                    break;

                case EventType.MouseUp:
                    if (!Dragging) return;
                    {
                        int target = SlotIndexAt(e.mousePosition);
                        var next = Compose(target);
                        _dragSlot = -1;
                        _dragFrom = -1;
                        if (next != null)
                        {
                            var d = _director;
                            fire = () => d.SetOrder(next);
                        }
                        e.Use();
                    }
                    break;
            }
        }

        int SlotIndexAt(Vector2 p)
        {
            for (int i = 0; i < _slotRects.Count; i++) if (_slotRects[i].Contains(p)) return i;
            return -1;
        }

        /// <summary>
        /// The order after dropping the dragged chip on `target` (-1 = nowhere): a drop on an
        /// occupied slot swaps the two (the occupant takes the chip's old place - its slot, or the
        /// bench); a drop outside the slots returns a slotted chip to the bench. Null = no change.
        /// </summary>
        int[] Compose(int target)
        {
            if (_dragSlot < 0) return null;
            var o = (int[])_order.Clone();
            int from = _dragFrom;
            if (target < 0)
            {
                if (from < 0) return null;   // bench to nowhere
                o[from] = -1;
                return o;
            }
            if (target == from) return null;
            int occupant = o[target];
            o[target] = _dragSlot;
            if (from >= 0) o[from] = occupant;   // a bench origin: the occupant simply returns to the bench
            return o;
        }

        // ---- pieces ----------------------------------------------------------------------------

        static Rect SlotChipRect(Rect slot) => new Rect(slot.x + (slot.width - ChipW) * 0.5f, slot.y + 96f, ChipW, ChipH);

        void DrawSlot(int i, Rect r, bool spinning, float now, bool captain)
        {
            bool keeper = i == 0;
            int slot = _order[i];
            bool empty = slot < 0;
            bool ghost = Dragging && _dragFrom == i;
            bool hot = Dragging && r.Contains(_mouse);

            UITheme.Fill(r, CupUiKit.Well);
            if (hot) UITheme.Fill(r, CupUiKit.LitBand);
            var frame = keeper ? UITheme.Green : new Color(1f, 1f, 1f, 0.16f);
            if (empty && !spinning) DashedFrame(r, keeper ? frame : new Color(1f, 1f, 1f, 0.28f));
            else UITheme.FrameOutline(r, frame);
            if (keeper) UITheme.Fill(new Rect(r.x, r.y, r.width, 3f), UITheme.Green);

            // Header: KEEPER with the gloved figure, or the shooter's ordinal.
            _slotHeadSt.normal.textColor = keeper ? UITheme.Green : UITheme.Dim;
            UITheme.Label(new Rect(r.x, r.y + 8f, r.width, 22f), keeper ? "KEEPER" : Ordinal(i), _slotHeadSt);
            if (keeper) DrawKeeperFigure(r.center.x, r.y + 84f, 0.36f * (r.width / SlotW));
            else
            {
                _ordinalSt.normal.textColor = new Color(1f, 1f, 1f, 0.10f);
                UITheme.Label(new Rect(r.x, r.y + 30f, r.width, 56f), i.ToString(), _ordinalSt);
            }

            var chip = SlotChipRect(r);
            if (spinning)
            {
                // The reel: names cycle until this slot's stop time, then the assigned player
                // lands with a pop. The faces are display only; the order underneath is the host's.
                float t = now - _reelStart;
                float stop = StopAt(i);
                if (t < stop)
                {
                    int count = Mathf.Max(1, _team.Count);
                    int face = (Mathf.FloorToInt(t / ReelFaceStep) + i * 3) % count;
                    var p = _team[face];
                    float blur = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * 31f + i));
                    DrawFace(chip, p != null ? p.DisplayName : "?", blur);
                }
                else
                {
                    float pop = CupUiKit.EaseOutBack(Mathf.Clamp01((t - stop) / LandPop));
                    var p = Player(slot);
                    if (p != null) DrawChip(Scaled(chip, pop), p, true, keeper ? "GK" : Ordinal(i), 1f, false);
                    else DashedFrame(chip, new Color(1f, 1f, 1f, 0.2f));
                }
                return;
            }
            if (!empty)
            {
                var p = Player(slot);
                DrawChip(chip, p, true, keeper ? "GK" : Ordinal(i), ghost ? 0.25f : 1f, captain);
            }
            else
            {
                _faceSt.normal.textColor = new Color(1f, 1f, 1f, hot ? 0.6f : 0.25f);
                UITheme.Label(chip, hot ? "drop here" : "empty", _faceSt);
            }
        }

        static Rect Scaled(Rect r, float s)
        {
            s = Mathf.Max(0.01f, s);
            return new Rect(r.center.x - r.width * 0.5f * s, r.center.y - r.height * 0.5f * s, r.width * s, r.height * s);
        }

        /// <summary>A player's chip: their slot colour, the name (16 pt), a small ordinal tag on a slot, a green dot when they are ready.</summary>
        void DrawChip(Rect r, CupPlayer p, bool onSlot, string tag, float alpha, bool grabbable)
        {
            var col = p != null ? Hud.SlotColor(p.Slot) : UITheme.Dim;
            var body = new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.95f * alpha);
            var edge = new Color(col.r, col.g, col.b, 0.9f * alpha);
            UITheme.Chip(r, body, edge);
            CupUiKit.Spine(new Rect(r.x + 2f, r.y + 4f, 3f, r.height - 8f), edge);
            string name = p != null ? p.DisplayName : "?";
            var st = onSlot ? _chipBigSt : _chipNameSt;
            st.normal.textColor = new Color(UITheme.Ink.r, UITheme.Ink.g, UITheme.Ink.b, alpha);
            UITheme.Label(new Rect(r.x + 10f, r.y, r.width - 26f, r.height), name, st);
            if (!string.IsNullOrEmpty(tag))
            {
                _slotHeadSt.normal.textColor = new Color(col.r, col.g, col.b, alpha);
                UITheme.Label(new Rect(r.x, r.yMax + 2f, r.width, 18f), tag, _slotHeadSt);
            }
            if (p != null && p.Ready) UITheme.Disc(new Rect(r.xMax - 14f, r.y + 6f, 8f, 8f), new Color(UITheme.Green.r, UITheme.Green.g, UITheme.Green.b, alpha));
            if (grabbable && !Dragging && r.Contains(_mouse)) UITheme.FrameOutline(r, new Color(1f, 1f, 1f, 0.35f * alpha));
        }

        /// <summary>A spinning reel face: a plain plate with the name, dimmed as it blurs past.</summary>
        void DrawFace(Rect r, string name, float alpha)
        {
            UITheme.Chip(r, new Color(0.10f, 0.11f, 0.15f, 0.95f), new Color(1f, 1f, 1f, 0.25f));
            _faceSt.normal.textColor = new Color(UITheme.Ink.r, UITheme.Ink.g, UITheme.Ink.b, alpha);
            UITheme.Label(r, name, _faceSt);
        }

        /// <summary>The slot machine's lever: a plate with a track and a knob that arcs down on the pull and eases back.</summary>
        void DrawLever(Rect r, bool captain, bool spinning, float now)
        {
            UITheme.Fill(r, CupUiKit.Well);
            UITheme.FrameOutline(r, new Color(1f, 1f, 1f, captain ? 0.3f : 0.12f));
            float cx = r.center.x;
            float top = r.y + 26f, bottom = r.yMax - 46f;
            UITheme.Fill(new Rect(cx - 2f, top, 4f, bottom - top), new Color(1f, 1f, 1f, 0.18f));

            // The knob: top at rest; on a pull it arcs down over LeverSeconds and returns.
            float k = 0f, arc = 0f;
            if (_reelStart >= 0f)
            {
                float t = now - _reelStart;
                if (t < CupTuning.LeverSeconds) k = Mathf.SmoothStep(0f, 1f, t / CupTuning.LeverSeconds);
                else if (t < CupTuning.LeverSeconds + KnobReturn) k = 1f - Mathf.SmoothStep(0f, 1f, (t - CupTuning.LeverSeconds) / KnobReturn);
                arc = Mathf.Sin(k * Mathf.PI) * 9f;
            }
            float ky = Mathf.Lerp(top, bottom, k);
            var knobCol = captain && !spinning ? UITheme.Gold : UITheme.Dim;
            if (captain && !spinning && r.Contains(_mouse)) UITheme.Glow(new Rect(cx - 26f + arc, ky - 26f, 52f, 52f), new Color(1f, 0.82f, 0.29f, 0.35f));
            UITheme.Disc(new Rect(cx - 13f + arc, ky - 13f, 26f, 26f), new Color(0f, 0f, 0f, 0.6f));
            UITheme.Disc(new Rect(cx - 11f + arc, ky - 11f, 22f, 22f), knobCol);

            _leverSt.normal.textColor = captain ? (spinning ? UITheme.Dim : UITheme.Gold) : UITheme.Faint;
            UITheme.Label(new Rect(r.x - 20f, r.yMax - 40f, r.width + 40f, 20f), spinning ? "..." : "PULL", _leverSt);
            UITheme.Label(new Rect(r.x - 20f, r.yMax - 22f, r.width + 40f, 16f), captain ? "Space" : "", _leverSt);
        }

        /// <summary>The keeper slot's figure: the goal editor's blocky keeper in his ready stance, with gloves on the arm ends. `m` = metres to pixels.</summary>
        static void DrawKeeperFigure(float cx, float groundY, float m)
        {
            var pc = GUI.color;
            Color kit = new Color(0.30f, 0.80f, 0.48f, 0.95f);     // the keeper slot's green
            Color skin = new Color(0.85f, 0.68f, 0.55f, 0.95f);
            Color shorts = new Color(0.16f, 0.17f, 0.22f, 0.95f);
            Color glove = new Color(0.95f, 0.95f, 0.30f, 0.95f);
            m *= 100f;   // the editor's scale is pixels per metre; ours is a fraction of a slot width
            GUI.color = skin;
            GUI.DrawTexture(new Rect(cx - 0.12f * m, groundY - 1.80f * m, 0.24f * m, 0.24f * m), Texture2D.whiteTexture);
            GUI.color = kit;
            GUI.DrawTexture(new Rect(cx - 0.21f * m, groundY - 1.54f * m, 0.42f * m, 0.60f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 0.62f * m, groundY - 1.50f * m, 0.41f * m, 0.13f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 0.21f * m, groundY - 1.50f * m, 0.41f * m, 0.13f * m), Texture2D.whiteTexture);
            GUI.color = glove;
            GUI.DrawTexture(new Rect(cx - 0.74f * m, groundY - 1.56f * m, 0.14f * m, 0.22f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 0.60f * m, groundY - 1.56f * m, 0.14f * m, 0.22f * m), Texture2D.whiteTexture);
            GUI.color = shorts;
            GUI.DrawTexture(new Rect(cx - 0.21f * m, groundY - 0.96f * m, 0.42f * m, 0.24f * m), Texture2D.whiteTexture);
            GUI.color = skin;
            GUI.DrawTexture(new Rect(cx - 0.20f * m, groundY - 0.74f * m, 0.16f * m, 0.74f * m), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 0.04f * m, groundY - 0.74f * m, 0.16f * m, 0.74f * m), Texture2D.whiteTexture);
            GUI.color = pc;
        }

        /// <summary>A dashed frame (the empty slot / empty chip well): 8 px dashes, 5 px gaps, 2 px thick.</summary>
        static void DashedFrame(Rect r, Color c)
        {
            const float dash = 8f, gapPx = 5f, th = 2f;
            for (float x = r.x; x < r.xMax; x += dash + gapPx)
            {
                float wdt = Mathf.Min(dash, r.xMax - x);
                UITheme.Fill(new Rect(x, r.y, wdt, th), c);
                UITheme.Fill(new Rect(x, r.yMax - th, wdt, th), c);
            }
            for (float y = r.y; y < r.yMax; y += dash + gapPx)
            {
                float hgt = Mathf.Min(dash, r.yMax - y);
                UITheme.Fill(new Rect(r.x, y, th, hgt), c);
                UITheme.Fill(new Rect(r.xMax - th, y, th, hgt), c);
            }
        }

        void DrawFooter(float w, float h, CupDirector d, bool captain, bool spinning, ref Action fire)
        {
            bool outerEnabled = GUI.enabled;
            float footerY = h - FooterH;
            float gateY = footerY - 34f;
            const float bw = 170f, bh = 48f, edge = 24f;

            // The gate line: what everyone is waiting for.
            string gate;
            _gateSt.normal.textColor = UITheme.Faint;
            if (spinning) gate = "";
            else if (!_complete) gate = captain ? "Fill every slot - or pull the lever" : CupText.CaptainIsChoosing;
            else
            {
                var waiting = new List<CupPlayer>();
                for (int i = 0; i < _team.Count; i++) if (!_team[i].Ready) waiting.Add(_team[i]);
                if (waiting.Count == 0) { gate = "Everyone is ready"; _gateSt.normal.textColor = UITheme.Green; }
                else gate = CupText.WaitingForPlayers(CupUiKit.Names(waiting));
            }
            if (!string.IsNullOrEmpty(gate)) UITheme.Label(new Rect(0f, gateY, w, 24f), gate, _gateSt);

            // Ready (design 6.8: enabled once every slot is filled; the host's gate is all filled and all ready).
            var me = d != null ? d.LocalPlayer : null;
            bool ready = me != null && me.Ready;
            bool canToggle = d != null && me != null && _complete && !spinning;
            var rr = new Rect(w - edge - bw, footerY, bw, bh);
            GUI.enabled = outerEnabled && canToggle;
            bool hit;
            if (ready) hit = UITheme.Toggle(rr, "READY", true, _btnSt, UITheme.GoodTint);
            else
            {
                var keep = GUI.backgroundColor;
                GUI.backgroundColor = UITheme.GoodTint;
                hit = UITheme.Button(rr, CupText.Ready, _btnSt);
                GUI.backgroundColor = keep;
            }
            GUI.enabled = outerEnabled;
            if (hit)
            {
                bool next = !ready;
                fire = () => d.SetReady(next);
            }

            // The rules and the controls, in one line.
            string hint = captain
                ? "One keeper, every slot filled, nobody twice   -   drop on a taken slot to swap   -   Space pulls the lever"
                : "One keeper, every slot filled, nobody twice   -   Ready once the order is set";
            UITheme.Hint(new Rect(0f, h - 40f, w - bw - edge * 2f, 24f), hint);
        }

        static string Ordinal(int i)
        {
            switch (i)
            {
                case 1: return "1st";
                case 2: return "2nd";
                case 3: return "3rd";
                default: return i + "th";
            }
        }

        static void EnsureStyles()
        {
            if (_subSt != null) return;
            _subSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _slotHeadSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _chipNameSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _chipBigSt = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _ordinalSt = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            UIFont.Heavy(_ordinalSt);
            _ordinalSt.fontSize = 48;
            _faceSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            _btnSt = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            _leverSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _promptSt = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            _gateSt = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
    }
}
