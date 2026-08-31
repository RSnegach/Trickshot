using UnityEngine;
using System.Collections.Generic;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Online (ranked drop-in): pick a playlist (3v3/5v5/11v11), then get auto-matched with
    /// strangers - no lobby browser, no host settings screen, matching a real drop-in queue.
    /// Sweeps for an existing Online-flagged lobby in the chosen playlist (NetSession.ModeLabel()
    /// already encodes both "Online" and the NvN size into the discovery-probe mode string, via
    /// MatchConfig.onlineRanked), joins it if found, auto-hosts one with fixed defaults if not.
    /// GrantSlot (NetSession.cs) already seats a joining human into the first open position, so
    /// no manual position-claim step is needed here - dropping in is automatic end to end. Hands
    /// off to LobbyUI once seated either way.
    /// </summary>
    public class OnlineQueueUI : MonoBehaviour
    {
        enum Phase { Playlist, Searching, Connecting }
        Phase _phase = Phase.Playlist;

        int _perSide;
        string _status = "";
        bool _dead;

        System.Action _onJoinedLobby, _onBack;

        bool _sweeping;
        float _sweepTimer;
        float _connectDeadline;

        const float SweepInterval = 2f;
        const float ConnectTimeout = 8f;

        static readonly int[] Playlists = { 3, 5, 11 };

        public void Init(System.Action onJoinedLobby, System.Action onBack)
        {
            _onJoinedLobby = onJoinedLobby; _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        void Update()
        {
            Multiplayer.BrowsePoll();
            if (_phase == Phase.Playlist) return;

            if (_phase == Phase.Connecting)
            {
                Multiplayer.Poll();
                var s = Multiplayer.Session;
                if (s == null) { _phase = Phase.Searching; _status = "Connection failed. Retrying..."; return; }
                if (s.SlotAnswered)
                {
                    if (s.SlotRefused) { Multiplayer.End(); _phase = Phase.Searching; _status = "Lobby full. Retrying..."; return; }
                    enabled = false; _onJoinedLobby?.Invoke();
                    return;
                }
                if (Time.unscaledTime > _connectDeadline) { Multiplayer.End(); _phase = Phase.Searching; _status = "Timed out. Retrying..."; }
                return;
            }

            // Searching: sweep on a timer, one sweep in flight at a time.
            _sweepTimer -= Time.unscaledDeltaTime;
            if (!_sweeping && _sweepTimer <= 0f) Sweep();
        }

        void StartQueue(int perSide)
        {
            _perSide = perSide;
            _phase = Phase.Searching;
            _status = "Searching for a match...";
            _sweepTimer = 0f;
            Sweep();
        }

        void Sweep()
        {
            _sweeping = true;
            Multiplayer.Browse(list =>
            {
                _sweeping = false;
                _sweepTimer = SweepInterval;
                if (_dead || _phase != Phase.Searching) return;   // cancelled or already connecting

                string wanted = "Online Match " + _perSide + "v" + _perSide;
                if (list != null)
                {
                    foreach (var l in list)
                    {
                        if (l.mode == wanted && l.players < l.maxPlayers)
                        {
                            Multiplayer.Join(l.handle);
                            _phase = Phase.Connecting;
                            _connectDeadline = Time.unscaledTime + ConnectTimeout;
                            _status = "Joining...";
                            return;
                        }
                    }
                }
                HostOnline();
            });
        }

        void HostOnline()
        {
            int maxPlayers = Mathf.Clamp(_perSide * 2, 2, 8);
            Multiplayer.Host(maxPlayers);
            if (Multiplayer.Session == null || !Multiplayer.Session.Active)
            {
                Multiplayer.End();
                _status = "Couldn't host. Retrying...";
                return;   // stay in Searching; the next sweep tries again
            }
            Multiplayer.Session.SetConfig(new MatchConfig
            {
                mode = (byte)GameMode.Match,
                stadium = (byte)StadiumStyle.SelectedIndex,
                perSide = (byte)_perSide,
                matchSec = (ushort)(5 * 60),
                publicLobby = true,
                goalScale = 1f,
                keeperAbility = 0.5f,
                onlineRanked = true,
            });
            enabled = false; _onJoinedLobby?.Invoke();
        }

        void Cancel()
        {
            _dead = true;
            Multiplayer.End();
            enabled = false; _onBack?.Invoke();
        }

        void OnGUI()
        {
            MenuScale.Begin();
            if (_phase == Phase.Playlist) DrawPlaylist();
            else DrawSearching();
            MenuScale.End();
        }

        void DrawPlaylist()
        {
            float w = 340f, h = 78f, gap = 20f;
            float cx = MenuScale.Width * 0.5f - w * 0.5f;
            float cy = MenuScale.Height * 0.5f - ((h + gap) * Playlists.Length) * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.30f, w + 380f);
            UITheme.Title(new Rect(0, cy - 110f, MenuScale.Width, 80f), "ONLINE", 48);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Faint } };

            for (int i = 0; i < Playlists.Length; i++)
            {
                int n = Playlists[i];
                float by = cy + i * (h + gap);
                var r = CareerStats.RankFor(n);
                string rankLine = r.MatchesPlayed > 0
                    ? CareerStats.RankTierName(r) + " - " + Mathf.RoundToInt(r.Mmr) + " MMR"
                    : "Unranked";
                if (UITheme.Button(new Rect(cx, by, w, h), n + "v" + n, btn)) StartQueue(n);
                GUI.Label(new Rect(cx, by + h - 24f, w, 18f), rankLine, sub);
            }

            float backY = cy + (h + gap) * Playlists.Length;
            if (UITheme.Button(new Rect(cx, backY, w, h * 0.6f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
        }

        void DrawSearching()
        {
            float w = 420f, h = 90f;
            float cx = MenuScale.Width * 0.5f - w * 0.5f;
            float cy = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.30f, w + 200f);
            UITheme.Title(new Rect(0, cy - 90f, MenuScale.Width, 60f), _perSide + "v" + _perSide + " ONLINE", 36);

            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            GUI.Label(new Rect(cx, cy, w, 30f), _status, st);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(cx + w * 0.5f - 160f, cy + 50f, 320f, 56f), "Cancel", btn)) Cancel();
        }
    }
}
