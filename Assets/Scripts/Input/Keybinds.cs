using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The player's key/mouse bindings: a map of action name -> Input System control path
    /// (e.g. "&lt;Keyboard&gt;/w", "&lt;Mouse&gt;/leftButton"). Defaults are defined here;
    /// custom binds are saved to PlayerPrefs so they persist across launches. GameInput
    /// builds its actions from this and applies overrides when a bind changes.
    ///
    /// Camera (mouse look) and the scroll wheel air-pitch are intentionally NOT rebindable
    /// (they are the mouse itself), so they are not listed here.
    /// </summary>
    public static class Keybinds
    {
        // Display order + human labels for the rebinding UI.
        public static readonly (string action, string label)[] Actions =
        {
            ("MoveUp",     "Move forward"),
            ("MoveDown",   "Move back"),
            ("MoveLeft",   "Move left"),
            ("MoveRight",  "Move right"),
            ("Sprint",     "Sprint"),
            ("Jump",       "Jump"),
            ("LegL",       "Left leg / save"),
            ("LegR",       "Right leg / save"),
            ("PassGround", "Pass (ground)"),
            ("PassLofted", "Pass (lofted)"),
            ("Switch",     "Switch player"),
            ("Tackle",     "Tackle"),
            ("Emote",      "Emote wheel"),
            ("BallCam",    "Ball cam toggle"),
            ("Reset",      "Reset / restart"),
        };

        static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "MoveUp",     "<Keyboard>/w" },
            { "MoveDown",   "<Keyboard>/s" },
            { "MoveLeft",   "<Keyboard>/a" },
            { "MoveRight",  "<Keyboard>/d" },
            { "Sprint",     "<Keyboard>/leftShift" },
            { "Jump",       "<Keyboard>/space" },
            { "LegL",       "<Mouse>/leftButton" },
            { "LegR",       "<Mouse>/rightButton" },
            { "PassGround", "<Keyboard>/q" },
            { "PassLofted", "<Keyboard>/e" },
            { "Switch",     "<Keyboard>/f" },
            { "Tackle",     "<Keyboard>/c" },
            { "Emote",      "<Keyboard>/b" },
            { "BallCam",    "<Keyboard>/v" },
            { "Reset",      "<Keyboard>/r" },
        };

        const string PrefPrefix = "trickshot.bind.";
        static Dictionary<string, string> _current;

        static Dictionary<string, string> Current
        {
            get { if (_current == null) Load(); return _current; }
        }

        public static string Path(string action)
            => Current.TryGetValue(action, out var p) ? p : (Defaults.TryGetValue(action, out var d) ? d : "");

        public static void Set(string action, string path)
        {
            Current[action] = path;
            PlayerPrefs.SetString(PrefPrefix + action, path);
            PlayerPrefs.Save();
        }

        public static void ResetDefaults()
        {
            foreach (var kv in Defaults)
            {
                Current[kv.Key] = kv.Value;
                PlayerPrefs.DeleteKey(PrefPrefix + kv.Key);
            }
            PlayerPrefs.Save();
        }

        static void Load()
        {
            _current = new Dictionary<string, string>();
            foreach (var kv in Defaults)
                _current[kv.Key] = PlayerPrefs.GetString(PrefPrefix + kv.Key, kv.Value);
        }

        // How many OTHER actions share this action's binding (for conflict highlighting).
        public static bool IsDuplicate(string action)
        {
            string path = Path(action);
            int n = 0;
            foreach (var a in Actions) if (Path(a.action) == path) n++;
            return n > 1;
        }

        // Pretty name for a control path, e.g. "<Keyboard>/w" -> "W", "<Mouse>/leftButton" -> "LMB".
        public static string Display(string path)
        {
            if (string.IsNullOrEmpty(path)) return "-";
            switch (path)
            {
                case "<Mouse>/leftButton":   return "LMB";
                case "<Mouse>/rightButton":  return "RMB";
                case "<Mouse>/middleButton": return "MMB";
                case "<Mouse>/forwardButton":return "Mouse4";
                case "<Mouse>/backButton":   return "Mouse5";
            }
            int slash = path.LastIndexOf('/');
            string key = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (key)
            {
                case "space":      return "Space";
                case "leftShift":  return "L-Shift";
                case "rightShift": return "R-Shift";
                case "leftCtrl":   return "L-Ctrl";
                case "leftAlt":    return "L-Alt";
                case "enter":      return "Enter";
                case "tab":        return "Tab";
                case "upArrow":    return "Up";
                case "downArrow":  return "Down";
                case "leftArrow":  return "Left";
                case "rightArrow": return "Right";
            }
            return key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }
    }
}
