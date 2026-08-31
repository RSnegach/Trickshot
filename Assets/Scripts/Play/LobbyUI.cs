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
        string _inviteCode;     // host + direct-IP only: the short code friends paste to join
        float _copiedUntil;     // brief "Copied!" confirmation after the Copy button

        // Online (ranked drop-in) only: the host auto-starts once this fires, rather than
        // waiting on a manual Ready/Start - a drop-in queue is meant to just go. -1 = not an
        // Online lobby (Friendlies keeps its exact manual flow, untouched).
        float _onlineDeadline = -1f;

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
                _inviteCode = NetEndpoint.LocalInvite();
                var ips = NetEndpoint.LocalIPv4s();
                _hostAddrLine = ips.Count > 0
                    ? string.Join("   /   ", ips) + "   (port " + NetEndpoint.DefaultPort + ")"
                    : "No network address. Check Tailscale is up.";
            }

            if (_s != null && _s.IsHost && _s.Config.onlineRanked)
                _onlineDeadline = Time.unscaledTime + 20f;   // short window for strangers to trickle in
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

            if (_s == null || _started || !_s.Config.onlineRanked) return;
            // Online drop-in: no manual ready-up - the moment you have a seat, you're in. Kept
            // sticky (re-applied every frame) rather than a one-shot, so a stray un-ready click
            // can't stall the host's countdown below.
            if (_s.LocalSlot >= 0 && !_s.LocalReady) _s.SetReady(true);
            if (_s.IsHost && _onlineDeadline >= 0f && Time.unscaledTime >= _onlineDeadline && _s.AllReady())
                _s.StartMatch();
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
            // The invite block is taller than the one-line address readout it replaced, so grow the
            // panel by the difference on a direct-IP host - otherwise the roster's last rows run
            // under the START MATCH button.
            bool showInvite = !string.IsNullOrEmpty(_hostAddrLine);
            float w = 560f, panelH = 480f + (showInvite ? 84f : 0f);
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 300f);
            UITheme.Panel(new Rect(x, y, w, panelH), UITheme.Blue);

            UITheme.Title(new Rect(x, y + 12f, w, 34f), _s.IsHost ? "LOBBY (HOST)" : "LOBBY", 28);

            // Config summary.
            var meta = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            GUI.Label(new Rect(x, y + 46f, w, 22f), ConfigLine(), meta);

            // Invite block (direct-IP host only): the code friends paste into Find a Session. Shown
            // big because reading it aloud / pasting it is the whole join flow off Steam.
            float rosterTop = y + 80f;
            if (showInvite)
            {
                float iy = y + 72f, ih = 94f;   // +18 for the discoverability line at the bottom
                UITheme.Chip(new Rect(x + 20f, iy, w - 40f, ih), new Color(0.10f, 0.14f, 0.21f, 0.96f), UITheme.Green);

                UITheme.Section(new Rect(x + 32f, iy + 4f, w - 64f, 18f), "INVITE CODE  -  PASTE INTO FIND A SESSION");

                var code = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Green } };
                var cr = new Rect(x + 32f, iy + 20f, w - 170f, 32f);
                UITheme.Shadowed(cr, string.IsNullOrEmpty(_inviteCode) ? "unavailable" : _inviteCode, code, UITheme.Green, 0.7f, 1.5f);

                var copyBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
                bool justCopied = Time.unscaledTime < _copiedUntil;
                GUI.enabled = !string.IsNullOrEmpty(_inviteCode);
                var keepCopy = GUI.backgroundColor;
                if (justCopied) GUI.backgroundColor = UITheme.GoodTint;
                if (UITheme.Button(new Rect(x + w - 132f, iy + 22f, 100f, 28f), justCopied ? "Copied!" : "Copy", copyBtn))
                {
                    GUIUtility.systemCopyBuffer = _inviteCode;
                    _copiedUntil = Time.unscaledTime + 1.5f;
                }
                GUI.backgroundColor = keepCopy;
                GUI.enabled = true;

                var addr = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
                GUI.Label(new Rect(x + 32f, iy + 52f, w - 64f, 18f), "or by address:  " + _hostAddrLine, addr);

                // Discoverability read-out. Discovery is silent by design: a host that is private,
                // full, or already playing simply does not answer probes, so it vanishes from every
                // browser with no error anywhere. Without this line the host would have no way to tell
                // "my friend can't find me" from "I turned that off", which is the same confusion the
                // Public toggle caused when it advertised nothing at all.
                Color vc; string vis = VisibilityLine(out vc);
                var visStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = vc } };
                UITheme.Dot(x + 26f, iy + 79f, vc, 2.5f);
                GUI.Label(new Rect(x + 32f, iy + 70f, w - 64f, 18f), vis, visStyle);

                rosterTop = iy + ih + 10f;
            }

            // Roster. Match gets Home/Away position columns (below) so friends can pick a team
            // and a shirt; every other mode keeps the flat per-slot list unchanged.
            UITheme.Divider(x + 28f, rosterTop - 6f, w - 56f);
            if ((GameMode)_s.Config.mode == GameMode.Match) DrawMatchTeams(x + 28f, rosterTop, w - 56f);
            else DrawFlatRoster(x + 28f, rosterTop, w - 56f);

            // Footer buttons.
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            float by = y + panelH - 54f;
            UITheme.Divider(x + 24f, by - 10f, w - 48f);
            if (UITheme.Button(new Rect(x + 24f, by, 130f, 42f), "Leave", btn, true)) { Multiplayer.End(); enabled = false; _onLeave?.Invoke(); }

            // Customize my player.
            if (UITheme.Button(new Rect(x + 164f, by, 170f, 42f), "Customize", btn)) { enabled = false; _onCustomize?.Invoke(); }

            // Ready toggle (me).
            bool ready = _s.LocalReady;
            var readyBtn = new GUIStyle(btn); if (ready) readyBtn.normal.textColor = UITheme.Green;
            if (UITheme.Toggle(new Rect(x + w - 154f, by, 130f, 42f), ready ? "Ready" : "Ready", ready, readyBtn, UITheme.GoodTint))
                _s.SetReady(!ready);

            // Host start (needs all humans ready).
            if (_s.IsHost)
            {
                bool can = _s.AllReady();
                var sr = new Rect(x + w * 0.5f - 90f, by - 52f, 180f, 44f);
                // Live once everyone is ready, and lit so it looks like the thing to press.
                if (can) UITheme.Glow(new Rect(sr.x - 20f, sr.y - 10f, sr.width + 40f, sr.height + 20f),
                                      new Color(UITheme.Green.r, UITheme.Green.g, UITheme.Green.b, 0.14f));
                GUI.enabled = can;
                var startBtn = new GUIStyle(btn) { fontSize = 18 };
                var keepStart = GUI.backgroundColor;
                if (can) GUI.backgroundColor = UITheme.GoodTint;
                if (UITheme.Button(sr, can ? "START MATCH" : "waiting...", startBtn)) _s.StartMatch();
                GUI.backgroundColor = keepStart;
                GUI.enabled = true;
            }
        }

        // The flat per-slot list every non-Match mode used before Match got its own team columns
        // (see DrawMatchTeams) - unchanged from the original single-column roster.
        void DrawFlatRoster(float lx, float top, float lw)
        {
            var name = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            var tag = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleRight };
            var claimBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            float row = top, rowH = 30f;
            var roster = _s.Roster;
            for (int i = 0; i < roster.Length; i++)
            {
                var slot = roster[i];
                bool isMe = slot.slot == _s.LocalSlot;
                string role = RoleName(slot.role, slot.slot);
                // Row occupant: human name, "Clanker N", or "Open" (all baked into slot.name).
                string who = slot.name;
                // Your own row gets a lit band plus a gold spine, so it is findable at a glance
                // in an eight-slot roster.
                if (isMe)
                {
                    UITheme.Fill(new Rect(lx - 6f, row, lw + 12f, rowH - 2f), new Color(0.14f, 0.28f, 0.48f, 0.55f));
                    UITheme.Fill(new Rect(lx - 6f, row, 2.5f, rowH - 2f), UITheme.Gold);
                }
                else UITheme.Divider(lx, row + rowH - 2f, lw);

                GUI.Label(new Rect(lx + 8f, row, lw * 0.5f, rowH), $"{role}:  {who}{(isMe ? "  (you)" : "")}", name);

                if (slot.human)
                {
                    // Human-held: show ready state (no buttons on someone else's row).
                    Color rc = slot.ready ? UITheme.Green : UITheme.Gold;
                    tag.normal.textColor = rc;
                    string rt = slot.ready ? "READY" : "not ready";
                    float rw = tag.CalcSize(new GUIContent(rt)).x;
                    UITheme.Dot(lx + lw - rw - 10f, row + rowH * 0.5f, rc, 2.5f);
                    GUI.Label(new Rect(lx, row, lw, rowH), rt, tag);
                }
                else
                {
                    // Non-human slot. The host gets a per-slot AI On/Off toggle; anyone (not
                    // already here) can Claim it to take that role themselves.
                    float bx = lx + lw;
                    if (!isMe)
                    {
                        bx -= 92f;
                        if (UITheme.Button(new Rect(bx, row + 2f, 92f, rowH - 6f), "Claim", claimBtn))
                            _s.RequestSlot(slot.slot);
                    }
                    if (_s.IsHost)
                    {
                        bx -= 84f;
                        var aiBtn = new GUIStyle(claimBtn);
                        aiBtn.normal.textColor = slot.ai ? UITheme.Green : UITheme.Faint;
                        if (UITheme.Toggle(new Rect(bx, row + 2f, 80f, rowH - 6f), slot.ai ? "AI: On" : "AI: Off", slot.ai, aiBtn, UITheme.GoodTint))
                            _s.SetSlotAi(slot.slot, !slot.ai);
                    }
                }
                row += rowH;
            }
        }

        // Home/Away position columns for Match (Pro Clubs style): one cell per seatable shirt
        // (NetSession.SlotAllowed already caps this at ScrimPerSide per team, so Roster only ever
        // carries the real seats - a 3v3 lobby shows 3 cells a side, not four). A click on an open
        // cell is the exact same RequestSlot the old flat Claim button sent; the host's per-slot
        // AI toggle is unchanged too. Laid out in the same vertical band the flat list used, so no
        // panel resize was needed: ScrimPerSide tops out at ScrimSlotsPerTeam (4), one cell per
        // shirt, well inside the height the old up-to-eight-row flat list already budgeted for.
        void DrawMatchTeams(float lx, float top, float lw)
        {
            var head = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            var name = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            var tag = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            var claimBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
            const float cellH = 46f, gap = 16f;
            float colW = (lw - gap) * 0.5f, homeX = lx, awayX = lx + colW + gap;

            GUI.Label(new Rect(homeX, top, colW, 18f), "HOME", head);
            GUI.Label(new Rect(awayX, top, colW, 18f), "AWAY", head);
            float gridTop = top + 20f;

            int perSide = _s.Config.perSide;
            var roster = _s.Roster;
            for (int i = 0; i < roster.Length; i++)
            {
                var slot = roster[i];
                int team = NetSession.ScrimTeamOfSlot(slot.slot);
                int shirt = NetSession.ScrimShirtOfSlot(slot.slot);
                float cx = team == 0 ? homeX : awayX;
                float cy = gridTop + shirt * cellH;
                bool isMe = slot.slot == _s.LocalSlot;
                string pos = SimConfig.PositionName(perSide, shirt);

                if (isMe)
                {
                    UITheme.Fill(new Rect(cx - 6f, cy, colW + 12f, cellH - 4f), new Color(0.14f, 0.28f, 0.48f, 0.55f));
                    UITheme.Fill(new Rect(cx - 6f, cy, 2.5f, cellH - 4f), UITheme.Gold);
                }
                else UITheme.Divider(cx, cy + cellH - 4f, colW);

                GUI.Label(new Rect(cx + 6f, cy, colW - 12f, 20f), $"{pos}  -  {slot.name}{(isMe ? "  (you)" : "")}", name);

                if (slot.human)
                {
                    Color rc = slot.ready ? UITheme.Green : UITheme.Gold;
                    tag.normal.textColor = rc;
                    string rt = slot.ready ? "READY" : "not ready";
                    float rw = tag.CalcSize(new GUIContent(rt)).x;
                    UITheme.Dot(cx + colW - rw - 8f, cy + 20f + 9f, rc, 2.2f);
                    GUI.Label(new Rect(cx + 6f, cy + 20f, colW - 12f, 18f), rt, tag);
                }
                else
                {
                    float bx = cx + colW;
                    if (!isMe)
                    {
                        bx -= 76f;
                        if (UITheme.Button(new Rect(bx, cy + 20f, 76f, 20f), "Claim", claimBtn))
                            _s.RequestSlot(slot.slot);
                    }
                    if (_s.IsHost)
                    {
                        bx -= 62f;
                        var aiBtn = new GUIStyle(claimBtn);
                        aiBtn.normal.textColor = slot.ai ? UITheme.Green : UITheme.Faint;
                        if (UITheme.Toggle(new Rect(bx, cy + 20f, 58f, 20f), slot.ai ? "AI:On" : "AI:Off", slot.ai, aiBtn, UITheme.GoodTint))
                            _s.SetSlotAi(slot.slot, !slot.ai);
                    }
                }
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

        /// <summary>
        /// Whether this lobby is currently answering discovery probes, and if not, which one of the
        /// three reasons it is. Mirrors NetSession.BuildAdvert's `visible` term for term - if the two
        /// ever disagree the host is being lied to, which is worse than showing nothing.
        ///
        /// Reported in the order the host can act on: their own Public setting first (a choice), then
        /// the match having started (irreversible), then the lobby being full (self-correcting).
        /// </summary>
        string VisibilityLine(out Color colour)
        {
            var listed = UITheme.Green;
            var hidden = UITheme.Gold;
            if (!_s.Config.publicLobby)
            {
                colour = hidden;
                return "PRIVATE  -  join by code only.";
            }
            if (_s.MatchStarted)
            {
                colour = hidden;
                return "IN PROGRESS  -  nobody can join.";
            }
            if (!_s.HasFreeSlot)
            {
                colour = hidden;
                return "FULL  -  hidden until a slot opens.";
            }
            colour = listed;
            return "LISTED  -  visible on your tailnet and LAN.";
        }

        string ConfigLine()
        {
            var c = _s.Config;
            var mode = (GameMode)c.mode;
            string stadium = c.stadium < StadiumStyle.All.Length ? StadiumStyle.All[c.stadium].Name : "?";
            if (mode == GameMode.Match)
                return $"Match  {c.perSide}v{c.perSide}   {stadium}   {c.matchSec / 60} min";
            return $"{mode}   {stadium}";
        }
    }
}
