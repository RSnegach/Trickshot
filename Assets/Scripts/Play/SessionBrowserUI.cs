using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Session browser: lists joinable lobbies (name / mode / players), Refresh + Join +
    /// Back. Routes through Multiplayer.Browse and Multiplayer.Join(handle). On a successful join it
    /// hands off to the lobby.
    ///
    /// Discovery is a real sweep now, not a Steam-only stub: Multiplayer.Browse enumerates the tailnet
    /// and probes each peer plus the LAN (see TailnetDiscovery), so a friend already on your tailnet
    /// simply APPEARS here. Two consequences shape this screen:
    ///   - Results are ASYNCHRONOUS (a worker thread and a ~1s reply window), so BrowsePoll must run
    ///     every frame and the screen has a visible scanning state instead of pretending to be instant.
    ///   - Discovery can fail in several distinct ways that need different actions from the player, so
    ///     the empty state NAMES the reason (see BrowseStatus) rather than shrugging. The invite box
    ///     stays regardless: it is the one path that works with no Tailscale, no LAN and no discovery.
    /// </summary>
    public class SessionBrowserUI : MonoBehaviour
    {
        System.Action _onBack;
        System.Action _onJoined;   // invoked once we've asked to join a lobby -> show lobby

        readonly List<LobbyInfo> _lobbies = new List<LobbyInfo>();
        // Role filter: show only lobbies LOOKING FOR every role ticked here (see LookingRole).
        // 0 = off, which is the default and shows everything. Purely a local view filter - it never
        // changes what discovery sweeps for, so clearing it re-reveals rows with no new sweep.
        byte _wantRoles;
        // _lobbies filtered by _wantRoles, rebuilt each frame it is drawn. Rows are SELECTED out of
        // this list, so _sel indexes the VISIBLE rows, never the raw sweep results.
        readonly List<LobbyInfo> _shown = new List<LobbyInfo>();
        int _sel = -1;
        ulong _selHandle;          // selection follows the LOBBY, not the row index (see Refresh)
        float _autoRefresh;
        string _ipText = "";        // direct-IP join box (invite code, IP, or hostname)
        string _ipError = "";       // shown when the typed address won't parse OR a join fails
        bool _swept;                // at least one sweep has come back, so "found nothing" is real

        // A sweep spawns the Tailscale CLI and waits ~1s for probe replies, so refreshing on the old
        // 1.5s cadence would keep a process spawn permanently in flight for no benefit: lobbies do not
        // appear and vanish on that timescale. (TailnetDiscovery caches the peer list for 15s, so the
        // CLI cost is amortised anyway, but the socket work is not.)
        const float AutoRefreshSeconds = 3f;

        // Join is a two-step handshake, not instant: after Multiplayer.Join we WAIT for the host to
        // assign us a real player slot (see NetSession.SlotRefused) before showing the lobby. Without this the client used to
        // pop straight into its own empty local lobby whether or not the host was ever reached (the
        // "joined an empty lobby that wasn't mine" bug). If no slot arrives before the deadline the
        // host is unreachable (firewall / wrong IP / Tailscale down): tear down + show why.
        bool _connecting;
        float _connectDeadline;
        string _connectLabel = "";
        // The transport's own 5s PeerTimeout is the PRIMARY failure signal, not this deadline: an
        // unreachable host produces zero inbound packets, so DirectIpTransport drops it at 5s and
        // Multiplayer tears the session down, which this loop sees as s == null. This 8s value is only
        // the backstop for a transport that somehow stays up without ever completing the handshake.
        // (The comment here used to read "> the transport's 5s peer timeout, room for reliable
        // resends", which had the ordering backwards - the transport always wins the race.)
        const float ConnectTimeout = 8f;

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
            Multiplayer.Browse(list =>
            {
                _lobbies.Clear();
                _lobbies.AddRange(list);
                _swept = true;
                ApplyFilter();
            });
        }

        /// <summary>
        /// Rebuild the visible row list from the last sweep + the role filter, then re-find the
        /// selection BY HANDLE (see Refresh: row order is network timing and reorders freely, so a
        /// held index would silently move the selection onto a different lobby). Called after every
        /// sweep AND whenever the filter changes, so ticking a role never leaves _sel pointing at a
        /// row that is no longer shown.
        /// </summary>
        void ApplyFilter()
        {
            _shown.Clear();
            for (int i = 0; i < _lobbies.Count; i++)
            {
                // A lobby matches when it is looking for EVERY role the searcher ticked (AND, not
                // OR): ticking Sniper+Referee means "somewhere I can do both", which is the reading
                // that never puts a lobby in front of someone it cannot seat.
                byte has = LookingRoles.Parse(_lobbies[i].mode);
                if ((has & _wantRoles) == _wantRoles) _shown.Add(_lobbies[i]);
            }
            _sel = -1;
            if (_selHandle != 0)
                for (int i = 0; i < _shown.Count; i++)
                    if (_shown[i].handle == _selHandle) { _sel = i; break; }
            // Nothing picked yet (or the pick is filtered away): preselect the top row so Join is
            // usable immediately, which is what a one-lobby list almost always is.
            if (_sel < 0 && _shown.Count > 0) { _sel = 0; _selHandle = _shown[0].handle; }
            if (_shown.Count == 0) _selHandle = 0;
        }

        // Parse the typed invite code OR "ip" / "ip:port" into a join handle and connect. The handle
        // encodes the endpoint (see NetEndpoint); Multiplayer.Join routes it to the direct-IP
        // transport. Accepting both means a friend can paste the host's invite code without ever
        // being told what an IP or a port is.
        void TryJoinByIp()
        {
            if (!NetEndpoint.TryParseAny(_ipText, out var handle))
            {
                _ipError = "Not recognised. Use an invite code, an IP, or the host PC name.";
                return;
            }
            StartConnect(handle, _ipText.Trim());
        }

        void Update()
        {
            // Hand finished sweeps to the main thread. Unconditionally, INCLUDING while connecting: a
            // sweep in flight when Join is pressed still has to be drained, because draining is what
            // releases the discovery slot - leave it stuck and the browser would never scan again for
            // the rest of the process.
            Multiplayer.BrowsePoll();

            if (_connecting)
            {
                // Pump the transport so the connect + Hello/AssignSlot handshake progresses.
                Multiplayer.Poll();
                var s = Multiplayer.Session;
                // REFUSED: the host answered, but with no player slot (255/spectator) - the lobby is
                // full or a match is already in progress. Spectating isn't implemented and the match
                // drivers clamp the slot into range, which would silently put us on slot 0's body,
                // so bail out with the reason instead of entering the lobby.
                //
                // s.SlotAnswered is what makes this branch mean "refused" instead of "not yet", and
                // it is load-bearing. Without it both halves were ALREADY true on the first frame of
                // every join: a fresh NetSession starts at LocalSlot = -1 / LocalRole = Spectator
                // (NetSession.cs:39-40), which is exactly the state a real AssignSlot(255, Spectator)
                // refusal produces. So this fired ~16ms after the button press, before any packet
                // could make a round trip, and CancelConnect closed the UDP socket one line after
                // Poll() had queued our Hello. The host would receive it, grant a slot, reply into a
                // dead socket, and time the ghost peer out 5s later. Joining was impossible 100% of
                // the time, the "Connecting..." overlay never rendered a single frame, and the error
                // blamed a full lobby that was in fact empty and waiting.
                if (s != null && s.SlotAnswered && s.SlotRefused)
                {
                    var why = s.RefusedBecause;
                    CancelConnect();
                    // Name the real cause. Each of these is a different action for the player, and
                    // reporting all three as a full lobby sent them to wait for a slot that was
                    // never the problem.
                    if (why == JoinRefusal.Version)
                        _ipError = "Version mismatch. Both players need the same build.";
                    else if (why == JoinRefusal.MatchRunning)
                        _ipError = _connectLabel + " is mid-match. Try the next lobby.";
                    else
                        _ipError = _connectLabel + " has no free slot.";
                    return;
                }
                // Success: the host assigned us a real player slot -> we're in their lobby.
                if (s != null && s.SlotAnswered && !s.SlotRefused)
                {
                    _connecting = false;
                    enabled = false; _onJoined?.Invoke();
                    return;
                }
                // Failure: the transport dropped (host timed out) or our deadline passed.
                if (s == null || !s.Active || Time.unscaledTime >= _connectDeadline)
                {
                    CancelConnect();
                    // Name the three things that actually cause this, in the order they bite:
                    // the host's firewall silently dropping inbound UDP is by far the most common.
                    _ipError = "Couldn't reach " + _connectLabel + ". Host must allow inbound UDP "
                             + NetEndpoint.DefaultPort + ".";
                }
                return;   // don't refresh/list while connecting
            }

            // Light auto-refresh so a lobby hosted moments ago shows up.
            _autoRefresh -= Time.unscaledDeltaTime;
            if (_autoRefresh <= 0f) { _autoRefresh = AutoRefreshSeconds; Refresh(); }
        }

        /// <summary>
        /// What to tell the player when the list is empty (or thin). Each branch is a DIFFERENT thing
        /// to go and do, which is the entire point of distinguishing them: "install Tailscale", "start
        /// Tailscale", "add your friend's device to the tailnet" and "wait for someone to host" all look
        /// identical as an empty list, and the old copy answered all four with a shrug about Steam.
        /// </summary>
        string BrowseStatus()
        {
            if (Multiplayer.SteamLinked)
                return "No Steam lobbies open. Use an invite code below.";
            if (!Multiplayer.UseDirectIp)
                return "Loopback transport. Only sessions in this process are listed.";
            if (!_swept || TailnetDiscovery.Scanning)
                return "Scanning Tailscale and LAN...";

            switch (TailnetDiscovery.LastReason)
            {
                case TailnetDiscovery.Reason.NoCli:
                    // Distinguish the two ways the CLI can be missing, because only one is the player's
                    // to fix. An adapter with no command means an install we cannot drive; no adapter
                    // and no command means Tailscale simply is not here.
                    return TailnetDiscovery.HasTailnet
                        ? "Tailscale program not found. Paste the host's invite code below."
                        : "No Tailscale here. LAN only. Invite codes still work.";

                case TailnetDiscovery.Reason.TailnetDown:
                    return "Tailscale is not connected. Sign in, then Refresh.";

                case TailnetDiscovery.Reason.NoPeers:
                    return TailnetDiscovery.HasTailnet
                        ? "No other devices on your tailnet. Invite your friend, then Refresh."
                        : "No LAN sessions. Internet play needs Tailscale on both PCs.";

                default:
                    // Sweep worked, peers answered, nobody is hosting a JOINABLE PUBLIC lobby. Say so
                    // exactly, because a host with "Public" off, a full lobby or a started match are all
                    // invisible by design and would otherwise look like a broken list.
                    return "Nobody is hosting an open session.";
            }
        }

        /// <summary>One-line footer once rows exist: says what was searched, so a SHORT list still
        /// reads as a working search rather than a suspiciously empty one.</summary>
        string FoundLine()
        {
            // Count what is VISIBLE, and say so when a filter is hiding the rest - "2 sessions
            // found" beside a list of two, out of nine swept, is a true sentence that reads false.
            string what = _shown.Count == 1 ? "1 session" : _shown.Count + " sessions";
            if (_wantRoles != 0 && _shown.Count != _lobbies.Count)
                what += " of " + _lobbies.Count;
            if (Multiplayer.SteamLinked || !Multiplayer.UseDirectIp) return what + " found";
            int peers = TailnetDiscovery.PeerCount;
            string where = TailnetDiscovery.HasTailnet
                ? (peers == 1 ? "1 Tailscale device + LAN" : peers + " Tailscale devices + LAN")
                : "your LAN";
            return what + " found on " + where + (TailnetDiscovery.Scanning ? "  (scanning...)" : "");
        }

        // Scaled up on big displays like the other pre-match menus (see MenuScale); the fixed sizes
        // below are unchanged, they just cover more of the screen. Wrapped so any early return
        // inside DrawBrowser can't leak the scaled GUI matrix.
        void OnGUI()
        {
            MenuScale.Begin();
            DrawBrowser();
            MenuScale.End();
        }

        void DrawBrowser()
        {
            float w = 560f, rowH = 46f, gap = 8f;
            float panelH = 150f + 6 * (rowH + gap) + 60f + 78f + 58f;   // +78 join row, +58 role filter
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, w + 300f);
            UITheme.Panel(new Rect(x, y, w, panelH), UITheme.Blue);

            UITheme.Title(new Rect(x, y + 14f, w, 40f), "FIND A SESSION", 30);

            var rowName = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var rowNameSel = new GUIStyle(rowName); rowNameSel.normal.textColor = UITheme.Gold;
            var meta = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Dim } };

            float row = y + 66f, lx = x + 24f, lw = w - 48f;

            // ---- Role filter: narrow the list to hosts looking for what the searcher wants to
            // play. Off by default (every lobby shown); ticking a role hides lobbies not asking for
            // it. Drawn ABOVE the rows so it reads as a control over the list beneath it.
            UITheme.Label(new Rect(lx, row, lw, 18f), "Looking to play as:", 
                          new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UITheme.Dim } });
            {
                var names = LookingRoles.Names;
                float bw = (lw - 6f * (names.Length - 1)) / names.Length;
                var fSt = new GUIStyle(GUI.skin.button) { fontSize = 12 };
                for (int i = 0; i < names.Length; i++)
                {
                    byte bit = (byte)LookingRoles.All[i];
                    bool on = (_wantRoles & bit) != 0;
                    var fs = new GUIStyle(fSt) { fontStyle = on ? FontStyle.Bold : FontStyle.Normal };
                    if (on) fs.normal.textColor = UITheme.Gold;
                    if (UITheme.Toggle(new Rect(lx + i * (bw + 6f), row + 20f, bw, 26f), names[i], on, fs))
                    {
                        _wantRoles ^= bit;   // independent checkboxes, not a one-of-N picker
                        ApplyFilter();       // re-filter NOW so the list reacts to the click
                    }
                }
            }
            UITheme.Divider(lx, row + 50f, lw);
            row += 58f;

            if (_shown.Count == 0)
            {
                var empty = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
                // Given the full row area, not 56px: these messages name a specific thing to go and do
                // and the old fixed height clipped that half away.
                var er = new Rect(lx, row, lw, 6f * (rowH + gap) - 8f);
                // Spin a ring while a sweep is in flight: an empty list plus motion reads as "looking",
                // which is the difference between patience and assuming the screen is broken.
                bool sweeping = !_swept || TailnetDiscovery.Scanning;
                if (sweeping) UITheme.Spinner(new Rect(er.center.x - 16f, er.y + 18f, 32f, 32f), UITheme.Gold);
                // A filter that hid every row is NOT a discovery failure, and saying "nobody is
                // hosting" there would send the player to fix a network that is working fine.
                string why = _lobbies.Count > 0 && _wantRoles != 0
                    ? "No open session is looking for that. Untick a role, or host one yourself."
                    : BrowseStatus();
                UITheme.Label(new Rect(er.x, er.y + (sweeping ? 44f : 0f), er.width, er.height - (sweeping ? 44f : 0f)),
                          why, empty);
            }
            for (int i = 0; i < _shown.Count && i < 6; i++)
            {
                var l = _shown[i];
                bool sel = i == _sel;
                var r = new Rect(lx, row, lw, rowH);
                if (UITheme.Toggle(r, "      " + (string.IsNullOrEmpty(l.name) ? "Session" : l.name), sel, sel ? rowNameSel : rowName))
                { _sel = i; _selHandle = l.handle; }
                // Green while the lobby has room, gold once it is full: the joinable ones stand out.
                UITheme.Dot(r.x + 16f, r.center.y, l.players < l.maxPlayers ? UITheme.Green : UITheme.Gold, 2.5f);
                UITheme.Label(new Rect(r.x, r.y, r.width - 14f, rowH), $"{l.mode}    {l.players}/{l.maxPlayers}  ", meta);
                row += rowH + gap;
            }
            if (_shown.Count > 0)
                UITheme.Hint(new Rect(lx, row - 2f, lw, 18f), FoundLine(), TextAnchor.MiddleLeft);

            // ---- Direct join: the path that works when discovery cannot. Kept in front of the player
            // permanently rather than hidden behind a failure, because it needs nothing from the
            // network stack: no Tailscale CLI, no broadcast, no host answering a probe.
            float ipY = y + panelH - 52f - 78f;
            UITheme.Divider(lx, ipY - 8f, lw);
            UITheme.Section(new Rect(lx, ipY, lw, 20f), "OR JOIN DIRECTLY  -  INVITE CODE, IP, OR PC NAME");

            var ipField = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            // Enter inside the field joins too (the reflex after pasting). Read the event BEFORE
            // TextField consumes the keystroke.
            bool submit = Event.current.type == EventType.KeyDown
                       && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                       && GUI.GetNameOfFocusedControl() == "joinbox";
            GUI.SetNextControlName("joinbox");
            _ipText = GUI.TextField(new Rect(lx, ipY + 22f, lw - 150f, 34f), _ipText, 32, ipField);
            if (submit) { Event.current.Use(); TryJoinByIp(); }
            // Ctrl+V into an IMGUI TextField only works while it has focus; a friend pasting an
            // invite code shouldn't have to click the box first, so offer an explicit Paste.
            if (UITheme.Button(new Rect(lx + lw - 140f, ipY - 2f, 66f, 20f), "Paste",
                               new GUIStyle(GUI.skin.button) { fontSize = 11 }))
                _ipText = GUIUtility.systemCopyBuffer ?? "";
            if (UITheme.Button(new Rect(lx + lw - 140f, ipY + 22f, 140f, 34f), "Join",
                               new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold }))
                TryJoinByIp();
            if (!string.IsNullOrEmpty(_ipError))
            {
                // wordWrap + real height: these messages name several causes and used to be
                // clipped to a single 18px line, hiding the part that says what to fix.
                var err = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Red } };
                UITheme.Chip(new Rect(lx - 6f, ipY + 55f, lw + 12f, 50f), new Color(0.22f, 0.07f, 0.07f, 0.9f), UITheme.Red);
                UITheme.Label(new Rect(lx, ipY + 58f, lw, 46f), _ipError, err);
            }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
            // Sit low, near the screen bottom, but never overlap the panel: on a short window the
            // panel can reach past that line, so push below it and clamp so the row always fits.
            float by = Mathf.Min(MenuScale.Height - 52f,
                                 Mathf.Max(MenuScale.Height - 64f, y + panelH + 16f));
            if (UITheme.Button(new Rect(x + 24f, by, 130f, 40f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            // A sweep takes about a second, so say so on the button rather than looking unresponsive.
            // Left ENABLED: pressing it mid-sweep is a harmless no-op (Sweep ignores a re-entrant call)
            // and a greyed button during every auto-refresh would flicker.
            bool scanning = Multiplayer.UseDirectIp && !Multiplayer.SteamLinked && TailnetDiscovery.Scanning;
            var rr = new Rect(x + w * 0.5f - 65f, by, 130f, 40f);
            if (UITheme.Button(rr, scanning ? "Scanning" : "Refresh", btn)) Refresh();
            if (scanning) UITheme.Spinner(new Rect(rr.x - 34f, rr.center.y - 11f, 22f, 22f), UITheme.Gold);

            bool canJoin = _sel >= 0 && _sel < _shown.Count;
            GUI.enabled = canJoin;
            var keepJoin = GUI.backgroundColor;
            if (canJoin) GUI.backgroundColor = UITheme.GoodTint;
            if (UITheme.Button(new Rect(x + w - 154f, by, 130f, 40f), "Join", btn))
                StartConnect(_shown[_sel].handle, _shown[_sel].name);
            GUI.backgroundColor = keepJoin;
            GUI.enabled = true;

            // Connecting overlay: block the panel + show progress while we wait for the host.
            if (_connecting) DrawConnecting();
        }

        // Modal "Connecting…" overlay shown between clicking Join and the host assigning us a slot.
        void DrawConnecting()
        {
            UITheme.Fill(new Rect(0, 0, MenuScale.Width, MenuScale.Height), new Color(0f, 0f, 0f, 0.72f));

            float w = 420f, h = 150f;
            float px = MenuScale.Width * 0.5f - w * 0.5f, py = MenuScale.Height * 0.5f - h * 0.5f;
            UITheme.Panel(new Rect(px, py, w, h), UITheme.Blue);

            int dots = ((int)(Time.unscaledTime * 2f) % 4);
            var msg = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            UITheme.Spinner(new Rect(px + 22f, py + 30f, 26f, 26f), UITheme.Blue);
            UITheme.Shadowed(new Rect(px, py + 28f, w, 30f),
                             "Connecting to " + _connectLabel + new string('.', dots), msg, UITheme.Ink, 0.7f, 1.5f);
            UITheme.Hint(new Rect(px + 20f, py + 60f, w - 40f, 20f), "Waiting for the host to respond");

            var cancel = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(px + w * 0.5f - 65f, py + h - 46f, 130f, 34f), "Cancel", cancel, true))
                CancelConnect();
        }
    }
}
