using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player-side gameplay preferences, persisted in PlayerPrefs the way DisplaySettings is.
    /// Replays: the post-goal slow-mo in the drill and set-piece modes (a match never rolls one).
    /// In multiplayer the HOST's setting decides whether a replay rolls at all; a client with it
    /// off casts its skip vote the moment one starts.
    /// </summary>
    public static class GameplaySettings
    {
        const string KeyReplays = "gameplay.replays";
        static bool _loaded, _replays;

        public static bool Replays
        {
            get { Load(); return _replays; }
            set
            {
                Load();
                if (_replays == value) return;
                _replays = value;
                PlayerPrefs.SetInt(KeyReplays, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _replays = PlayerPrefs.GetInt(KeyReplays, 1) != 0;
        }
    }
}
