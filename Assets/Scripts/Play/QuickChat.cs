using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Quickchat catalog + per-player key assignments. The 25 phrases are a fixed, curated set
    /// (so they need no censoring). Each player maps their own number keys 1-6 to a phrase index,
    /// saved locally in PlayerPrefs (not networked) - the same convention as Keybinds: a prefixed
    /// key per slot, a lazily-loaded cache, and an explicit Save() after each write.
    ///
    /// The wire only ever carries a phrase INDEX for a preset (one byte), or the custom string for
    /// a Tab-typed message; assignments themselves stay local to each player.
    /// </summary>
    public static class QuickChat
    {
        // 25 curated phrases. Edit freely; order is the phrase INDEX used on the wire, so appending
        // is safe but reordering changes what an in-flight preset id means (matches the emote-enum
        // caution). Kept clean so presets bypass the censor.
        public static readonly string[] Phrases =
        {
            "Nice shot!",        // 0
            "What a save!",      // 1
            "Great pass!",       // 2
            "Thanks!",           // 3
            "Close one!",        // 4
            "No problem.",       // 5
            "Wow!",              // 6
            "Calculated.",       // 7
            "$#@%!",             // 8  (already masked - a mock-swear)
            "Whew!",             // 9
            "Defending...",      // 10
            "Go for it!",        // 11
            "Centering!",        // 12
            "Take the shot!",    // 13
            "Nice one!",         // 14
            "Well played.",      // 15
            "Sorry!",            // 16
            "My fault.",         // 17
            "OMG!",              // 18
            "Sííí!",             // 19
            "That was epic!",    // 20
            "GG",                // 21
            "Rematch?",          // 22
            "One. More. Game.",  // 23
            "Savage!",           // 24
        };

        // Default phrase index bound to keys 1..6 (index 0 = key 1).
        static readonly int[] DefaultSlots = { 0, 2, 1, 4, 3, 11 };

        const string PrefPrefix = "trickshot.quickchat.";
        const int SlotCount = 6;

        static int[] _slots;   // lazily loaded; index 0 = key 1
        static int[] Slots { get { if (_slots == null) Load(); return _slots; } }

        static void Load()
        {
            _slots = new int[SlotCount];
            for (int i = 0; i < SlotCount; i++)
                _slots[i] = Mathf.Clamp(PlayerPrefs.GetInt(PrefPrefix + i, DefaultSlots[i]), 0, Phrases.Length - 1);
        }

        // key is 1..6. Returns the phrase index assigned to that key (clamped/default-safe).
        public static int PhraseIndexForKey(int key)
        {
            int i = key - 1;
            if (i < 0 || i >= SlotCount) return 0;
            return Slots[i];
        }

        // key is 1..6. Resolves straight to the phrase text.
        public static string PhraseForKey(int key)
        {
            int idx = PhraseIndexForKey(key);
            return (idx >= 0 && idx < Phrases.Length) ? Phrases[idx] : "";
        }

        // Assign a phrase index to a key (1..6) and persist.
        public static void SetSlot(int key, int phraseIndex)
        {
            int i = key - 1;
            if (i < 0 || i >= SlotCount) return;
            phraseIndex = Mathf.Clamp(phraseIndex, 0, Phrases.Length - 1);
            Slots[i] = phraseIndex;
            PlayerPrefs.SetInt(PrefPrefix + i, phraseIndex);
            PlayerPrefs.Save();
        }

        public static void ResetDefaults()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                Slots[i] = DefaultSlots[i];
                PlayerPrefs.DeleteKey(PrefPrefix + i);
            }
            PlayerPrefs.Save();
        }

        // Safe lookup for a wire preset id.
        public static string PhraseByIndex(int idx) =>
            (idx >= 0 && idx < Phrases.Length) ? Phrases[idx] : "";
    }
}
