using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Host setup: the host picks the configuration for the mode chosen on the Multiplayer hub
    /// (team size, match length, visibility, the dead-ball knobs...) then Creates the session. On
    /// create it starts hosting, pushes the config to the session, and hands off to the lobby.
    /// Joiners inherit this config (host-authoritative), so they don't re-pick it.
    /// </summary>
    public class HostSetupUI : MonoBehaviour
    {
        System.Action _onCreated, _onBack;

        // The mode this screen is locked to. There is no mode picker here any more: the hub
        // offers every networkable mode as its own button (MultiplayerHubUI.NetModes), so by the
        // time this screen opens the choice is made, and one option is not a choice.
        GameMode _gameMode;
        int _stadium;
        int _perSide = 3;          // match team size (1/3/5) - see the Team size picker
        // Match only: extra roles this host wants a stranger to drop into, as a LookingRole
        // bitmask. Advertised through ModeLabel so the session browser can filter on it; the roles
        // are not hooked up to gameplay yet, so this is discovery only.
        byte _lookingFor;
        int _matchMin = 3;         // match length (min)
        bool _publicLobby = true;
        // Set-pieces host settings (goal size %, keeper ability). Ball/player speed intentionally
        // NOT exposed - kept fixed so multiplayer stays balanced.
        int _goalPct = 100;        // 80 / 100 / 125 (the picker modes; accuracy uses the graphic below)
        // ACCURACY uses the same drag-to-size goal picture single player does, so its goal is two
        // free metre values rather than one of three presets. Static, so reopening the screen keeps
        // what was dialled.
        static float _accGoalW = SimConfig.GoalWidthBase, _accGoalH = SimConfig.GoalHeightBase;
        readonly GoalEditor _goalEditor = new GoalEditor();
        // Accuracy places its practice-style free kick map beside the panel, exactly as SP does.
        static bool _accFkInit;
        static Vector3 _accFkBall, _accFkWall;
        static int _accFkEdit;
        static bool _accFkRandom;
        // AI keeper strength, if no human takes the gloves. One of KeeperPcts (the SimConfig ladder
        // x100); 30 = Normal. The default has to be ON the ladder or the picker opens with no button
        // lit, which is exactly what an older 50 did.
        int _keeperPct = 30;   // Normal (see KeeperPcts)
        // Host-placed free-kick spot + wall (world x/z). Lazily defaulted the first frame the
        // Set Pieces map is shown (centre spot at FreeKickDistance, wall at WallDistance toward
        // goal). _fkEdit selects which marker a map click moves: 0 = ball, 1 = wall.
        bool _fkInit; Vector3 _fkBall, _fkWall; int _fkEdit;
        // RANDOM set-piece spots: when on, all shooters shoot from a fresh random outside-box spot
        // each of the 10 rounds (same spot per round for everyone). A seed generated at Create time is
        // carried in MatchConfig so every peer derives the identical 10-spot schedule.
        bool _fkRandom;
        // Accuracy host settings: an optional wall, how many targets are up, and how each
        // shooter's turn ENDS - either a fixed number of kicks (1..100) or a per-turn timer
        // (up to 120 s). Goal size / keeper ability reuse the set-piece pickers above, and the
        // ball/wall placement reuses the same free-kick map.
        bool _accSuddenDeath;      // false = strikes (a run each), true = one shot per visit
        bool _accNoKeeper;         // true = open goal for the whole match
        // Trickshot Cup host settings (design 6.9): the play STYLE (Head to Head / Co-op - Solo
        // is single player and never hosted) and the FORMAT. That is the whole of the cup's
        // configuration: regulation goal, the stage ramp owns the keeper, always 32 nations.
        // Static, like the accuracy goal above, so reopening the screen keeps what was dialled.
        static CupStyle _cupStyle = CupStyle.HeadToHead;
        static CupFormat _cupFormat = CupFormat.Penalties;
        static readonly string[] CupStyleNames = { CupText.StyleName(CupStyle.HeadToHead), CupText.StyleName(CupStyle.Coop) };
        static readonly string[] CupFormatNames = { CupText.FormatName(CupFormat.Penalties), CupText.FormatName(CupFormat.FreeKicks) };
        string _hostError = "";    // shown when Create couldn't open the host port

        public void Init(System.Action onCreated, System.Action onBack, GameMode mode)
        {
            _onCreated = onCreated; _onBack = onBack;
            _gameMode = mode;
            _stadium = StadiumStyle.SelectedIndex;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            // Scale the whole setup panel up on big displays (see MenuScale); sizes below are
            // unchanged, they just fill more of the screen.
            MenuScale.Begin();
            var mode = _gameMode;
            // Accuracy draws the GOAL PICTURE instead of a goal-size picker, so it is sized the way
            // the single-player setup screens are (SetupPanel.Height): the picture, plus its two
            // remaining rows (format + the public-lobby toggle). Match adds the Looking-for row
            // (+58). -116 on the others: neither a mode row (the mode was chosen on the hub) nor a
            // stadium row (picked on its own screen after Create).
            // The cup is sized the same way: the goal picture, then three rows' worth of content
            // (two LadderPicker rows at SetupPanel.RowH, the blurb line under the first, and the
            // public-lobby toggle - 104 + 26 + 40 = 170 < 3 * 52 + the head band's slack).
            float w = 480f;
            float panelH = mode == GameMode.Accuracy
                         ? SetupPanel.Height(2)
                         : mode == GameMode.TrickshotCup
                         ? SetupPanel.Height(3)
                         : 470f + (mode == GameMode.Match ? 58f : 0f) - 116f;
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 640f);
            UITheme.Panel(new Rect(x, y, w, panelH), UITheme.Blue);

            UITheme.Title(new Rect(x, y + 12f, w, 36f), PauseMenu.ModeName(mode).ToUpper() + " - HOST SETUP", 28);

            float lx = x + 30f, lw = w - 60f, row = y + 60f;
            UITheme.Divider(lx, row - 8f, lw);
            // (The stadium is picked on its own screen after Create - the same one single player
            // uses - so the venue row that used to sit here is gone.)
            if (mode == GameMode.Match)
            {
                // ONLY SEATABLE SIZES. Match maps two teams onto the 8-slot board, so a team is
                // capped at NetSession.ScrimSlotsPerTeam (4) - and NetSession.ScrimPerSide clamps to
                // exactly that. The old 5v5/11v11 entries were silently clamped to 4 as well: they
                // opened a lobby with four slots per side while calling itself eleven a side. Offering
                // only what the board can seat is what stops the picker promising a size the lobby
                // then refuses.
                //
                // Values are roster sizes INCLUDING the keeper (shirt 0), the same "N a side"
                // convention the pitch sizing uses - so 2 is a keeper + 1 outfielder. perSide 1 would
                // be a keeper and NOBODY, the shirt invariant ScrimPerSide and Footballer clamp away.
                PickerVals(lx, ref row, lw, "Team size", new[] { "2 v 2", "3 v 3", "4 v 4" }, new[] { 2, 3, 4 }, ref _perSide);
                PickerVals(lx, ref row, lw, "Match length", new[] { "2 min", "3 min", "5 min", "10 min" }, new[] { 2, 3, 5, 10 }, ref _matchMin);
                LookingForRow(lx, ref row, lw);
            }
            else if (mode == GameMode.SetPieces)
            {
                // Balance-safe knobs only: goal size + AI keeper strength.
                PickerVals(lx, ref row, lw, "Goal size", new[] { "Small", "Normal", "Big" }, new[] { 80, 100, 125 }, ref _goalPct);
                PickerVals(lx, ref row, lw, "Keeper", KeeperNames, KeeperPcts, ref _keeperPct);
            }
            else if (mode == GameMode.Accuracy)
            {
                // The SP PRACTICE format (see SetupPanel): the goal as a picture you drag to size,
                // the keeper as a plain Yes/No under it - its STRENGTH is the round ladder's, not a
                // setting - then the format, and the placement map beside the panel.
                int kLvl = _accNoKeeper ? 0 : GoalEditor.YesLevel;
                SetupPanel.GoalRow(_goalEditor, x, ref row, ref _accGoalW, ref _accGoalH, ref kLvl,
                                   locked: false, yesNo: true);
                _accNoKeeper = kLvl <= 0;

                int fmt = _accSuddenDeath ? 1 : 0;
                PickerVals(lx, ref row, lw, "Format", new[] { "Strikes", "Sudden Death" }, new[] { 0, 1 }, ref fmt);
                _accSuddenDeath = fmt == 1;

                if (!_accFkInit) { SetPieceMap.DefaultPlacement(out _accFkBall, out _accFkWall); _accFkInit = true; }
                SetupPanel.Map(x, y, ref _accFkBall, ref _accFkWall, ref _accFkEdit, ref _accFkRandom,
                               "Random spot each round.", showWall: false);
            }
            else if (mode == GameMode.TrickshotCup)
            {
                // Design 6.9: the goal picture LOCKED at regulation with NO keeper row (the stage
                // ramp owns the keeper - CupTuning.KeeperAbilityByStage), then Play style with a
                // one-line blurb under the selected one, Format, and the public-lobby toggle
                // below. No map, no sliders, no field-size picker: the field is always 32.
                // The picture's values are throwaways - locked means regulation whatever it is
                // handed, and the level only decides that the figure stands beside the post.
                float gw = SimConfig.GoalWidthBase, gh = SimConfig.GoalHeightBase;
                int kLvl = GoalEditor.YesLevel;
                _goalEditor.Draw(new Rect(x + 30f, row + 4f, w - 60f, GoalEditor.ContentH), ref gw, ref gh, ref kLvl,
                                 framed: false, locked: true, keeperRow: GoalEditor.KeeperRow.None);
                row += GoalEditor.ContentH + 8f;

                int style = PrematchUI.LadderPicker(lx, ref row, lw, CupText.PlayStyle + ":", CupStyleNames,
                                                    _cupStyle == CupStyle.Coop ? 1 : 0);
                _cupStyle = style == 1 ? CupStyle.Coop : CupStyle.HeadToHead;
                UITheme.Hint(new Rect(lx, row - 2f, lw, 20f), CupText.StyleBlurb(_cupStyle), TextAnchor.MiddleLeft);
                row += 26f;

                int fmt = PrematchUI.LadderPicker(lx, ref row, lw, CupText.Format + ":", CupFormatNames,
                                                  _cupFormat == CupFormat.FreeKicks ? 1 : 0);
                _cupFormat = fmt == 1 ? CupFormat.FreeKicks : CupFormat.Penalties;
            }
            // This toggle now DOES something. It used to be carried on the wire and ignored; it decides
            // whether the host answers discovery probes at all, so off means the lobby appears in
            // nobody's Find a Session list and the invite code is the only way in. Labelled by that
            // effect rather than by the old "anyone can join", which was never true either way - the
            // invite code always worked.
            Toggle(lx, ref row, lw, "List in Find a Session", ref _publicLobby);
            // (Striker AI is chosen per-slot in the lobby now, not here.)

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            // Back/Create normally sit 100px above the screen bottom, but the Accuracy panel is
            // much taller (610 vs 470) and its bottom edge reaches past that row, so the buttons
            // overlapped its content. Push the row BELOW the panel whenever the panel extends
            // lower, clamped so it can never run off the bottom of the screen.
            float by = Mathf.Min(MenuScale.Height - 52f,
                                 Mathf.Max(MenuScale.Height - 100f, y + panelH + 14f));
            if (UITheme.Button(new Rect(x + 30f, by, 160f, 44f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            var keep = GUI.backgroundColor; GUI.backgroundColor = UITheme.GoodTint;
            bool create = UITheme.Button(new Rect(x + w - 190f, by, 160f, 44f), "Create", btn);
            GUI.backgroundColor = keep;
            if (create) Create();

            // Why Create failed (port already in use), instead of silently hosting nothing.
            if (!string.IsNullOrEmpty(_hostError))
            {
                var err = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter,
                                                        normal = { textColor = UITheme.Red } };
                UITheme.Chip(new Rect(x + 24f, by - 50f, w - 48f, 44f), new Color(0.22f, 0.07f, 0.07f, 0.9f), UITheme.Red);
                UITheme.Label(new Rect(x + 24f, by - 46f, w - 48f, 40f), _hostError, err);
            }

            // Free-kick placement map (SET PIECES only): a side panel to the right of the main
            // window where the host drops the ball spot + wall, like the in-match cross map.
            // ACCURACY draws its OWN map inside its branch above - it has no wall, so it needs the
            // wall-less variant, and drawing this one too stacked a second map with a WALL marker
            // straight on top of it.
            if (mode == GameMode.SetPieces)
                DrawFreeKickSetup(x + w + 16f, y);

            MenuScale.End();
        }

        void DrawFreeKickSetup(float px, float py)
        {
            // Default just outside the box (free kicks are taken from outside it).
            if (!_fkInit) { SetPieceMap.DefaultPlacement(out _fkBall, out _fkWall); _fkInit = true; }

            // The panel itself lives in SetPieceMap so single player's pre-match screen draws the
            // identical control.
            SetPieceMap.DrawSetupPanel(px, py, 300f, 300f, ref _fkBall, ref _fkWall, ref _fkEdit, ref _fkRandom,
                                       "Random spot each round.");
        }

        void Create()
        {
            var mode = _gameMode;
            StadiumStyle.SelectedIndex = _stadium;
            // Both dead-ball modes (set pieces + accuracy) use the goal/keeper/placement knobs.
            bool deadBall = mode == GameMode.SetPieces || mode == GameMode.Accuracy;
            bool cup = mode == GameMode.TrickshotCup;

            // Match is two teams mapped onto the 8 slots (capped 4-a-side incl keepers), so
            // both sides can be human: allow up to 2*perSide (bounded to the 8-slot board).
            // The cup seats all eight as entrants (NetSession.SlotAllowed), and so does every
            // single-goal mode by default.
            int maxPlayers = mode == GameMode.Match ? Mathf.Clamp(_perSide * 2, 2, 8) : 8;
            Multiplayer.Host(maxPlayers);
            // Hosting can FAIL to bind UDP 7777 (another copy of the game still holding it, or an
            // orphaned session from earlier in this run). The transport logs and carries on with
            // IsRunning=false, which used to sail straight into a normal-looking lobby: nobody could
            // ever join, START MATCH built the SINGLE-PLAYER mode, and the host played alone without
            // ever being told. Detect it here and stay on this screen with the reason.
            if (Multiplayer.Session == null || !Multiplayer.Session.Active)
            {
                Multiplayer.End();
                _hostError = "Couldn't open port " + NetEndpoint.DefaultPort + ". Close the other copy.";
                return;
            }
            _hostError = "";
            Multiplayer.Session.SetConfig(new MatchConfig
            {
                mode = (byte)mode,
                stadium = (byte)_stadium,
                perSide = (byte)_perSide,
                matchSec = (ushort)(_matchMin * 60),
                publicLobby = _publicLobby,
                // Set-pieces + accuracy share these knobs (both are dead-ball modes).
                // Accuracy sizes its goal on the picture, which gives width and height separately;
                // the other dead-ball modes use the one three-step picker for both.
                goalScale  = mode == GameMode.Accuracy ? _accGoalW / SimConfig.GoalWidthBase
                           : deadBall ? _goalPct / 100f : 1f,
                goalScaleH = mode == GameMode.Accuracy ? _accGoalH / SimConfig.GoalHeightBase
                           : deadBall ? _goalPct / 100f : 1f,
                // Striker's goal + keeper are set on the stadium/goal screen that follows; this is
                // just the starting point it opens on (Normal).
                // Accuracy overloads this: -1 means NO KEEPER, and any other value is ignored (its
                // keeper comes from the round ladder). Every other mode sends a real 0..1 ability.
                keeperAbility = mode == GameMode.Accuracy ? (_accNoKeeper ? -1f : 0f)
                              : deadBall ? _keeperPct / 100f
                              : SimConfig.AiLevelAbility[(int)SimConfig.AiDifficulty.Normal],
                // Host-placed free-kick spot + wall. fkPlaced tells the driver to honour them;
                // when false (map never opened / other modes) the driver uses its own default.
                fkPlaced = deadBall && _fkInit,
                fkBallX = _fkBall.x, fkBallZ = _fkBall.z,
                fkWallX = _fkWall.x, fkWallZ = _fkWall.z,
                // Random per-round spots: carry the flag + a fresh seed so every peer derives the
                // same schedule. The seed also drives the accuracy target layout.
                fkRandom = mode == GameMode.SetPieces && _fkRandom,
                fkSeed = (uint)Random.Range(1, int.MaxValue),
                // Accuracy: format, plus the keeper carried as a NEGATIVE ability (see the
                // keeperAbility line above) - the mode's real ability comes from the round ladder,
                // so the field is free to mean "none at all" here rather than needing a new one.
                accSuddenDeath = mode == GameMode.Accuracy && _accSuddenDeath,
                // Match only: the roles this host is advertising for (see LookingRole).
                lookingFor = mode == GameMode.Match ? _lookingFor : (byte)0,
                // Trickshot Cup: the style (never Solo from here - 1 = Head to Head, 2 = Co-op)
                // and the format. Its seed is the fresh fkSeed above; the goal is regulation by
                // construction (goalScale 1 falls out of `deadBall` being false) and the keeper is
                // the stage ramp's, so neither is read from this config by the cup.
                cupStyle = cup ? (byte)_cupStyle : (byte)0,
                cupFormat = cup ? (byte)_cupFormat : (byte)0,
            });

            enabled = false;
            _onCreated?.Invoke();
        }

        // ---- small pickers ----
        // One label style and one button style for every row, so the list reads as a table.
        static GUIStyle _rowLbl;
        static GUIStyle RowLabel()
        {
            _rowLbl ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _rowLbl.normal.textColor = UITheme.Ink;
            return _rowLbl;
        }
        static GUIStyle PickStyle(bool sel)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
            if (sel) s.normal.textColor = UITheme.Gold;
            return s;
        }

        // Keeper difficulty, as whole percents of SimConfig.KeeperAbility. Same five names and the
        // same five levels the single-player pre-match screen offers (PrematchUI.KeeperNames), so
        // "Hard" means one thing across the game instead of the old Low/Med/High meaning something
        // else again. None builds no goalkeeper at all.
        static readonly string[] KeeperNames = { "None", "Easy", "Normal", "Hard", "Insane" };
        // The SAME ladder as SimConfig.AiLevelAbility (x100), not a separate 0/25/50/75/100 - the
        // comment above promised "Hard means one thing across the game" and the numbers did not.
        static readonly int[]    KeeperPcts  = { 0, 15, 30, 55, 80 };

        void PickerVals(float lx, ref float row, float lw, string label, string[] names, int[] vals, ref int val)
        {
            UITheme.Label(new Rect(lx, row, lw, 20f), label + ":", RowLabel());
            float bw = (lw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = vals[i] == val;
                if (UITheme.Toggle(new Rect(lx + i * (bw + 6f), row + 22f, bw, 28f), names[i], sel, PickStyle(sel)))
                    val = vals[i];
            }
            UITheme.Divider(lx, row + 53f, lw);
            row += 58f;
        }

        // Match only: "Looking for" - Sniper / Referee / Cameraman as independent checkboxes on
        // one row, so a host can advertise wanting any combination. Drawn as its own row rather
        // than through Toggle() because all three share a single label and sit side by side.
        void LookingForRow(float lx, ref float row, float lw)
        {
            UITheme.Label(new Rect(lx, row, lw, 20f), "Looking for:", RowLabel());
            var names = LookingRoles.Names;
            float bw = (lw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                byte bit = (byte)LookingRoles.All[i];
                bool on = (_lookingFor & bit) != 0;
                if (UITheme.Toggle(new Rect(lx + i * (bw + 6f), row + 22f, bw, 28f), names[i], on, PickStyle(on)))
                    _lookingFor ^= bit;   // independent checkboxes, not a one-of-N picker
            }
            UITheme.Divider(lx, row + 53f, lw);
            row += 58f;
        }

        void Toggle(float lx, ref float row, float lw, string label, ref bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            st.normal.textColor = UITheme.Ink;
            st.onNormal.textColor = st.onHover.textColor =
                st.onActive.textColor = st.onFocused.textColor = UITheme.Gold;
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "", st);
            UITheme.Label(new Rect(lx, row + 6f, lw, 26f), "  " + label, st);
            row += 40f;
        }

    }
}
