using System.Collections.Generic;
using System.Text;

namespace Trickshot
{
    /// <summary>
    /// Aggressive, obfuscation-aware profanity filter for custom (Tab-typed) quickchat. Matched
    /// spans are replaced with asterisks; clean text (and every curated preset) passes untouched.
    ///
    /// Two passes, tuned to be aggressive without wrecking clean words (the "Scunthorpe problem"):
    ///   Pass A - de-obfuscate: lowercase, map leetspeak (@4->a, 3->e, 1!->i, 0->o, $5->s, 7->t...),
    ///            DROP separators (space/punctuation) and COLLAPSE repeated letters, then match the
    ///            SEVERE roots anywhere in that stream. Beats "f u c k", "sh1t", "fuuuck", "f.u.c.k".
    ///            Masks the full ORIGINAL span of the match (including the separators it spanned).
    ///   Pass B - token pass: split on non-letters, normalize each token, and mask MILDER words only
    ///            when a whole token equals them (+ simple plurals). Keeps "class", "assess",
    ///            "shell" etc. clean while still catching the word used on its own.
    ///
    /// Best-effort, not perfect. Both lists are plain editable arrays.
    /// </summary>
    public static class ChatCensor
    {
        // Severe: masked wherever they surface in the de-obfuscated stream. Roots (no need to list
        // every inflection). Extend as needed.
        static readonly string[] SevereRoots =
        {
            "fuck", "shit", "bitch", "cunt", "dick", "pussy", "asshole", "bastard",
            "whore", "slut", "fag", "nigger", "nigga", "retard", "kike", "spic", "chink",
            "motherfuck", "jackass", "dumbass",
        };

        // Milder / ambiguous: only masked as a standalone token (+ -s/-es plural), so they don't
        // trip inside clean words ("class", "hello", "grass", "assassin").
        static readonly string[] WordRoots =
        {
            "ass", "hell", "damn", "crap", "piss", "cock", "twat", "wank", "prick", "arse",
        };

        static readonly Dictionary<char, char> Leet = new Dictionary<char, char>
        {
            ['@'] = 'a', ['4'] = 'a',
            ['3'] = 'e',
            ['1'] = 'i', ['!'] = 'i', ['|'] = 'i',
            ['0'] = 'o',
            ['$'] = 's', ['5'] = 's',
            ['7'] = 't',
            ['2'] = 'z', ['8'] = 'b', ['6'] = 'g',
        };

        static char Norm(char c)
        {
            c = char.ToLowerInvariant(c);
            if (c >= 'a' && c <= 'z') return c;
            return Leet.TryGetValue(c, out var m) ? m : '\0';   // '\0' = separator / non-content
        }

        public static string Clean(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var mask = new bool[input.Length];

            // ---- Pass A: de-obfuscated stream + severe substring match ----
            var sb = new StringBuilder(input.Length);
            var startIdx = new List<int>(input.Length);   // original index where each stream char began
            var endIdx = new List<int>(input.Length);     // original index where its collapsed run ends
            char last = '\0';
            for (int i = 0; i < input.Length; i++)
            {
                char nc = Norm(input[i]);
                if (nc == '\0') continue;                  // skip separators (join across them)
                if (nc == last)                            // collapse a repeated letter into the run
                {
                    endIdx[endIdx.Count - 1] = i;
                    continue;
                }
                sb.Append(nc);
                startIdx.Add(i);
                endIdx.Add(i);
                last = nc;
            }
            string stream = sb.ToString();
            foreach (var root in SevereRoots)
            {
                int from = 0, hit;
                while ((hit = stream.IndexOf(root, from, System.StringComparison.Ordinal)) >= 0)
                {
                    int a = startIdx[hit], b = endIdx[hit + root.Length - 1];
                    for (int k = a; k <= b; k++) mask[k] = true;
                    from = hit + root.Length;
                }
            }

            // ---- Pass B: whole-token match for milder words ----
            int t = 0;
            while (t < input.Length)
            {
                if (Norm(input[t]) == '\0') { t++; continue; }
                int tStart = t;
                var tb = new StringBuilder();
                char tlast = '\0';
                while (t < input.Length && Norm(input[t]) != '\0')
                {
                    char nc = Norm(input[t]);
                    if (nc != tlast) { tb.Append(nc); tlast = nc; }
                    t++;
                }
                int tEnd = t - 1;
                string tok = tb.ToString();
                if (IsMildBad(tok))
                    for (int k = tStart; k <= tEnd; k++) mask[k] = true;
            }

            // ---- Build output: mask flagged chars, keep whitespace readable ----
            var outSb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
                outSb.Append(mask[i] && !char.IsWhiteSpace(input[i]) ? '*' : input[i]);
            return outSb.ToString();
        }

        static bool IsMildBad(string tok)
        {
            foreach (var r in WordRoots)
                if (tok == r || tok == r + "s" || tok == r + "es") return true;
            return false;
        }
    }
}
