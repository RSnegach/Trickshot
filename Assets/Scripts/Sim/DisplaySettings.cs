using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Display and camera options, persisted in PlayerPrefs and applied at boot.
    ///
    /// The game builds its whole UI in IMGUI at runtime, so nothing about the window is authored
    /// in a scene: resolution, window mode, vsync, UI scale and camera FOV all live here and are
    /// edited from the Camera tab of the options menu (pause menu -> Options -> Camera).
    ///
    /// ApplyOnBoot() runs once from GameBootstrap before anything draws. It only calls
    /// Screen.SetResolution when the player actually chose a resolution, so a fresh install keeps
    /// whatever the platform picked (native fullscreen on desktop).
    /// </summary>
    public static class DisplaySettings
    {
        const string KeyW = "disp.w", KeyH = "disp.h", KeyMode = "disp.mode";
        const string KeyVsync = "disp.vsync", KeyFov = "cam.fov", KeyUi = "ui.scale";

        /// <summary>Extra degrees of camera FOV on top of each camera mode's tuned value.</summary>
        public const float MinFov = -8f, MaxFov = 14f;

        static bool _loaded;
        static float _fov;
        static bool _vsync = true;
        static Resolution[] _list;

        // ---------------------------------------------------------------- load / save

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _fov = Mathf.Clamp(PlayerPrefs.GetFloat(KeyFov, 0f), MinFov, MaxFov);
            _vsync = PlayerPrefs.GetInt(KeyVsync, 1) != 0;
            MenuScale.UserScale = PlayerPrefs.GetFloat(KeyUi, 1f);
        }

        /// <summary>Push the saved settings onto the engine. Called once from GameBootstrap.</summary>
        public static void ApplyOnBoot()
        {
            Load();
            QualitySettings.vSyncCount = _vsync ? 1 : 0;

            int w = PlayerPrefs.GetInt(KeyW, 0), h = PlayerPrefs.GetInt(KeyH, 0);
            var mode = (FullScreenMode)PlayerPrefs.GetInt(KeyMode, (int)Screen.fullScreenMode);
            if (w >= 640 && h >= 480)
            {
                // Only touch the window if the player picked something; otherwise leave the
                // platform default alone (native res, correct display).
                if (w != Screen.width || h != Screen.height || mode != Screen.fullScreenMode)
                    Screen.SetResolution(w, h, mode);
            }
            else if (mode != Screen.fullScreenMode)
            {
                Screen.fullScreenMode = mode;
            }
        }

        // ---------------------------------------------------------------- camera FOV

        /// <summary>FOV offset in degrees, added to every camera mode's base FOV.</summary>
        public static float FovOffset
        {
            get { Load(); return _fov; }
            set
            {
                Load();
                _fov = Mathf.Clamp(value, MinFov, MaxFov);
                PlayerPrefs.SetFloat(KeyFov, _fov);
            }
        }

        // ---------------------------------------------------------------- vsync

        public static bool VSync
        {
            get { Load(); return _vsync; }
            set
            {
                Load();
                _vsync = value;
                QualitySettings.vSyncCount = value ? 1 : 0;
                PlayerPrefs.SetInt(KeyVsync, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // ---------------------------------------------------------------- UI scale

        /// <summary>Multiplier on the automatic UI fit (see MenuScale). Persisted.</summary>
        public static float UiScale
        {
            get { Load(); return MenuScale.UserScale; }
            set
            {
                Load();
                MenuScale.UserScale = value;
                PlayerPrefs.SetFloat(KeyUi, MenuScale.UserScale);
            }
        }

        /// <summary>Commit pending pref writes. Called when a slider drag ends.</summary>
        public static void Flush() => PlayerPrefs.Save();

        // ---------------------------------------------------------------- resolution

        /// <summary>Window modes offered in the options menu, in menu order.</summary>
        public static readonly FullScreenMode[] Modes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };

        public static string ModeLabel(FullScreenMode m)
        {
            switch (m)
            {
                case FullScreenMode.ExclusiveFullScreen: return "Fullscreen";
                case FullScreenMode.FullScreenWindow:    return "Borderless";
                case FullScreenMode.MaximizedWindow:     return "Maximized";
                default:                                 return "Windowed";
            }
        }

        /// <summary>
        /// Resolutions the display supports, deduped by size (Screen.resolutions repeats each
        /// size once per refresh rate) and sorted small to large. Built once and cached.
        /// </summary>
        public static Resolution[] Available
        {
            get
            {
                if (_list != null) return _list;
                var seen = new HashSet<long>();
                var outp = new List<Resolution>();
                foreach (var r in Screen.resolutions)
                {
                    if (r.width < 640 || r.height < 480) continue;
                    if (!seen.Add((long)r.width * 100000L + r.height)) continue;
                    outp.Add(r);
                }
                // Some platforms (and windowed editor runs) report nothing useful. Offer a
                // sensible ladder plus the current window so the list is never empty.
                if (outp.Count == 0)
                {
                    int[,] fallback = { { 1280, 720 }, { 1600, 900 }, { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 } };
                    for (int i = 0; i < fallback.GetLength(0); i++)
                    {
                        var r = new Resolution { width = fallback[i, 0], height = fallback[i, 1] };
                        if (seen.Add((long)r.width * 100000L + r.height)) outp.Add(r);
                    }
                }
                if (seen.Add((long)Screen.width * 100000L + Screen.height))
                    outp.Add(new Resolution { width = Screen.width, height = Screen.height });

                outp.Sort((a, b) => a.width != b.width ? a.width.CompareTo(b.width)
                                                       : a.height.CompareTo(b.height));
                _list = outp.ToArray();
                return _list;
            }
        }

        /// <summary>Switch the window and remember it.</summary>
        public static void Apply(int w, int h, FullScreenMode mode)
        {
            Load();
            PlayerPrefs.SetInt(KeyW, w);
            PlayerPrefs.SetInt(KeyH, h);
            PlayerPrefs.SetInt(KeyMode, (int)mode);
            PlayerPrefs.Save();
            Screen.SetResolution(w, h, mode);
        }

        /// <summary>Change window mode only, keeping the current size.</summary>
        public static void ApplyMode(FullScreenMode mode) => Apply(Screen.width, Screen.height, mode);
    }
}
