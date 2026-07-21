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

        // Only the two networkable modes.
        static readonly GameMode[] Modes = { GameMode.Scrimmage, GameMode.Striker };
        static readonly string[] ModeNames = { "Scrimmage", "Striker" };
        int _mode;                 // index into Modes
        int _stadium;
        int _perSide = 3;          // scrimmage team size (3/5/11)
        int _matchMin = 3;         // scrimmage length (min)
        bool _publicLobby = true;

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
            Toggle(lx, ref row, lw, "Public (anyone can join)", ref _publicLobby);
            // (Striker AI is chosen per-slot in the lobby now, not here.)

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            float by = y + panelH - 56f;
            if (GUI.Button(new Rect(x + 30f, by, 160f, 44f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(x + w - 190f, by, 160f, 44f), "Create", btn)) Create();
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
