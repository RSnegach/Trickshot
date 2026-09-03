using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// INVITE FRIENDS modal, opened from the lobby's "Invite Friends" button. Lists the host's
    /// Steam friends and gives each one its own Invite button that sends a real Steam invite
    /// notification for THIS lobby (SteamFriendsAPI.InviteToLobby -> SteamMatchmaking
    /// .InviteUserToLobby). No Steam overlay is involved, so the interaction stays in the game.
    ///
    /// Static, not a MonoBehaviour, exactly like FriendsPanelUI: LobbyUI already runs its own
    /// OnGUI, and all this needs is a small cache of the last-fetched list plus per-row send state.
    /// LobbyUI owns the open/closed flag and calls Draw while it is set.
    ///
    /// HONESTY RULES, because an invite is a promise that something reached another person:
    ///   * offline friends are listed but cannot be invited (Steam delivers nothing to them);
    ///   * a send that returns false says FAILED on that row - never a silent success;
    ///   * with no Steamworks SDK linked the list is empty and says so, rather than showing
    ///     placeholder people who cannot be invited.
    /// </summary>
    public static class InviteFriendsUI
    {
        static List<SteamFriendInfo> _friends;      // null = still loading
        static bool _loading;
        static float _refreshedAt;

        // Per-friend send state, keyed by steamId, so a row can report what happened to ITS invite
        // rather than the panel carrying one shared status line for everybody.
        enum Sent { None, Ok, Failed }
        static readonly Dictionary<ulong, Sent> _sent = new Dictionary<ulong, Sent>();
        static readonly Dictionary<ulong, float> _sentAt = new Dictionary<ulong, float>();

        const float SentHold = 4f;    // seconds a row keeps showing its result

        // Scroll position for rosters longer than the panel.
        static Vector2 _scroll;

        /// <summary>Call on the frame the panel opens. Clears stale results and refetches.</summary>
        public static void OnOpened()
        {
            _sent.Clear();
            _sentAt.Clear();
            _scroll = Vector2.zero;
            Refresh();
        }

        static void Refresh()
        {
            _friends = null;          // shows "Loading..." rather than a stale list mid-fetch
            _loading = true;
            _refreshedAt = Time.unscaledTime;
            SteamFriendsAPI.RequestFriendsList(list =>
            {
                // Most useful first: friends already in Trickshot, then online, then the rest,
                // alphabetical inside each band. An invite to someone already playing is the one
                // most likely to be accepted.
                list.Sort((a, b) =>
                {
                    if (a.playingTrickshot != b.playingTrickshot) return a.playingTrickshot ? -1 : 1;
                    if (a.online != b.online) return a.online ? -1 : 1;
                    return string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
                });
                _friends = list;
                _loading = false;
            });
        }

        /// <summary>
        /// Draw the modal centred on the virtual screen. Call inside the caller's own
        /// MenuScale/Hud block. `onClose` fires on the X, on Done, or on a click outside the card.
        /// </summary>
        public static void Draw(System.Action onClose)
        {
            const float w = 460f, h = 420f;
            float x = MenuScale.Width * 0.5f - w * 0.5f;
            float y = MenuScale.Height * 0.5f - h * 0.5f;
            var card = new Rect(x, y, w, h);

            // Dim everything behind, and swallow clicks that land outside the card so the lobby
            // underneath cannot be operated through the modal (the same contract MenuUI's own
            // overlays use - see UITheme.ClickBlocker).
            //
            // The close is LATCHED rather than returned on: every control below has to be drawn on
            // every event or IMGUI's ids shift between the layout and repaint passes and every
            // click on the screen breaks. The panel closes at the end of the frame instead.
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.62f, w + 260f);
            bool close = UITheme.ClickBlocker(MenuScale.Width, MenuScale.Height, card, card);

            UITheme.Panel(card, UITheme.Blue);
            UITheme.Title(new Rect(x, y + 12f, w, 32f), "INVITE FRIENDS", 26);

            var closeBtn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(x + w - 36f, y + 12f, 24f, 24f), "x", closeBtn)) close = true;

            float lx = x + 22f, lw = w - 44f;
            UITheme.Divider(lx, y + 48f, lw);

            float listTop = y + 58f;
            float listH = h - (listTop - y) - 62f;
            var listRect = new Rect(lx, listTop, lw, listH);

            // ---- states that are not a list -------------------------------------------------
            // Each of these draws a message INSTEAD of the rows, but the footer below is drawn
            // either way, so the control count only varies by the row buttons - which is safe,
            // because a state change also refetches and cannot happen between two passes of one
            // frame (nothing here mutates _friends during Draw).
            bool haveList = SteamFriendsAPI.Available && !_loading && _friends != null && _friends.Count > 0;
            bool canRefresh = SteamFriendsAPI.Available && !_loading;

            if (!SteamFriendsAPI.Available)
                Note(listRect,
                     "Steam is not connected.\n\nFriends and invites need the game launched through Steam. " +
                     "You can still invite people with the lobby's invite code.");
            else if (_loading || _friends == null)
                Note(listRect, "Loading friends...");
            else if (_friends.Count == 0)
                Note(listRect, "No Steam friends found.\n\nIf you just signed in, give Steam a moment and hit Refresh.");

            if (!haveList)
            {
                DrawFooter(x, y, w, h, ref close, canRefresh);
                if (close) onClose?.Invoke();
                return;
            }

            // ---- the list -------------------------------------------------------------------
            // A lobby that has not finished being created cannot take an invite yet. That is a
            // DIFFERENT problem from Steam being absent, so it is said here rather than folded
            // into the empty state, and the rows stay visible while it resolves.
            bool canInvite = SteamFriendsAPI.CanInvite;
            if (!canInvite)
            {
                var warn = new GUIStyle(GUI.skin.label)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
                UITheme.Label(new Rect(lx, listTop, lw, 16f), "Creating lobby - invites available in a moment...", warn);
                listTop += 18f; listH -= 18f;
            }

            const float rowH = 40f;
            float viewH = _friends.Count * rowH;
            var view = new Rect(0f, 0f, lw - (viewH > listH ? 16f : 0f), viewH);
            _scroll = GUI.BeginScrollView(new Rect(lx, listTop, lw, listH), _scroll, view, false, viewH > listH);

            var nameSt = new GUIStyle(GUI.skin.label)
            { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            var tagSt = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            var btnSt = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };

            for (int i = 0; i < _friends.Count; i++)
            {
                var f = _friends[i];
                float ry = i * rowH;
                var row = new Rect(0f, ry, view.width, rowH);
                if ((i & 1) == 0) UITheme.Fill(row, new Color(1f, 1f, 1f, 0.03f));

                // Status dot + name.
                Color dot = f.playingTrickshot ? UITheme.Green : f.online ? UITheme.Gold : UITheme.Faint;
                UITheme.Dot(row.x + 12f, row.center.y, dot, 4f);
                UITheme.Label(new Rect(row.x + 24f, ry + 4f, row.width - 130f, 20f), f.name, nameSt);

                tagSt.normal.textColor = dot;
                UITheme.Label(new Rect(row.x + 24f, ry + 22f, row.width - 130f, 14f),
                              f.playingTrickshot ? "IN TRICKSHOT" : f.online ? "ONLINE" : "OFFLINE", tagSt);

                // Per-row result, or the Invite button. An offline friend keeps a disabled button
                // rather than none at all, so the row does not reshuffle when they come online.
                var br = new Rect(row.xMax - 96f, ry + 7f, 88f, rowH - 14f);
                Sent state = SentState(f.steamId);
                if (state != Sent.None)
                {
                    var res = new GUIStyle(GUI.skin.label)
                    { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                      normal = { textColor = state == Sent.Ok ? UITheme.Green : UITheme.Red } };
                    UITheme.Label(br, state == Sent.Ok ? "Invited" : "Failed", res);
                }
                else
                {
                    GUI.enabled = canInvite && f.online;
                    if (UITheme.Button(br, "Invite", btnSt))
                    {
                        bool ok = SteamFriendsAPI.InviteToLobby(f.steamId);
                        _sent[f.steamId] = ok ? Sent.Ok : Sent.Failed;
                        _sentAt[f.steamId] = Time.unscaledTime;
                    }
                    GUI.enabled = true;
                }
            }

            GUI.EndScrollView();
            DrawFooter(x, y, w, h, ref close, canRefresh: true);
            if (close) onClose?.Invoke();
        }

        // A row's result, expired back to None once its hold elapses so a player can retry.
        static Sent SentState(ulong id)
        {
            if (!_sent.TryGetValue(id, out var s)) return Sent.None;
            if (_sentAt.TryGetValue(id, out float t) && Time.unscaledTime - t > SentHold)
            {
                _sent.Remove(id); _sentAt.Remove(id);
                return Sent.None;
            }
            return s;
        }

        static void Note(Rect r, string text)
        {
            var st = new GUIStyle(GUI.skin.label)
            { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter, normal = { textColor = UITheme.Dim } };
            UITheme.Label(new Rect(r.x + 10f, r.y + 12f, r.width - 20f, r.height - 24f), text, st);
        }

        // The footer is drawn on EVERY path (see Draw) so the control count below the rows never
        // changes with the panel's state. The overlay button is likewise always allocated and
        // merely disabled, rather than appearing and disappearing with CanInvite - a control that
        // comes and goes between two passes of one frame shifts every id after it.
        static void DrawFooter(float x, float y, float w, float h, ref bool close, bool canRefresh)
        {
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            float by = y + h - 48f;

            GUI.enabled = canRefresh && Time.unscaledTime - _refreshedAt > 0.5f;
            if (UITheme.Button(new Rect(x + 22f, by, 110f, 34f), "Refresh", btn)) Refresh();

            // Steam's own picker, for players who prefer it or whose friends list will not read.
            GUI.enabled = SteamFriendsAPI.CanInvite;
            if (UITheme.Button(new Rect(x + 140f, by, 150f, 34f), "Steam overlay", btn))
                SteamFriendsAPI.OpenInviteDialog();
            GUI.enabled = true;

            if (UITheme.Button(new Rect(x + w - 132f, by, 110f, 34f), "Done", btn)) close = true;
        }
    }
}
