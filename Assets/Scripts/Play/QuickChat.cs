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
        // 25 curated phrases. Order is the phrase INDEX used on the wire, so appending is safe but
        // REORDERING changes what an in-flight preset id means (same caution as the emote enum). Kept
        // clean so presets bypass the censor.
        //
        // REWRITTEN, because the previous set was Rocket League's almost item for item - "Nice shot!",
        // "What a save!", "Close one!", "No problem.", "Calculated.", "Savage!", "One. More. Game.",
        // "Centering!", "Take the shot!". Beyond the obvious problem with shipping someone else's
        // wordlist, it was wrong for the game: a car-football wheel is built around shooting and saving
        // because that is the whole sport there, whereas most of what footballers actually shout is
        // INFORMATION - where the pressure is, whether you have time, where you want it.
        //
        // So this set leads with calls a team-mate can act on, and keeps praise and commiseration short.
        // Nothing here is a catchphrase; it is all things said on a pitch.
        //
        // Replacing the list wholesale remaps anyone's saved wheel slots (they are stored by index) and
        // invalidates a preset id already in flight. Both are acceptable pre-release and neither can
        // crash: the loader clamps to Phrases.Length - 1.
        public static readonly string[] Phrases =
        {
            // ---- calls: information a team-mate can act on ----
            "Man on!",           // 0
            "Time!",             // 1
            "Square!",           // 2
            "Through!",          // 3
            "In behind!",        // 4
            "Hit it!",           // 5
            "Switch it!",        // 6
            "Hold it up!",       // 7
            "Push up!",          // 8
            "Drop in!",          // 9
            "Away!",             // 10
            "Keeper's!",         // 11
            // ---- praise ----
            "Get in!",           // 12
            "Top corner.",       // 13
            "Big hands!",        // 14
            "Class.",            // 15
            "Well in.",          // 16
            // ---- reaction ----
            "Woodwork.",         // 17
            "So close.",         // 18
            "Unlucky.",          // 19
            // ---- owning it ----
            "My ball.",          // 20
            "My fault.",         // 21
            "On me.",            // 22
            // ---- after the whistle ----
            "Good game.",        // 23
            "Again?",            // 24
        };

        // Default phrase index bound to keys 1..6 (index 0 = key 1).
        // The six on the wheel out of the box: four calls, one for a goal, one for a miss. Weighted
        // toward things said DURING play, because that is when a wheel is reachable.
        //   Man on! / Square! / Through! / Hit it! / Get in! / Unlucky.
        static readonly int[] DefaultSlots = { 0, 2, 3, 5, 12, 19 };

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
