using UnityEngine;

namespace Trickshot
{
    // Core roles plus the single-player modes and the full scrimmage match.
    // SetPieces = networked free-kick shootout (also playable solo via the FreeKick build).
    // NOTE: append new values at the END - MatchConfig sends GameMode as a byte over the wire.
    public enum GameMode { Striker, Goalkeeper, Freeplay, TimeTrial, Accuracy, FreeKick, Scrimmage, SetPieces }

    /// <summary>
    /// IMGUI start menu. Top level: Striker, Goalkeeper, Mode. "Mode" opens a submenu of
    /// the extra modes (Freeplay, Time Trial, Accuracy, Free Kick, Scrimmage). Invokes a
    /// callback with the chosen mode. Kept as IMGUI so it needs no Canvas/EventSystem
    /// wiring (consistent with the rest of the runtime build).
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        System.Action<GameMode> _onChoose;
        System.Action _onMultiplayer;
        bool _chosen;
        bool _inChallenges;

        // Options overlay (Keybindings + Audio), same panel the pause menu uses. Built lazily from
        // the passed GameInput; null if none was supplied (then no Options button is shown).
        OptionsMenu _options;
        bool _optionsOpen;

        // Vignette textures for legibility over the animated backdrop: a faint full-screen tint
        // and a soft dark disc drawn behind the title + button column. Built lazily, freed on destroy.
        Texture2D _tintTex;
        Texture2D _vignetteTex;

        public void Init(System.Action<GameMode> onChoose, System.Action onMultiplayer = null,
                         GameInput input = null)
        {
            _onChoose = onChoose;
            _onMultiplayer = onMultiplayer;
            if (input != null) _options = new OptionsMenu(input);
            // Menu needs a visible, free cursor.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnGUI()
        {
            if (_chosen) return;

            float w = 320f, h = 66f, gap = 20f;
            float cx = Screen.width * 0.5f - w * 0.5f;

            // Darken behind the menu so the white title and buttons read over the moving scene,
            // while the pitch stays visible at the screen edges.
            DrawVignette();

            // Options overlay takes over the whole menu while open (same panel as the pause menu).
            if (_optionsOpen && _options != null)
            {
                _options.Draw(() => _optionsOpen = false);
                return;
            }

            var title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 54, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };

            if (!_inChallenges)
            {
                // Row count grows by one when Options is available, so keep the column centered.
                bool hasOptions = _options != null;
                float rows = hasOptions ? 5f : 4f;
                float cy = Screen.height * 0.5f - (h * rows + gap * (rows - 1f)) * 0.5f;
                GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "TRICKSHOT", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Striker", btn)) Choose(GameMode.Striker);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Goalkeeper", btn)) Choose(GameMode.Goalkeeper);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Mode", btn)) _inChallenges = true;
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Multiplayer", btn))
                {
                    _chosen = true; enabled = false; _onMultiplayer?.Invoke();
                }
                if (hasOptions && GUI.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Options", btn))
                    _optionsOpen = true;
            }
            else
            {
                float cy = Screen.height * 0.5f - (h * 3f + gap * 2.5f);
                GUI.Label(new Rect(0, cy - 110f, Screen.width, 80f), "MODE", title);
                if (GUI.Button(new Rect(cx, cy, w, h), "Scrimmage", btn)) Choose(GameMode.Scrimmage);
                if (GUI.Button(new Rect(cx, cy + (h + gap), w, h), "Freeplay", btn)) Choose(GameMode.Freeplay);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 2f, w, h), "Time Trial", btn)) Choose(GameMode.TimeTrial);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 3f, w, h), "Accuracy", btn)) Choose(GameMode.Accuracy);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 4f, w, h), "Free Kick / Penalty", btn)) Choose(GameMode.FreeKick);
                if (GUI.Button(new Rect(cx, cy + (h + gap) * 5f, w, h), "Back", btn)) _inChallenges = false;
            }
        }

        void Choose(GameMode m)
        {
            _chosen = true;
            enabled = false;
            _onChoose?.Invoke(m);   // may destroy this object; do nothing after
        }

        // A faint even tint over the whole screen plus a soft radial dark patch centered on the
        // menu column. The radial patch is a small texture stretched by GUI, so its bilinear
        // filtering does the smoothing for free.
        void DrawVignette()
        {
            if (_tintTex == null)
            {
                _tintTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _tintTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.28f));
                _tintTex.Apply();
                _tintTex.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_vignetteTex == null)
            {
                const int N = 64;
                _vignetteTex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color[N * N];
                float c = (N - 1) * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0 center -> 1 edge
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a;                       // tighter falloff, transparent by the rim
                        px[y * N + x] = new Color(0f, 0f, 0f, a * 0.62f);
                    }
                _vignetteTex.SetPixels(px);
                _vignetteTex.Apply();
                _vignetteTex.wrapMode = TextureWrapMode.Clamp;
                _vignetteTex.hideFlags = HideFlags.HideAndDontSave;
            }

            // Even tint everywhere.
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _tintTex);
            // Dark disc behind the column: tall enough for title + up to six buttons, centered.
            float vw = 720f, vh = Screen.height * 1.15f;
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - vw * 0.5f, Screen.height * 0.5f - vh * 0.5f, vw, vh), _vignetteTex);
        }

        void OnDestroy()
        {
            if (_tintTex != null) Destroy(_tintTex);
            if (_vignetteTex != null) Destroy(_vignetteTex);
            _options?.Dispose();   // abort any in-flight rebind so the op isn't orphaned
        }
    }
}
