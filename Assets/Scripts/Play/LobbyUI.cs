using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Pre-match lobby for a networked session. Shows the slot roster (keeper + shooters,
    /// marking YOU / human / AI + ready state) and the host's match config. Each player can
    /// open the Customize screens for their OWN player, then Ready up. The host sees ready
    /// states and Starts when everyone's ready (broadcasts StartMatch); clients wait for it.
    ///
    /// Polls the session each frame. Callbacks: onCustomize (open the customize flow, then
    /// return here), onStart (the match is beginning - build it), onLeave (back to the hub).
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        System.Action _onCustomize, _onStart, _onLeave;
        NetSession _s;
        bool _started;
        string _hostAddrLine;   // host + direct-IP only: the IPs to share with friends

        public void Init(System.Action onCustomize, System.Action onStart, System.Action onLeave)
        {
            _onCustomize = onCustomize; _onStart = onStart; _onLeave = onLeave;
            _s = Multiplayer.Session;
            // Losing the host is handled centrally (Multiplayer.HostConnectionLost -> GameBootstrap),
            // so the lobby only needs the match-start hand-off.
            if (_s != null) _s.MatchStarting += OnMatchStarting;
            GameInput.CaptureCursor(false);

            // On the direct-IP path, a host shows its address(es) so friends can type them in.
            // (Steam has its own invite flow, so skip it there.) Cache once - Dns isn't free.
            if (_s != null && _s.IsHost && !Multiplayer.SteamLinked)
            {
                var ips = NetEndpoint.LocalIPv4s();
                _hostAddrLine = ips.Count > 0
                    ? "Friends join at:  " + string.Join("   /   ", ips) + "   (port " + NetEndpoint.DefaultPort + ")"
                    : "Share your IP with friends to join (port " + NetEndpoint.DefaultPort + ").";
            }
        }

        void OnDestroy()
        {
            if (_s != null) _s.MatchStarting -= OnMatchStarting;
        }

        void OnMatchStarting()
        {
            if (_started) return;
            _started = true;
            enabled = false;
            _onStart?.Invoke();
        }

        void Update()
        {
            // NOTE: the session-lifetime pump in Multiplayer polls every frame now, so this call is
            // belt-and-braces (Poll is idempotent within a frame). Losing the host is likewise no
            // longer handled here: Multiplayer.HostConnectionLost fires on EVERY screen (including
            // mid-match, which nothing used to cover) and GameBootstrap unwinds to the main menu, so
            // handling it locally too would tear down twice.
            Multiplayer.Poll();
        }

        // Scale the lobby up on big displays (see MenuScale). Wrapped so the early return can't
        // leak the scaled GUI matrix.
        void OnGUI()
        {
            if (_s == null) { _onLeave?.Invoke(); return; }
            MenuScale.Begin();
            DrawLobby();
            MenuScale.End();
        }

        void DrawLobby()
        {
            float w = 560f, panelH = 480f;
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.94f);
            GUI.DrawTexture(new Rect(x, y, w, panelH), Texture2D.whiteTexture);
            GUI.color = new Color(0.16f, 0.55f, 0.95f, 0.9f); GUI.DrawTexture(new Rect(x, y, w, 3f), Texture2D.whiteTexture);
            GUI.color = prev;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, y + 12f, w, 34f), _s.IsHost ? "LOBBY (HOST)" : "LOBBY", title);

            // Config summary.
            var meta = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.86f, 0.32f) } };
            GUI.Label(new Rect(x, y + 46f, w, 22f), ConfigLine(), meta);

            // Host address line (direct-IP host only): what friends type to join.
            float rosterTop = y + 80f;
            if (!string.IsNullOrEmpty(_hostAddrLine))
            {
                var addr = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.55f, 0.85f, 0.95f) } };
                GUI.Label(new Rect(x + 10f, y + 66f, w - 20f, 20f), _hostAddrLine, addr);
                rosterTop = y + 92f;
            }

            // Roster.
            var name = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            var tag = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
            float row = rosterTop, lx = x + 28f, lw = w - 56f, rowH = 30f;
            var roster = _s.Roster;
            var claimBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            for (int i = 0; i < roster.Length; i++)
            {
                var slot = roster[i];
                bool isMe = slot.slot == _s.LocalSlot;
                string role = RoleName(slot.role, slot.slot);
                // Row occupant: human name, "Clanker N", or "Open" (all baked into slot.name).
                string who = slot.name;
                var rowBg = GUI.color; if (isMe) { GUI.color = new Color(0.16f, 0.3f, 0.5f, 0.6f); GUI.DrawTexture(new Rect(lx - 6f, row, lw + 12f, rowH - 2f), Texture2D.whiteTexture); GUI.color = rowBg; }

                GUI.Label(new Rect(lx, row, lw * 0.5f, rowH), $"{role}:  {who}{(isMe ? "  (you)" : "")}", name);

                if (slot.human)
                {
                    // Human-held: show ready state (no buttons on someone else's row).
                    tag.normal.textColor = slot.ready ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.9f, 0.6f, 0.3f);
                    GUI.Label(new Rect(lx, row, lw, rowH), slot.ready ? "READY" : "not ready", tag);
                }
                else
                {
                    // Non-human slot. The host gets a per-slot AI On/Off toggle; anyone (not
                    // already here) can Claim it to take that role themselves.
                    float bx = lx + lw;
                    if (!isMe)
                    {
                        bx -= 92f;
                        if (GUI.Button(new Rect(bx, row + 2f, 92f, rowH - 6f), "Claim", claimBtn))
                            _s.RequestSlot(slot.slot);
                    }
                    if (_s.IsHost)
                    {
                        bx -= 84f;
                        var aiBtn = new GUIStyle(claimBtn);
                        aiBtn.normal.textColor = slot.ai ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.7f, 0.72f, 0.78f);
                        if (GUI.Button(new Rect(bx, row + 2f, 80f, rowH - 6f), slot.ai ? "AI: On" : "AI: Off", aiBtn))
                            _s.SetSlotAi(slot.slot, !slot.ai);
                    }
                }
                row += rowH;
            }

            // Footer buttons.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            float by = y + panelH - 54f;
            if (GUI.Button(new Rect(x + 24f, by, 130f, 42f), "Leave", btn)) { Multiplayer.End(); enabled = false; _onLeave?.Invoke(); }

            // Customize my player.
            if (GUI.Button(new Rect(x + 164f, by, 170f, 42f), "Customize", btn)) { enabled = false; _onCustomize?.Invoke(); }

            // Ready toggle (me).
            bool ready = _s.LocalReady;
            var readyBtn = new GUIStyle(btn); if (ready) readyBtn.normal.textColor = new Color(0.35f, 0.85f, 0.45f);
            if (GUI.Button(new Rect(x + w - 154f, by, 130f, 42f), ready ? "Ready ✓" : "Ready", readyBtn)) _s.SetReady(!ready);

            // Host start (needs all humans ready).
            if (_s.IsHost)
            {
                bool can = _s.AllReady();
                GUI.enabled = can;
                var startBtn = new GUIStyle(btn) { fontSize = 18 };
                if (GUI.Button(new Rect(x + w * 0.5f - 90f, by - 52f, 180f, 44f), can ? "START MATCH" : "waiting...", startBtn))
                    _s.StartMatch();
                GUI.enabled = true;
            }
        }

        // Label a roster row by its NetRole (falls back to slot index for shooters).
        static string RoleName(byte role, byte slot)
        {
            switch ((Trickshot.Net.NetRole)role)
            {
                case Trickshot.Net.NetRole.Keeper:  return "Keeper";
                case Trickshot.Net.NetRole.Crosser: return "Crosser";
                default:                            return "Shooter " + slot;
            }
        }

        string ConfigLine()
        {
            var c = _s.Config;
            var mode = (GameMode)c.mode;
            string stadium = c.stadium < StadiumStyle.All.Length ? StadiumStyle.All[c.stadium].Name : "?";
            if (mode == GameMode.Scrimmage)
                return $"Scrimmage  {c.perSide}v{c.perSide}   {stadium}   {c.matchSec / 60} min";
            return $"{mode}   {stadium}";
        }
    }
}
