using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Session browser: lists joinable lobbies (name / mode / players), Refresh + Join +
    /// Back. Routes through Multiplayer.Browse (loopback now, Steam RequestLobbyList once
    /// wired) and Multiplayer.Join(handle). On a successful join it hands off to the lobby.
    /// </summary>
    public class SessionBrowserUI : MonoBehaviour
    {
        System.Action _onBack;
        System.Action _onJoined;   // invoked once we've asked to join a lobby -> show lobby

        readonly List<LobbyInfo> _lobbies = new List<LobbyInfo>();
        int _sel = -1;
        float _autoRefresh;
        string _ipText = "";        // direct-IP join box ("ip" or "ip:port")
        string _ipError = "";       // shown when the typed address won't parse

        public void Init(System.Action onJoined, System.Action onBack)
        {
            _onJoined = onJoined; _onBack = onBack;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Refresh();
        }

        void Refresh()
        {
            Multiplayer.Browse(list => { _lobbies.Clear(); _lobbies.AddRange(list); if (_sel >= _lobbies.Count) _sel = -1; });
        }

        // Parse the typed "ip" / "ip:port" into a join handle and connect. The handle encodes
        // the endpoint (see NetEndpoint); Multiplayer.Join routes it to the direct-IP transport.
        void TryJoinByIp()
        {
            if (!NetEndpoint.TryParse(_ipText, out var handle))
            {
                _ipError = "Enter a valid IPv4 address, e.g. 192.168.1.5 or 100.90.1.2:7777";
                return;
            }
            _ipError = "";
            Multiplayer.Join(handle);
            enabled = false; _onJoined?.Invoke();
        }

        void Update()
        {
            // Light auto-refresh so a lobby hosted moments ago shows up.
            _autoRefresh -= Time.unscaledDeltaTime;
            if (_autoRefresh <= 0f) { _autoRefresh = 1.5f; Refresh(); }
        }

        void OnGUI()
        {
            float w = 560f, rowH = 46f, gap = 8f;
            float panelH = 150f + 6 * (rowH + gap) + 60f + 78f;   // +78: direct-IP join row
            float x = Screen.width * 0.5f - w * 0.5f;
            float y = Screen.height * 0.5f - panelH * 0.5f;
            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
            GUI.DrawTexture(new Rect(x, y, w, panelH), Texture2D.whiteTexture); GUI.color = prev;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, y + 14f, w, 40f), "FIND A SESSION", title);

            var rowName = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var rowNameSel = new GUIStyle(rowName); rowNameSel.normal.textColor = new Color(1f, 0.9f, 0.3f);
            var meta = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.82f, 0.83f, 0.88f) } };

            float row = y + 66f, lx = x + 24f, lw = w - 48f;
            if (_lobbies.Count == 0)
            {
                var empty = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
                GUI.Label(new Rect(lx, row, lw, 40f), "No sessions found. Host one, or Refresh.", empty);
            }
            for (int i = 0; i < _lobbies.Count && i < 6; i++)
            {
                var l = _lobbies[i];
                bool sel = i == _sel;
                var r = new Rect(lx, row, lw, rowH);
                if (GUI.Button(r, "  " + (string.IsNullOrEmpty(l.name) ? "Session" : l.name), sel ? rowNameSel : rowName)) _sel = i;
                GUI.Label(new Rect(r.x, r.y, r.width - 14f, rowH), $"{l.mode}    {l.players}/{l.maxPlayers}  ", meta);
                row += rowH + gap;
            }

            // ---- Direct-IP join (LAN / Tailscale): no discovery, so type the host's IP. ----
            float ipY = y + panelH - 52f - 78f;
            var sep = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.7f, 0.72f, 0.8f) } };
            GUI.Label(new Rect(lx, ipY, lw, 20f), "Or join directly by the host's IP  (LAN, or Tailscale 100.x):", sep);

            var ipField = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            _ipText = GUI.TextField(new Rect(lx, ipY + 22f, lw - 150f, 34f), _ipText, 32, ipField);
            if (GUI.Button(new Rect(lx + lw - 140f, ipY + 22f, 140f, 34f), "Join by IP",
                           new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold }))
                TryJoinByIp();
            if (!string.IsNullOrEmpty(_ipError))
            {
                var err = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(1f, 0.5f, 0.45f) } };
                GUI.Label(new Rect(lx, ipY + 56f, lw, 18f), _ipError, err);
            }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            float by = y + panelH - 52f;
            if (GUI.Button(new Rect(x + 24f, by, 130f, 40f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(x + w * 0.5f - 65f, by, 130f, 40f), "Refresh", btn)) Refresh();

            GUI.enabled = _sel >= 0 && _sel < _lobbies.Count;
            if (GUI.Button(new Rect(x + w - 154f, by, 130f, 40f), "Join", btn))
            {
                Multiplayer.Join(_lobbies[_sel].handle);
                enabled = false; _onJoined?.Invoke();
            }
            GUI.enabled = true;
        }
    }
}
