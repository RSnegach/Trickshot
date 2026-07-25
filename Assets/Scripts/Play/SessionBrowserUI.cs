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
        string _ipError = "";       // shown when the typed address won't parse OR a join fails

        // Join is a two-step handshake, not instant: after Multiplayer.Join we WAIT for the host to
        // assign us a slot (LocalSlot >= 0) before showing the lobby. Without this the client used to
        // pop straight into its own empty local lobby whether or not the host was ever reached (the
        // "joined an empty lobby that wasn't mine" bug). If no slot arrives before the deadline the
        // host is unreachable (firewall / wrong IP / Tailscale down): tear down + show why.
        bool _connecting;
        float _connectDeadline;
        string _connectLabel = "";
        const float ConnectTimeout = 8f;   // > the transport's 5s peer timeout, room for reliable resends

        // Begin connecting to a host handle and enter the Connecting state (pumped in Update).
        void StartConnect(ulong handle, string label)
        {
            Multiplayer.Join(handle);
            _connecting = true;
            _connectDeadline = Time.unscaledTime + ConnectTimeout;
            _connectLabel = string.IsNullOrEmpty(label) ? "the host" : label;
            _ipError = "";
        }

        void CancelConnect()
        {
            Multiplayer.End();
            _connecting = false;
        }

        public void Init(System.Action onJoined, System.Action onBack)
        {
            _onJoined = onJoined; _onBack = onBack;
            GameInput.CaptureCursor(false);
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
            StartConnect(handle, _ipText.Trim());
        }

        void Update()
        {
            if (_connecting)
            {
                // Pump the transport so the connect + Hello/AssignSlot handshake progresses.
                Multiplayer.Poll();
                var s = Multiplayer.Session;
                // Success: the host assigned us a slot -> we're really in their lobby.
                if (s != null && s.LocalSlot >= 0)
                {
                    _connecting = false;
                    enabled = false; _onJoined?.Invoke();
                    return;
                }
                // Failure: the transport dropped (host timed out) or our deadline passed.
                if (s == null || !s.Active || Time.unscaledTime >= _connectDeadline)
                {
                    CancelConnect();
                    _ipError = "Couldn't reach " + _connectLabel + ". Check the IP, the host's "
                             + "firewall (allow UDP " + NetEndpoint.DefaultPort + "), and Tailscale.";
                }
                return;   // don't refresh/list while connecting
            }

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
                var empty = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.85f) } };
                GUI.Label(new Rect(lx, row, lw, 56f),
                          "The lobby list only finds Steam games. For LAN or Tailscale, use "
                          + "“Join by IP” below with the host's address.", empty);
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
            float by = Screen.height - 100f;   // fixed 100px from the screen bottom, clear of panel content
            if (GUI.Button(new Rect(x + 24f, by, 130f, 40f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(x + w * 0.5f - 65f, by, 130f, 40f), "Refresh", btn)) Refresh();

            GUI.enabled = _sel >= 0 && _sel < _lobbies.Count;
            if (GUI.Button(new Rect(x + w - 154f, by, 130f, 40f), "Join", btn))
                StartConnect(_lobbies[_sel].handle, _lobbies[_sel].name);
            GUI.enabled = true;

            // Connecting overlay: block the panel + show progress while we wait for the host.
            if (_connecting) DrawConnecting();
        }

        // Modal "Connecting…" overlay shown between clicking Join and the host assigning us a slot.
        void DrawConnecting()
        {
            var pc = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = pc;

            float w = 420f, h = 150f;
            float px = Screen.width * 0.5f - w * 0.5f, py = Screen.height * 0.5f - h * 0.5f;
            GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.98f); GUI.DrawTexture(new Rect(px, py, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.16f, 0.55f, 0.95f); GUI.DrawTexture(new Rect(px, py, w, 3f), Texture2D.whiteTexture);
            GUI.color = pc;

            int dots = ((int)(Time.unscaledTime * 2f) % 4);
            var msg = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(px, py + 28f, w, 30f), "Connecting to " + _connectLabel + new string('.', dots), msg);
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.77f, 0.82f) } };
            GUI.Label(new Rect(px + 20f, py + 60f, w - 40f, 20f), "Waiting for the host to respond", sub);

            var cancel = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            if (GUI.Button(new Rect(px + w * 0.5f - 65f, py + h - 46f, 130f, 34f), "Cancel", cancel))
                CancelConnect();
        }
    }
}
