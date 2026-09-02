using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Top-down map for choosing where a cross lands (and, in SP, where the AI crosser stands).
    /// Shows the ENTIRE attacking half of the pitch: from the goal line (map top) back to the
    /// halfway line (map bottom), full touchline-to-touchline width. Every marking - goal mouth,
    /// six-yard + penalty boxes, penalty spot + arc, halfway line, centre circle, touchlines - is
    /// drawn at its REAL world coordinate, mapped through the SAME world<->map transform the click
    /// handler uses. That is what makes a click land exactly where it looks: the pixel you click and
    /// the marking you clicked on both resolve through one transform, so placing on the drawn penalty
    /// spot puts the target on the real penalty spot.
    ///
    /// The pitch is regulation size (105x68), so the attacking half is 52.5m deep x 68m wide. The x
    /// and z axes therefore map at different pixel scales; circles/arcs are sampled in WORLD space
    /// and mapped point-by-point so they render as the correct (slightly elliptical) shape rather
    /// than a naive screen circle. A live hover reticle follows the mouse; a click drops the marker.
    /// </summary>
    public static class CrossMap
    {
        // --- Attacking-half extent (world metres), read live so mutable goal size stays honest ---
        static float TopZ    => SimConfig.GoalCenter.z;      // map top edge = attacking goal line
        static float BottomZ => PitchLayout.PitchCenterZ;    // map bottom edge = halfway line
        static float HalfW   => PitchLayout.HalfWidth;       // touchline half-width (x = +/-HalfW)
        const float TargetY  = 0.25f;                        // world y stored for a placed target

        static readonly Color Grass     = new Color(0.17f, 0.44f, 0.20f, 0.98f);
        static readonly Color GrassAlt  = new Color(0.15f, 0.40f, 0.18f, 0.98f);
        static readonly Color Line      = new Color(0.95f, 0.97f, 0.95f, 0.9f);
        static readonly Color LineSoft   = new Color(1f, 1f, 1f, 0.55f);
        static readonly Color Gold      = new Color(1f, 0.85f, 0.25f);
        static readonly Color HoverCol  = new Color(0.55f, 0.9f, 1f);
        static readonly Color CrosserCol = new Color(0.4f, 0.7f, 1f);   // crosser placement icon

        // Regulation attacking-half markings (metres), matching PitchBuilder's painted pitch.
        const float PenaltyBoxHalfW = 20.15f, PenaltyBoxDepth = 16.5f;
        const float SixYardHalfW    = 9.15f,  SixYardDepth    = 5.5f;
        const float PenaltySpotDist = 11f;    // out from the goal line
        const float PenaltyArcRadius = 9.15f;
        const float CentreCircleRadius = 9.15f;

        // ---------------------------------------------------------------- shared AI-crosser overlay
        /// <summary>
        /// Everything the AI/auto crosser overlay (M) settles: where crosses land, where the crosser
        /// stands, how he delivers, and the two serve sliders. One struct so the whole panel is a
        /// single call, and it lives here rather than in each driver because single-player
        /// (GameManager) and multiplayer (NetStrikerMatch) must behave identically - two hand-copied
        /// IMGUI blocks is exactly how the two drift apart. The live instance is <see cref="Session"/>.
        /// </summary>
        public struct State
        {
            public int edit;           // 0 = cross target, 1 = crosser position
            public Vector3 target;     // where crosses land (world)
            public Vector3 spot;       // where the AI crosser stands (world)
            public Crosser.DeliveryType delivery;
            public float ballSpeed;    // -> SimConfig.BallSpeedMul
            public float crossInterval;// multiplier -> SimConfig.ServeInterval

            /// <summary>Defaults matching the pre-match baseline (1.00x sliders, lofted delivery).</summary>
            public static State Default => new State
            {
                target = SimConfig.ServeTarget,
                spot = SimConfig.CrosserStart,
                delivery = Crosser.DeliveryType.Lofted,
                ballSpeed = 1f,
                crossInterval = 1f,
            };

            /// <summary>
            /// Same SETTINGS? (`edit` is which tab is showing - local view state, not a setting, so
            /// switching tabs must not read as an edit and publish a packet.) Positions compare on
            /// x/z only, matching what the wire actually carries.
            /// </summary>
            public bool SameAs(in State o)
                => Mathf.Approximately(target.x, o.target.x) && Mathf.Approximately(target.z, o.target.z)
                && Mathf.Approximately(spot.x, o.spot.x)     && Mathf.Approximately(spot.z, o.spot.z)
                && delivery == o.delivery
                && Mathf.Approximately(ballSpeed, o.ballSpeed)
                && Mathf.Approximately(crossInterval, o.crossInterval);
        }

        // Base cross cadence the 1.00x "Cross interval" multiplier maps to. Same number PrematchUI
        // used when this slider lived there, kept in one place now that the slider moved here.
        public const float BaseServeInterval = 3.5f;

        /// <summary>
        /// The panel's settings, SHARED and persistent for the session (single-player and the
        /// networked driver each read this same one; only one striker match runs at a time).
        ///
        /// Static for the same reason PrematchUI's sliders are: these used to live on that screen,
        /// where they survived leaving and reopening it, and a Restart Match or a trip to the menu
        /// must not silently throw away the delivery, cadence and placement the player dialled in.
        /// Reset only by ResetSession (a genuinely new session), never by a rebuild.
        /// </summary>
        public static State Session = State.Default;

        /// <summary>Back to defaults - for a "reset all" style action, not for a match rebuild.</summary>
        public static void ResetSession() => Session = State.Default;

        // The delivery type as the player sees it (Ground / Low / High) and the order Q cycles it.
        public static string DeliveryName(Crosser.DeliveryType d) => d switch
        {
            Crosser.DeliveryType.Ground => "GROUND",
            Crosser.DeliveryType.Low    => "LOW",
            _                           => "HIGH",
        };
        public static Crosser.DeliveryType NextDelivery(Crosser.DeliveryType d) => d switch
        {
            Crosser.DeliveryType.Ground => Crosser.DeliveryType.Low,
            Crosser.DeliveryType.Low    => Crosser.DeliveryType.Lofted,
            _                           => Crosser.DeliveryType.Ground,
        };

        // ---- wire conversion (multiplayer) ----
        // The panel edits a State; the network carries a CrosserSetupMsg. The y components are not
        // sent (see CrosserSetupMsg): a target sits at the map's own TargetY and the crosser stands
        // on the ground, so both ends derive them rather than transmitting a number neither chose.

        public static Trickshot.Net.CrosserSetupMsg ToWire(in State s, string aiName)
            => new Trickshot.Net.CrosserSetupMsg
            {
                targetX = s.target.x, targetZ = s.target.z,
                spotX = s.spot.x,     spotZ = s.spot.z,
                delivery = (byte)s.delivery,
                ballSpeed = s.ballSpeed, crossInterval = s.crossInterval,
                aiName = aiName,
            };

        /// <summary>Adopt a replicated setup, preserving `edit` (which tab this viewer is on is
        /// local view state and must not be yanked around by someone else's packet).</summary>
        public static void FromWire(ref State s, in Trickshot.Net.CrosserSetupMsg c)
        {
            s.target = new Vector3(c.targetX, TargetY, c.targetZ);
            s.spot   = new Vector3(c.spotX, 0f, c.spotZ);
            s.delivery = (Crosser.DeliveryType)Mathf.Clamp(c.delivery, 0, 2);
            s.ballSpeed = c.ballSpeed;
            s.crossInterval = c.crossInterval;
        }

        // ---- Escape ownership ----
        // PauseMenu owns Escape globally and reacts to it in its OWN Update, independent of the
        // driver's - so Escape closing this map and PauseMenu opening on the very same press is a
        // real race, not a hypothetical (Update order between them is unspecified). Same fix as
        // QuickChatFeed.EscapeOwned: a flag PauseMenu checks to skip its own action, true while the
        // map is open AND for one extra frame after close, so PauseMenu still skips even if its
        // Update happens to run right after the frame the driver closed the map.
        //
        // Static (not per-driver) because only one cross map can be up at a time and PauseMenu asks
        // one question; single-player and the networked driver both report through here.
        static bool s_open;
        static int s_closedFrame = -10;
        // s_renaming counts too: Escape there backs out of the rename field, and PauseMenu must skip
        // that same press exactly as it skips the one that closes the map.
        public static bool EscapeOwned => s_open || s_renaming || (Time.frameCount - s_closedFrame) <= 1;

        /// <summary>Drivers call this whenever they open/close the panel.</summary>
        public static void NoteOpenState(bool open)
        {
            s_open = open;
            if (!open) s_closedFrame = Time.frameCount;
        }

        /// <summary>
        /// Push this state's live-tunable values at the world. Both the sliders and the delivery
        /// picker are read every serve by Crosser.PickServe/Launch (and BallSpeedMul by
        /// BallController.LaunchTo), so writing them is all it takes for a change to take effect on
        /// the very next cross - no rebuild. Call after any edit, and once at match start so a
        /// fresh match starts from the panel's own values rather than whatever a prior mode left in
        /// the statics.
        /// </summary>
        public static void Apply(in State s, Crosser crosser)
        {
            SimConfig.BallSpeedMul = s.ballSpeed;
            SimConfig.ServeInterval = BaseServeInterval * s.crossInterval;
            if (crosser == null) return;
            crosser.TargetOverride = s.target;
            crosser.Delivery = s.delivery;
        }

        /// <summary>
        /// Who is looking at the panel and what they may do with it. Single-player passes the
        /// default (everything editable, no dropdown); multiplayer fills it from the session.
        /// </summary>
        public struct Perms
        {
            /// <summary>Cross Target tab + the AI's delivery/speed/interval: editable by this viewer.
            /// True for every human while the AI crosses (it is where THEIR crosses arrive).</summary>
            public bool canEditTarget;
            /// <summary>Crosser Position: editable by this viewer. While the AI crosses, everyone
            /// (anyone may move it); while a human crosses, only that human, placing himself (and
            /// not mid-stance); single-player always.</summary>
            public bool canEditSpot;
            /// <summary>Show the AI controls at all (Cross Target tab, delivery, the two sliders).
            /// Only while the AI crosses - a human crosser aims his own, so they describe nothing.</summary>
            public bool aiControls;
            /// <summary>Host only: the crosser-assignment dropdown + the rename pencil.</summary>
            public bool isHost;
            /// <summary>True in a networked match (draws the crosser picker at all).</summary>
            public bool networked;
            /// <summary>Name of whoever crosses now: a player's name, or the AI's.</summary>
            public string crosserName;
            /// <summary>True when a HUMAN holds the crosser seat.</summary>
            public bool humanCrosser;
            /// <summary>True when THIS viewer holds the crosser seat.</summary>
            public bool isCrosser;
            /// <summary>Host: may the dropdown be used? True whenever it has something to offer:
            /// another human, or the AI while a human is crossing. A host alone in the lobby can
            /// still hand the seat to the AI (and then plays as a striker), but is not offered
            /// himself - crossing to nobody is not a choice worth listing.</summary>
            public bool dropdownEnabled;
            /// <summary>Humans that could be given the seat: (slot, name), the host included.</summary>
            public List<(int slot, string name)> candidates;
            /// <summary>The AI crosser's name, for the "hand it back to the AI" row.</summary>
            public string aiName;

            public static Perms SinglePlayer => new Perms { canEditTarget = true, canEditSpot = true, aiControls = true };

            public string aiNameOr() => string.IsNullOrWhiteSpace(aiName) ? "Clanker" : aiName;
        }

        // Dropdown + rename are modal-ish bits of local UI state; they belong to whoever has the
        // panel open, not to the replicated setup, so they live here rather than in State.
        static bool s_pickerOpen;
        static bool s_renaming;
        static string s_renameBuf = "";

        /// <summary>Close any transient sub-UI. Call when the panel closes so it never reopens onto
        /// a half-finished rename.</summary>
        public static void CancelTransientUI() { s_pickerOpen = false; s_renaming = false; }

        /// <summary>
        /// True while the rename field has the keyboard. The driver checks this before acting on
        /// Escape (which should back out of the FIELD, not close the whole map) and before reading
        /// M, so typing a name containing "m" cannot toggle the panel shut under the typist.
        /// </summary>
        public static bool Renaming => s_renaming;

        /// <summary>Back out of the rename field only. Returns false if it was not open.</summary>
        public static bool CancelRename()
        {
            if (!s_renaming) return false;
            s_renaming = false;
            // Stamp the close so EscapeOwned stays true for the frame after: PauseMenu polls the raw
            // key in its own Update and would otherwise open on the very press that closed this.
            s_closedFrame = Time.frameCount;
            return true;
        }

        /// <summary>
        /// What the panel wants done, handed back to the driver rather than performed here: this is
        /// a draw routine, and the actions it can request (re-plant the crosser, publish the edit to
        /// the session, reassign the seat) are all things only the caller knows how to do correctly.
        /// </summary>
        public struct Result
        {
            public bool spotMoved;      // re-plant: caller calls Crosser.SetOrigin
            public bool edited;         // any value changed: caller publishes it
            public int assignCrosser;   // -2 none, -1 hand the seat to the AI, >=0 give it to that slot
            public string rename;       // non-null: the AI's new name
        }

        /// <summary>
        /// The whole M overlay: tabs, the AI's delivery picker + two serve sliders, the map, the
        /// caption, and (in multiplayer) who is crossing - a dropdown for the host, a label for
        /// everyone else.
        ///
        /// Draw-only: it never reads the M key or opens/closes itself (that is Update's job, where
        /// the pause/Escape interlocks live), and it never writes the session or moves a body - it
        /// reports what was asked for in its Result and the caller does it.
        /// </summary>
        public static Result DrawOverlay(ref State s, Crosser crosser, in Perms p)
        {
            var res = new Result { assignCrosser = -2 };
            Hud.Scrim(0.45f);

            const float w = 380f, h = 300f;
            var mapRect = new Rect(Hud.W * 0.5f - w * 0.5f, Hud.H * 0.5f - h * 0.5f, w, h);

            // Which tabs exist. CROSS TARGET is where the AI's crosses land, so it only exists while
            // the AI crosses (any human may aim it - it is where their own crosses arrive). CROSSER
            // POSITION is where the crosser stands and always exists: anyone may move the AI, and a
            // human crosser places himself (canEditSpot says which applies to this viewer).
            bool targetTab = p.aiControls;
            bool spotTab   = true;
            if (targetTab && spotTab)
            {
                if (Hud.Seg(new Rect(mapRect.x, mapRect.y - 30f, w * 0.5f - 4f, 24f), "Cross Target", s.edit == 0)) s.edit = 0;
                if (Hud.Seg(new Rect(mapRect.x + w * 0.5f + 4f, mapRect.y - 30f, w * 0.5f - 4f, 24f), "Crosser Position", s.edit == 1)) s.edit = 1;
            }
            else if (targetTab) { Hud.Seg(new Rect(mapRect.x, mapRect.y - 30f, w, 24f), "Cross Target", true); s.edit = 0; }
            else if (spotTab)   { Hud.Seg(new Rect(mapRect.x, mapRect.y - 30f, w, 24f), "Crosser Position", true); s.edit = 1; }
            if (!targetTab && s.edit == 0) s.edit = 1;
            if (!spotTab && s.edit == 1) s.edit = 0;

            // The map itself: non-interactive when this viewer may not edit the marker this tab
            // places, so clicks do nothing and the hover reticle is gone. A wash says so at a glance.
            bool canEditThis = s.edit == 0 ? p.canEditTarget : p.canEditSpot;
            var before = s;
            res.spotMoved = Draw(mapRect, ref s.target, ref s.spot, interactive: canEditThis, editing: s.edit);
            if (!canEditThis) UITheme.Fill(mapRect, new Color(0.02f, 0.03f, 0.05f, 0.45f));

            float y = mapRect.yMax + 30f;

            // The AI's serve: delivery type + the two sliders. Only while the AI crosses.
            if (p.aiControls)
            {
                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && p.canEditTarget;   // greys the controls AND refuses their clicks

                // Delivery: one row, three exclusive options (the codebase has no dropdown widget;
                // a segmented row is the same single-select and matches the tabs right above).
                UITheme.Label(new Rect(mapRect.x, y, 70f, 24f), "Delivery", Hud.RowName);
                float dx = mapRect.x + 74f, dw = (w - 74f) / 3f;
                if (Hud.Seg(new Rect(dx, y, dw - 4f, 24f), "High", s.delivery == Crosser.DeliveryType.Lofted))
                    s.delivery = Crosser.DeliveryType.Lofted;
                if (Hud.Seg(new Rect(dx + dw, y, dw - 4f, 24f), "Low", s.delivery == Crosser.DeliveryType.Low))
                    s.delivery = Crosser.DeliveryType.Low;
                if (Hud.Seg(new Rect(dx + dw * 2f, y, dw - 4f, 24f), "Ground", s.delivery == Crosser.DeliveryType.Ground))
                    s.delivery = Crosser.DeliveryType.Ground;

                s.ballSpeed     = OverlaySlider(mapRect.x, y + 30f, w, "Cross speed",    s.ballSpeed,     0.5f, 2f);
                s.crossInterval = OverlaySlider(mapRect.x, y + 58f, w, "Cross interval", s.crossInterval, 0.4f, 2f);

                GUI.enabled = wasEnabled;
                y += 88f;
            }

            // Who is crossing (multiplayer only - single-player has nobody to pick from). Drawn for
            // EVERYONE so a client can see who has the seat; only the host's is a control.
            if (p.networked) DrawCrosserPicker(mapRect.x, y, w, p, ref res);

            // Did anything actually change this frame? Compared against the snapshot taken before
            // the controls. A control this viewer may not use is disabled or absent, so it cannot
            // have changed anything - any difference here was a permitted edit.
            res.edited = !s.SameAs(before);

            // Live-apply. Only where this peer is the one that SIMULATES the crosser: a client
            // writing SimConfig from a half-finished local edit would fight the host's replicated
            // truth.
            if (!p.networked || p.isHost) Apply(s, crosser);

            // No header over the map in the normal case (the tab above already says what a click
            // places). The one kept is for a viewer looking at somebody ELSE'S crossing spot, which
            // nothing else on screen explains.
            string header = p.humanCrosser && !p.isCrosser ? (p.crosserName ?? "A PLAYER") + " IS CROSSING" : null;
            string tip;
            if (s.edit == 1)
                tip = p.canEditSpot ? (p.isCrosser ? "Click to place yourself.  Enter to set up a cross."
                                                   : "Click to place the crosser.")
                    : p.isCrosser   ? "Finish the cross first."
                                    : "Only the crosser can move their spot.";
            else
                tip = p.canEditTarget ? "Click to place where crosses land." : "";
            Hud.OverlayLabel(mapRect, header, tip + "   M or Esc to close.", 60f);
            return res;
        }

        // "Crossing: <name>" plus, for the host, a dropdown of every human + the AI, and a pencil
        // that renames the AI. Drawn under the AI controls (or straight under the map when a human
        // crosses and there are none).
        static void DrawCrosserPicker(float x, float y, float w, in Perms p, ref Result res)
        {
            UITheme.Label(new Rect(x, y, 70f, 24f), "Crossing", Hud.RowName);

            float bx = x + 74f, bw = w - 74f;
            // The rename box replaces the row while it is up, so the two can't be used at once.
            if (s_renaming && p.isHost)
            {
                GUI.SetNextControlName("CrosserRename");
                s_renameBuf = GUI.TextField(new Rect(bx, y, bw - 128f, 24f), s_renameBuf ?? "",
                                            Trickshot.Net.NetSession.MaxCrosserNameLength);
                var e = Event.current;
                bool enter = e.type == EventType.KeyDown
                             && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
                if (UITheme.Button(new Rect(bx + bw - 124f, y, 60f, 24f), "Save", SmallBtn) || enter)
                {
                    res.rename = s_renameBuf;
                    CancelRename();          // closes the field AND stamps the Escape-ownership frame
                    if (enter) e.Use();
                }
                if (UITheme.Button(new Rect(bx + bw - 60f, y, 60f, 24f), "Cancel", SmallBtn))
                    CancelRename();
                return;
            }

            // A client (or any non-host) just reads who it is.
            if (!p.isHost)
            {
                UITheme.Label(new Rect(bx, y, bw, 24f), p.crosserName ?? "-", Hud.RowName);
                return;
            }

            // Host: the current pick opens a list of every human plus the AI. GREYED when it has
            // nothing to offer (dropdownEnabled): a control that changes nothing should look like one.
            if (!p.dropdownEnabled) s_pickerOpen = false;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && p.dropdownEnabled;
            float pencilW = p.humanCrosser ? 0f : 30f;   // the pencil renames the AI, so only when it holds the seat
            if (UITheme.Button(new Rect(bx, y, bw - pencilW - (pencilW > 0f ? 4f : 0f), 24f),
                               (p.crosserName ?? "-") + "   \u25be", SmallBtn))
                s_pickerOpen = !s_pickerOpen;
            GUI.enabled = wasEnabled;
            // The rename is the host's regardless of who else is here.
            if (pencilW > 0f && UITheme.Button(new Rect(bx + bw - pencilW, y, pencilW, 24f), "\u270e", SmallBtn))
            {
                s_renameBuf = p.crosserName ?? "";
                s_renaming = true;
                s_pickerOpen = false;
            }

            if (!s_pickerOpen) return;

            // The open list, drawn UPWARD from the row so it never runs off the bottom of the screen
            // (the row already sits below the map, near the lower edge). The AI is offered only when
            // it does NOT already have the seat; every human who does not have it is offered.
            int nCand = p.candidates?.Count ?? 0;
            int n = (p.humanCrosser ? 1 : 0) + nCand;
            if (n == 0) { s_pickerOpen = false; return; }
            const float itemH = 22f;
            float listH = n * itemH;
            float ly = y - 2f - listH;
            UITheme.Fill(new Rect(bx - 2f, ly - 2f, bw + 4f, listH + 4f), new Color(0.05f, 0.06f, 0.09f, 0.97f));

            int i = 0;
            if (p.humanCrosser)
            {
                if (UITheme.Button(new Rect(bx, ly + i * itemH, bw, itemH - 2f), p.aiNameOr(), SmallBtn))
                { res.assignCrosser = -1; s_pickerOpen = false; }
                i++;
            }
            if (p.candidates != null)
                foreach (var c in p.candidates)
                {
                    if (UITheme.Button(new Rect(bx, ly + i * itemH, bw, itemH - 2f), c.name, SmallBtn))
                    { res.assignCrosser = c.slot; s_pickerOpen = false; }
                    i++;
                }
        }

        // Compact button for the picker rows / rename box. Local rather than added to Hud: it is
        // this panel's own dressing, and Hud's public surface is the shared HUD vocabulary.
        static GUIStyle _smallBtn;
        static GUIStyle SmallBtn => _smallBtn ??= new GUIStyle(GUI.skin.button)
        { fontSize = 12, alignment = TextAnchor.MiddleCenter };

        // One labelled slider row sized for the overlay (the pre-match screen's own Slider() is
        // bound to that screen's row layout, so it cannot be reused here).
        static float OverlaySlider(float x, float y, float w, string label, float val, float min, float max)
        {
            UITheme.Label(new Rect(x, y, 140f, 22f), label, Hud.RowName);
            UITheme.Label(new Rect(x + w - 60f, y, 60f, 22f), val.ToString("0.00") + "x", Hud.RowValue);
            return GUI.HorizontalSlider(new Rect(x + 146f, y + 5f, w - 212f, 18f), val, min, max);
        }

        // Draw the map filling `rect`. Reads/writes `target` (world). Returns true if the
        // marker was moved this frame. `interactive` gates click handling + the hover reticle.
        // Target-only overload (callers that don't place a crosser).
        public static bool Draw(Rect rect, ref Vector3 target, bool interactive)
        {
            Vector3 dummy = Vector3.zero;
            return Draw(rect, ref target, ref dummy, interactive, editing: 0, showCrosser: false);
        }

        // Full overload: place the cross TARGET (editing 0) and/or the CROSSER spot (editing 1).
        // showCrosser draws + enables the crosser marker (skip it for a human crosser).
        public static bool Draw(Rect rect, ref Vector3 target, ref Vector3 crosserSpot,
                                bool interactive, int editing, bool showCrosser = true)
        {
            var prev = GUI.color;
            float t = Time.unscaledTime;

            // --- Pitch: mowed stripes (alternating horizontal bands). More bands for the deeper map. ---
            const int stripes = 11;
            float bandH = rect.height / stripes;
            for (int i = 0; i < stripes; i++)
            {
                GUI.color = (i & 1) == 0 ? Grass : GrassAlt;
                GUI.DrawTexture(new Rect(rect.x, rect.y + i * bandH, rect.width, bandH + 1f), Texture2D.whiteTexture);
            }

            // --- Outer frame = touchlines (sides) + goal line (top) + halfway line (bottom) ---
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            DrawRectOutline(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), 3f);
            GUI.color = Line;
            DrawRectOutline(rect, 2f);

            // --- Boxes (drawn at real world coords through WorldToMap) ---
            GUI.color = LineSoft;
            WBoxOutline(rect, -PenaltyBoxHalfW, PenaltyBoxHalfW, TopZ - PenaltyBoxDepth, TopZ, 1.5f);
            WBoxOutline(rect, -SixYardHalfW, SixYardHalfW, TopZ - SixYardDepth, TopZ, 1.5f);

            // --- Penalty spot + arc (arc bulges INTO the field = toward -Z = down the map) ---
            float spotZ = TopZ - PenaltySpotDist;
            var spotMap = WorldToMap(rect, new Vector3(0f, 0f, spotZ));
            GUI.color = Line;
            GUI.DrawTexture(new Rect(spotMap.x - 2f, spotMap.y - 2f, 4f, 4f), Texture2D.whiteTexture);
            // The arc is the part of the 9.15m circle around the spot beyond the box front line.
            float boxFrontZ = TopZ - PenaltyBoxDepth;
            float half = Mathf.Acos(Mathf.Clamp((PenaltyBoxDepth - PenaltySpotDist) / PenaltyArcRadius, -1f, 1f)) * Mathf.Rad2Deg;
            // World angle convention here: 0deg = +X, 90 = +Z (toward goal), 270 = -Z (into field).
            WorldArc(rect, 0f, spotZ, PenaltyArcRadius, 270f - half, 270f + half, 20, Line);

            // --- Centre circle at the halfway line (only the half inside the map is visible) ---
            WorldArc(rect, 0f, BottomZ, CentreCircleRadius, 0f, 180f, 28, LineSoft);
            var midMap = WorldToMap(rect, new Vector3(0f, 0f, BottomZ));
            GUI.color = Line;
            GUI.DrawTexture(new Rect(midMap.x - 2f, midMap.y - 2f, 4f, 4f), Texture2D.whiteTexture);

            // --- Goal mouth: bright bar + posts along the top edge, from live goal width ---
            var goalL = WorldToMap(rect, new Vector3(-SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            var goalR = WorldToMap(rect, new Vector3( SimConfig.GoalWidth * 0.5f, 0f, TopZ));
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(goalL.x, rect.y - 3f, goalR.x - goalL.x, 5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalL.x, rect.y, 3f, 8f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(goalR.x - 3f, rect.y, 3f, 8f), Texture2D.whiteTexture);
            GUI.color = prev;

            // --- Click to place whichever marker `editing` selects (0 = target, 1 = crosser) ---
            bool moved = false;
            Event e = Event.current;
            bool hovering = interactive && rect.Contains(e.mousePosition);
            if (hovering && e.type == EventType.MouseDown && e.button == 0)
            {
                Vector3 p = MapToWorld(rect, e.mousePosition);
                if (showCrosser && editing == 1) crosserSpot = p; else target = p;
                moved = true;
                e.Use();
            }

            // --- Placed target marker: gold pulsing reticle (smaller now) ---
            var mc = WorldToMap(rect, target);
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
            bool targetActive = !showCrosser || editing == 0;
            DrawReticle(mc, 7f + (targetActive ? pulse * 2.5f : 0f), Gold,
                        ringAlpha: targetActive ? 0.5f + 0.5f * pulse : 0.4f, filled: true);

            // --- Crosser marker: a small player icon (distinct blue), pulses when being edited ---
            if (showCrosser)
            {
                var cc = WorldToMap(rect, crosserSpot);
                bool crosserActive = editing == 1;
                DrawPlayerIcon(cc, CrosserCol, crosserActive ? 0.55f + 0.45f * pulse : 0.5f);
            }

            // --- Live hover reticle following the mouse (colour of the marker being placed) ---
            if (hovering && e.type == EventType.Repaint)
            {
                float hp = 0.5f + 0.5f * Mathf.Sin(t * 7f);
                Color hc = (showCrosser && editing == 1) ? CrosserCol : HoverCol;
                DrawReticle(e.mousePosition, 6f + hp * 2.5f, hc, ringAlpha: 0.35f + 0.4f * hp, filled: false);
            }

            GUI.color = prev;
            return moved;
        }

        // world <-> map helpers. x across the width (touchline to touchline), z from the goal line
        // (map top) back to the halfway line (map bottom). SINGLE source of truth for clicks + markings.
        static Vector3 MapToWorld(Rect rect, Vector2 m)
        {
            float fx = Mathf.Clamp01((m.x - rect.x) / rect.width);
            float fy = Mathf.Clamp01((m.y - rect.y) / rect.height);
            return new Vector3(Mathf.Lerp(-HalfW, HalfW, fx), TargetY,
                               Mathf.Lerp(TopZ, BottomZ, fy));
        }
        static Vector2 WorldToMap(Rect rect, Vector3 w)
        {
            float fx = Mathf.InverseLerp(-HalfW, HalfW, w.x);
            float fy = Mathf.InverseLerp(TopZ, BottomZ, w.z);
            return new Vector2(rect.x + fx * rect.width, rect.y + fy * rect.height);
        }

        // A world-axis-aligned box outline, mapped to the (anisotropic) screen rect.
        static void WBoxOutline(Rect rect, float minX, float maxX, float minZ, float maxZ, float th)
        {
            var a = WorldToMap(rect, new Vector3(minX, 0f, maxZ));   // near-left (toward goal = top)
            var b = WorldToMap(rect, new Vector3(maxX, 0f, minZ));   // far-right (toward halfway = bottom)
            var r = new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                             Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
            DrawRectOutline(r, th);
        }

        // A tiny stylised player: head dot + body, in the crosser colour.
        static void DrawPlayerIcon(Vector2 c, Color col, float alpha)
        {
            var prev = GUI.color;
            GUI.color = new Color(col.r, col.g, col.b, alpha);
            GUI.DrawTexture(new Rect(c.x - 2f, c.y - 6f, 4f, 4f), Texture2D.whiteTexture);   // head
            GUI.DrawTexture(new Rect(c.x - 3f, c.y - 1f, 6f, 7f), Texture2D.whiteTexture);   // body
            GUI.color = prev;
        }

        // A small circle with a crosshair straight THROUGH it - the same shape AimReticle uses on the
        // turf everywhere else now. The old version left a gap around the centre and put two of its
        // four ticks OUTSIDE the ring and two INSIDE it; this is one bar each axis, unbroken, so it
        // reads as a single reticle rather than four disconnected tick marks.
        static void DrawReticle(Vector2 c, float r, Color col, float ringAlpha, bool filled)
        {
            var prev = GUI.color;
            GUI.color = new Color(col.r, col.g, col.b, ringAlpha);
            DrawCircle(c, r, 2f, 24);
            GUI.color = col;
            float reach = r * 1.3f;   // overshoots the ring on both ends, same read as AimReticle's 3D one
            GUI.DrawTexture(new Rect(c.x - 1f, c.y - reach, 2f, reach * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(c.x - reach, c.y - 1f, reach * 2f, 2f), Texture2D.whiteTexture);
            // Centre dot.
            if (filled) { GUI.color = col; GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Texture2D.whiteTexture); }
            GUI.color = prev;
        }

        // Approximate a circle outline with short segment quads.
        static void DrawCircle(Vector2 c, float r, float thick, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                var p = new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r);
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
        }

        // A partial arc sampled in WORLD space (metres) and mapped point-by-point, so it renders with
        // the map's true x/z scale (an ellipse), and only the part inside the map is drawn. Angle: 0
        // = +X, 90 = +Z (toward goal), 270 = -Z (into the field, down the map).
        static void WorldArc(Rect rect, float cx, float cz, float radiusM, float fromDeg, float toDeg,
                             int segs, Color col)
        {
            var prev = GUI.color; GUI.color = col;
            const float thick = 1.5f;
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(fromDeg, toDeg, i / (float)segs);
                float wx = cx + Mathf.Cos(a) * radiusM;
                float wz = cz + Mathf.Sin(a) * radiusM;
                if (wz < BottomZ || wz > TopZ || wx < -HalfW || wx > HalfW) continue;   // clip to the half
                var p = WorldToMap(rect, new Vector3(wx, 0f, wz));
                GUI.DrawTexture(new Rect(p.x - thick * 0.5f, p.y - thick * 0.5f, thick, thick), Texture2D.whiteTexture);
            }
            GUI.color = prev;
        }

        static void DrawRectOutline(Rect r, float th)
        {
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, th), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - th, r.width, th), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, th, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - th, r.y, th, r.height), tex);
        }
    }
}
