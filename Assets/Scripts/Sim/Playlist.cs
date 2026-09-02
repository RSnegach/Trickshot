using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The game's MUSIC catalog: one row per song, storing the song itself (its clip, loaded
    /// lazily from Resources/Audio by file name), its display title and its author.
    /// AudioManager plays whichever row the cursor is on, and the now-playing pill at the top
    /// of the screen (AudioManager.OnGUI) reads Title + Author straight off the current row.
    ///
    /// One track today; more will follow, so this object is the whole interface. APPEND new
    /// rows at the END and never insert or reorder: the cursor is an INDEX, so a reorder
    /// silently repoints what the running player is on (same rule as every other catalog in
    /// this codebase).
    /// </summary>
    public static class Playlist
    {
        public class Track
        {
            public string File;    // Resources/Audio file name, no extension
            public string Title;   // banner text, left of the dash
            public string Author;  // banner text, right of the dash
            AudioClip _clip;        // loaded once on first play

            /// <summary>The song itself. Null (the player skips it) if the file is missing
            /// from the build.</summary>
            public AudioClip Song => _clip != null ? _clip : (_clip = Resources.Load<AudioClip>("Audio/" + File));
        }

        // ---- the playlist ----
        static readonly Track[] _tracks =
        {
            new Track { File = "Trickshot!", Title = "Trickshot!", Author = "RSneggy" },
        };

        static int _index;

        public static int Count => _tracks.Length;
        public static Track Current => _tracks[Mathf.Clamp(_index, 0, _tracks.Length - 1)];

        /// <summary>Advance to the next song (wraps) and return it. AudioManager calls this
        /// when a track finishes; a one-track playlist restarts it.</summary>
        public static Track Next()
        {
            _index = (_index + 1) % _tracks.Length;
            return Current;
        }
    }
}