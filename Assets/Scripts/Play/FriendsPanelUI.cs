using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Small flyout for the Hub's "Friends" chip (MenuUI.cs). Shows the player's Steam friends
    /// (name, online, playing Trickshot) via SteamFriendsAPI - empty and honestly labelled
    /// "Steam not connected" until Steamworks is actually wired (SteamFriendsAPI.Available).
    /// Static, not a MonoBehaviour: MenuUI already drives its own OnGUI/Update, this just needs a
    /// small cache of the last-fetched list.
    /// </summary>
    public static class FriendsPanelUI
    {
        static List<SteamFriendInfo> _friends;

        /// <summary>
        /// Call once, on the frame the panel opens (MenuUI only calls this from inside its own
        /// click handler, never every frame, so there's no need to gate against re-fetching - and
        /// gating on a "did we ever fetch" latch would show stale data forever on the SECOND open
        /// across a Hub screen re-entry, since MenuUI itself is destroyed/rebuilt but this static
        /// cache would survive).
        /// </summary>
        public static void OnOpened()
        {
            _friends = null;   // shows "Loading..." rather than a stale list while the fetch is in flight
            SteamFriendsAPI.RequestFriendsList(list => _friends = list);
        }

        public static void Draw(Rect r, System.Action onClose)
        {
            UITheme.Panel(r, UITheme.Blue);
            UITheme.Section(new Rect(r.x + 16f, r.y + 10f, r.width - 32f, 18f), "FRIENDS");

            var closeBtn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
            if (UITheme.Button(new Rect(r.x + r.width - 30f, r.y + 6f, 22f, 22f), "x", closeBtn)) onClose?.Invoke();

            float y = r.y + 34f, lx = r.x + 16f, lw = r.width - 32f;

            if (!SteamFriendsAPI.Available)
            {
                var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = UITheme.Dim } };
                UITheme.Label(new Rect(lx, y, lw, r.height - 44f), "Steam not connected. Friends will appear here once Steam is linked.", hint);
                return;
            }

            if (_friends == null)
            {
                UITheme.Label(new Rect(lx, y, lw, 20f), "Loading...", new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UITheme.Dim } });
                return;
            }

            if (_friends.Count == 0)
            {
                UITheme.Label(new Rect(lx, y, lw, 20f), "No friends online.", new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UITheme.Dim } });
                return;
            }

            var name = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            var tag = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight };
            const float rowH = 26f;
            float maxRows = Mathf.Floor((r.height - 44f) / rowH);
            for (int i = 0; i < _friends.Count && i < maxRows; i++)
            {
                var f = _friends[i];
                var row = new Rect(lx, y, lw, rowH);
                UITheme.Label(row, f.name, name);
                string status = f.playingTrickshot ? "IN TRICKSHOT" : f.online ? "ONLINE" : "OFFLINE";
                tag.normal.textColor = f.playingTrickshot ? UITheme.Green : f.online ? UITheme.Gold : UITheme.Faint;
                UITheme.Label(row, status, tag);
                y += rowH;
            }
        }
    }
}
