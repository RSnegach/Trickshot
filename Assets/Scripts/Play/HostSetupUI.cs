using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Host setup: the host picks the match configuration (mode, stadium, team size, match
    /// length, visibility) then Creates the session. On create it starts hosting, pushes the
    /// config to the session, and hands off to the lobby. Joiners inherit this config
    /// (host-authoritative), so they don't re-pick it.
    /// </summary>
    public class HostSetupUI : MonoBehaviour
    {
        System.Action _onCreated, _onBack;

        // Networkable modes. Match is reached ONLY pre-locked now (via Match -> Friendlies -> Host
        // a Session) - the generic picker (reached via Other Modes) never offers it, so the two
        // arrays are built per-Init rather than being one static list every path shares.
        GameMode[] Modes;
        string[] ModeNames;
        bool _modeLocked;          // true when Modes has exactly one entry - skip drawing the picker
        int _mode;                 // index into Modes
        int _stadium;
        int _perSide = 3;          // match team size (3/5/11)
        int _matchMin = 3;         // match length (min)
        bool _publicLobby = true;
        // Set-pieces host settings (goal size %, keeper ability). Ball/player speed intentionally
        // NOT exposed - kept fixed so multiplayer stays balanced.
        int _goalPct = 100;        // 80 / 100 / 125
        // AI keeper strength, if no human takes the gloves. One of KeeperPcts; 50 = Normal.
        // Worth noting that 50 was NOT one of the old 0/30/60/90 steps, so this picker used to open
        // with no button lit at all - the five named steps include it, so it now shows Normal.
        int _keeperPct = 50;
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
        int _accWall = 0;          // wall players (0 = no wall)
        int _accTargets = 4;       // targets up at once
        bool _accByTime;           // false = fixed kicks, true = per-turn timer
        int _accKicks = 10;        // kicks each (1..100) when !_accByTime
        int _accSeconds = 60;      // turn seconds (<=120) when _accByTime
        string _hostError = "";    // shown when Create couldn't open the host port

        public void Init(System.Action onCreated, System.Action onBack, GameMode? lockedMode = null)
        {
            _onCreated = onCreated; _onBack = onBack;
            if (lockedMode.HasValue)
            {
                Modes = new[] { lockedMode.Value };
                ModeNames = new[] { lockedMode.Value == GameMode.Match ? "Match" : lockedMode.Value.ToString() };
                _modeLocked = true;
            }
            else
            {
                Modes = new[] { GameMode.Striker, GameMode.SetPieces, GameMode.Accuracy };
                ModeNames = new[] { "Striker", "Set Pieces", "Accuracy" };
                _modeLocked = false;
            }
            _mode = 0;
            _stadium = StadiumStyle.SelectedIndex;
            GameInput.CaptureCursor(false);
        }

        void OnGUI()
        {
            // Scale the whole setup panel up on big displays (see MenuScale); sizes below are
            // unchanged, they just fill more of the screen.
            MenuScale.Begin();
            // Accuracy adds four extra option rows (wall / targets / turn format / turn amount).
            // A locked single mode skips the picker row entirely (one option is not a choice).
            float w = 480f, panelH = (Modes[_mode] == GameMode.Accuracy ? 610f : 470f) - (_modeLocked ? 58f : 0f);
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 640f);
            UITheme.Panel(new Rect(x, y, w, panelH), UITheme.Blue);

            UITheme.Title(new Rect(x, y + 12f, w, 36f), _modeLocked ? ModeNames[0].ToUpper() + " - HOST SETUP" : "HOST SETUP", 28);

            float lx = x + 30f, lw = w - 60f, row = y + 60f;
            UITheme.Divider(lx, row - 8f, lw);
            if (!_modeLocked) Picker(lx, ref row, lw, "Mode", ModeNames, ref _mode);
            // PickerVals, not Picker: the names are filtered to the OFFERED venues, so the button
            // position is no longer the All index, and Create() sends _stadium as the wire byte.
            PickerVals(lx, ref row, lw, "Stadium", StadiumNames(), StadiumStyle.PickableIndices(), ref _stadium);
            if (Modes[_mode] == GameMode.Match)
            {
                PickerVals(lx, ref row, lw, "Team size", new[] { "3 v 3", "5 v 5", "11 v 11" }, new[] { 3, 5, 11 }, ref _perSide);
                PickerVals(lx, ref row, lw, "Match length", new[] { "2 min", "3 min", "5 min", "10 min" }, new[] { 2, 3, 5, 10 }, ref _matchMin);
            }
            else if (Modes[_mode] == GameMode.SetPieces)
            {
                // Balance-safe knobs only: goal size + AI keeper strength.
                PickerVals(lx, ref row, lw, "Goal size", new[] { "Small", "Normal", "Big" }, new[] { 80, 100, 125 }, ref _goalPct);
                PickerVals(lx, ref row, lw, "Keeper", KeeperNames, KeeperPcts, ref _keeperPct);
            }
            else if (Modes[_mode] == GameMode.Accuracy)
            {
                // Free kicks at pop-up targets; shooters take turns and compare target points.
                PickerVals(lx, ref row, lw, "Goal size", new[] { "Small", "Normal", "Big" }, new[] { 80, 100, 125 }, ref _goalPct);
                PickerVals(lx, ref row, lw, "Keeper", KeeperNames, KeeperPcts, ref _keeperPct);
                PickerVals(lx, ref row, lw, "Wall players", new[] { "None", "2", "3", "4", "5" }, new[] { 0, 2, 3, 4, 5 }, ref _accWall);
                PickerVals(lx, ref row, lw, "Targets up", new[] { "2", "3", "4", "6", "8" }, new[] { 2, 3, 4, 6, 8 }, ref _accTargets);
                // Turn format: a fixed kick count, or a timed round each.
                int fmt = _accByTime ? 1 : 0;
                PickerVals(lx, ref row, lw, "Turn ends on", new[] { "Kicks", "Timer" }, new[] { 0, 1 }, ref fmt);
                _accByTime = fmt == 1;
                if (_accByTime)
                    PickerVals(lx, ref row, lw, "Turn time", new[] { "30s", "45s", "60s", "90s", "120s" }, new[] { 30, 45, 60, 90, 120 }, ref _accSeconds);
                else
                    PickerVals(lx, ref row, lw, "Kicks each", new[] { "1", "3", "5", "10", "25", "50", "100" }, new[] { 1, 3, 5, 10, 25, 50, 100 }, ref _accKicks);
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

            // Free-kick placement map (Set Pieces only): a side panel to the right of the main
            // window where the host drops the ball spot + wall, like the in-match cross map.
            // Accuracy uses the same dead-ball + wall placement, so it shows the same map.
            if (Modes[_mode] == GameMode.SetPieces || Modes[_mode] == GameMode.Accuracy)
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
            var mode = Modes[_mode];
            StadiumStyle.SelectedIndex = _stadium;
            // Both dead-ball modes (set pieces + accuracy) use the goal/keeper/placement knobs.
            bool deadBall = mode == GameMode.SetPieces || mode == GameMode.Accuracy;

            // Match is two teams mapped onto the 8 slots (capped 4-a-side incl keepers), so
            // both sides can be human: allow up to 2*perSide (bounded to the 8-slot board).
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
                goalScale = deadBall ? _goalPct / 100f : 1f,
                keeperAbility = deadBall ? _keeperPct / 100f : 0.5f,
                // Host-placed free-kick spot + wall. fkPlaced tells the driver to honour them;
                // when false (map never opened / other modes) the driver uses its own default.
                fkPlaced = deadBall && _fkInit,
                fkBallX = _fkBall.x, fkBallZ = _fkBall.z,
                fkWallX = _fkWall.x, fkWallZ = _fkWall.z,
                // Random per-round spots: carry the flag + a fresh seed so every peer derives the
                // same schedule. The seed also drives the accuracy target layout.
                fkRandom = mode == GameMode.SetPieces && _fkRandom,
                fkSeed = (uint)Random.Range(1, int.MaxValue),
                // Accuracy: optional wall, target count, and the turn-end rule.
                accWallCount = (byte)(mode == GameMode.Accuracy ? _accWall : 0),
                accTargets = (byte)(mode == GameMode.Accuracy ? _accTargets : 4),
                accTurnByTime = mode == GameMode.Accuracy && _accByTime,
                accTurnKicks = (byte)Mathf.Clamp(_accKicks, 1, 100),
                accTurnSeconds = (ushort)Mathf.Clamp(_accSeconds, 10, 120),
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

        void Picker(float lx, ref float row, float lw, string label, string[] names, ref int idx)
        {
            UITheme.Label(new Rect(lx, row, lw, 20f), label + ":", RowLabel());
            float bw = (lw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = i == idx;
                if (UITheme.Toggle(new Rect(lx + i * (bw + 6f), row + 22f, bw, 28f), names[i], sel, PickStyle(sel)))
                    idx = i;
            }
            UITheme.Divider(lx, row + 53f, lw);
            row += 58f;
        }

        // Keeper difficulty, as whole percents of SimConfig.KeeperAbility. Same five names and the
        // same five levels the single-player pre-match screen offers (PrematchUI.KeeperNames), so
        // "Hard" means one thing across the game instead of the old Low/Med/High meaning something
        // else again. None builds no goalkeeper at all.
        static readonly string[] KeeperNames = { "None", "Easy", "Normal", "Hard", "Insane" };
        static readonly int[]    KeeperPcts  = { 0, 25, 50, 75, 100 };

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

        void Toggle(float lx, ref float row, float lw, string label, ref bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            st.normal.textColor = UITheme.Ink;
            st.onNormal.textColor = st.onHover.textColor =
                st.onActive.textColor = st.onFocused.textColor = UITheme.Gold;
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "  " + label, st);
            row += 40f;
        }

        static string[] StadiumNames()
        {
            var idx = StadiumStyle.PickableIndices();
            var names = new string[idx.Length];
            for (int i = 0; i < idx.Length; i++) names[i] = StadiumStyle.All[idx[i]].Name;
            return names;
        }
    }
}
