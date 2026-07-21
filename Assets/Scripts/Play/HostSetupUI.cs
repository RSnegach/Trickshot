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

        // Networkable modes.
        static readonly GameMode[] Modes = { GameMode.Scrimmage, GameMode.Striker, GameMode.SetPieces };
        static readonly string[] ModeNames = { "Scrimmage", "Striker", "Set Pieces" };
        int _mode;                 // index into Modes
        int _stadium;
        int _perSide = 3;          // scrimmage team size (3/5/11)
        int _matchMin = 3;         // scrimmage length (min)
        bool _publicLobby = true;
        // Set-pieces host settings (goal size %, keeper ability). Ball/player speed intentionally
        // NOT exposed - kept fixed so multiplayer stays balanced.
        int _goalPct = 100;        // 80 / 100 / 125
        int _keeperPct = 50;       // 0 / 30 / 60 / 90 (AI keeper strength if no human GK)
        // Host-placed free-kick spot + wall (world x/z). Lazily defaulted the first frame the
        // Set Pieces map is shown (centre spot at FreeKickDistance, wall at WallDistance toward
        // goal). _fkEdit selects which marker a map click moves: 0 = ball, 1 = wall.
        bool _fkInit; Vector3 _fkBall, _fkWall; int _fkEdit;

        public void Init(System.Action onCreated, System.Action onBack)
        {
            _onCreated = onCreated; _onBack = onBack;
            _stadium = StadiumStyle.SelectedIndex;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void OnGUI()
        {
            float w = 480f, panelH = 470f;
            float x = Screen.width * 0.5f - w * 0.5f;
            float y = Screen.height * 0.5f - panelH * 0.5f;
            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
            GUI.DrawTexture(new Rect(x, y, w, panelH), Texture2D.whiteTexture); GUI.color = prev;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, y + 12f, w, 36f), "HOST SETUP", title);

            float lx = x + 30f, lw = w - 60f, row = y + 60f;
            Picker(lx, ref row, lw, "Mode", ModeNames, ref _mode);
            Picker(lx, ref row, lw, "Stadium", StadiumNames(), ref _stadium);
            if (Modes[_mode] == GameMode.Scrimmage)
            {
                PickerVals(lx, ref row, lw, "Team size", new[] { "3 v 3", "5 v 5", "11 v 11" }, new[] { 3, 5, 11 }, ref _perSide);
                PickerVals(lx, ref row, lw, "Match length", new[] { "2 min", "3 min", "5 min", "10 min" }, new[] { 2, 3, 5, 10 }, ref _matchMin);
            }
            else if (Modes[_mode] == GameMode.SetPieces)
            {
                // Balance-safe knobs only: goal size + AI keeper strength.
                PickerVals(lx, ref row, lw, "Goal size", new[] { "Small", "Normal", "Big" }, new[] { 80, 100, 125 }, ref _goalPct);
                PickerVals(lx, ref row, lw, "Keeper ability", new[] { "None", "Low", "Med", "High" }, new[] { 0, 30, 60, 90 }, ref _keeperPct);
            }
            Toggle(lx, ref row, lw, "Public (anyone can join)", ref _publicLobby);
            // (Striker AI is chosen per-slot in the lobby now, not here.)

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            float by = Screen.height - 100f;   // fixed 100px from the screen bottom, clear of panel content
            if (GUI.Button(new Rect(x + 30f, by, 160f, 44f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(x + w - 190f, by, 160f, 44f), "Create", btn)) Create();

            // Free-kick placement map (Set Pieces only): a side panel to the right of the main
            // window where the host drops the ball spot + wall, like the in-match cross map.
            if (Modes[_mode] == GameMode.SetPieces) DrawFreeKickSetup(x + w + 16f, y);
        }

        void DrawFreeKickSetup(float px, float py)
        {
            if (!_fkInit)
            {
                // Default just outside the box (free kicks are taken from outside it).
                _fkBall = SetPieceMap.ClampOutsideBox(new Vector3(0f, 0f, SimConfig.GoalCenter.z - SimConfig.FreeKickDistance));
                Vector3 toGoal = SimConfig.GoalCenter - _fkBall; toGoal.y = 0f;
                _fkWall = _fkBall + toGoal.normalized * SimConfig.WallDistance;
                _fkInit = true;
            }

            float w = 300f, h = 300f;
            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
            GUI.DrawTexture(new Rect(px, py, w, h + 74f), Texture2D.whiteTexture); GUI.color = prev;

            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(px, py + 8f, w, 26f), "FREE KICK SETUP", hdr);

            // Ball / Wall edit selector.
            var sel = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            var selOn = new GUIStyle(sel); selOn.normal.textColor = new Color(1f, 0.9f, 0.3f);
            if (GUI.Button(new Rect(px + 16f, py + 38f, (w - 40f) * 0.5f, 28f), _fkEdit == 0 ? "● Ball" : "Ball", _fkEdit == 0 ? selOn : sel)) _fkEdit = 0;
            if (GUI.Button(new Rect(px + 24f + (w - 40f) * 0.5f, py + 38f, (w - 40f) * 0.5f, 28f), _fkEdit == 1 ? "● Wall" : "Wall", _fkEdit == 1 ? selOn : sel)) _fkEdit = 1;

            var mapRect = new Rect(px + 16f, py + 74f, w - 32f, h - 74f);
            SetPieceMap.Draw(mapRect, ref _fkBall, ref _fkWall, _fkEdit);

            var tip = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.85f, 0.9f) } };
            GUI.Label(new Rect(px, py + h + 44f, w, 20f), "Click the map to place the " + (_fkEdit == 1 ? "wall" : "ball") + ".", tip);
        }

        void Create()
        {
            var mode = Modes[_mode];
            StadiumStyle.SelectedIndex = _stadium;

            int maxPlayers = mode == GameMode.Scrimmage ? _perSide + 1 : 8;   // keeper + shooters
            Multiplayer.Host(maxPlayers);
            Multiplayer.Session.SetConfig(new MatchConfig
            {
                mode = (byte)mode,
                stadium = (byte)_stadium,
                perSide = (byte)_perSide,
                matchSec = (ushort)(_matchMin * 60),
                publicLobby = _publicLobby,
                // Set-pieces knobs; harmless defaults for other modes (goalScale 1 = regulation).
                goalScale = mode == GameMode.SetPieces ? _goalPct / 100f : 1f,
                keeperAbility = mode == GameMode.SetPieces ? _keeperPct / 100f : 0.5f,
                // Host-placed free-kick spot + wall. fkPlaced tells the driver to honour them;
                // when false (map never opened / other modes) the driver uses its own default.
                fkPlaced = mode == GameMode.SetPieces && _fkInit,
                fkBallX = _fkBall.x, fkBallZ = _fkBall.z,
                fkWallX = _fkWall.x, fkWallZ = _fkWall.z,
            });

            enabled = false;
            _onCreated?.Invoke();
        }

        // ---- small pickers ----
        void Picker(float lx, ref float row, float lw, string label, string[] names, ref int idx)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUI.Label(new Rect(lx, row, lw, 20f), label + ":", st);
            float bw = (lw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = i == idx;
                var b = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) b.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (bw + 6f), row + 22f, bw, 28f), names[i], b)) idx = i;
            }
            row += 58f;
        }

        void PickerVals(float lx, ref float row, float lw, string label, string[] names, int[] vals, ref int val)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUI.Label(new Rect(lx, row, lw, 20f), label + ":", st);
            float bw = (lw - 6f * (names.Length - 1)) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                bool sel = vals[i] == val;
                var b = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) b.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (bw + 6f), row + 22f, bw, 28f), names[i], b)) val = vals[i];
            }
            row += 58f;
        }

        void Toggle(float lx, ref float row, float lw, string label, ref bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15, normal = { textColor = Color.white }, onNormal = { textColor = Color.white } };
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "  " + label, st);
            row += 40f;
        }

        static string[] StadiumNames()
        {
            var all = StadiumStyle.All;
            var names = new string[all.Length];
            for (int i = 0; i < all.Length; i++) names[i] = all[i].Name;
            return names;
        }
    }
}
