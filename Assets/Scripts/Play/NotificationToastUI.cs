using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Small toast queue for "you just got X" moments (an achievement unlock today; a top-10
    /// Challenge leaderboard placement once that exists) - anywhere in the game, not just the
    /// menu, since a networked match can finish an achievement mid-session. Lazily installs a
    /// single DontDestroyOnLoad instance on first Show(), the same pattern Multiplayer.
    /// InstallPump() uses for its session-lifetime pump - nothing else needs to remember to spawn
    /// this. Top-right, deliberately away from the Hub's bottom-right Friends/Achievements chips
    /// (MenuUI.cs) and from Hud's bottom control band, so a toast never sits on top of either.
    ///
    /// NOT visually verified this session (Editor bridge down): Unity's OnGUI draw order across
    /// separate active MonoBehaviours isn't something this class controls (it's Script Execution
    /// Order / instantiation order, not a per-call z-index), so whether this reliably paints
    /// ABOVE whatever screen is currently active hasn't been confirmed on-screen. If a toast ever
    /// turns out to render underneath another IMGUI screen, that's the thing to fix first.
    /// </summary>
    public class NotificationToastUI : MonoBehaviour
    {
        struct Toast { public string title, body; public float shownAt; }

        const float ShowSeconds = 4f;
        const float FadeSeconds = 0.35f;

        static NotificationToastUI _instance;
        readonly List<Toast> _toasts = new List<Toast>();

        public static void Show(string title, string body)
        {
            if (_instance == null)
            {
                var go = new GameObject("NotificationToastUI");
                Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<NotificationToastUI>();
            }
            _instance._toasts.Add(new Toast { title = title, body = body, shownAt = Time.unscaledTime });
        }

        void Update()
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
                if (Time.unscaledTime - _toasts[i].shownAt > ShowSeconds) _toasts.RemoveAt(i);
        }

        void OnGUI()
        {
            if (_toasts.Count == 0) return;
            MenuScale.Begin();

            const float w = 320f, h = 62f, gap = 10f;
            float x = MenuScale.Width - w - 24f, y = 24f;

            foreach (var t in _toasts)
            {
                float age = Time.unscaledTime - t.shownAt;
                float alpha = age < FadeSeconds ? age / FadeSeconds
                            : age > ShowSeconds - FadeSeconds ? (ShowSeconds - age) / FadeSeconds
                            : 1f;
                alpha = Mathf.Clamp01(alpha);

                var r = new Rect(x, y, w, h);
                var glow = new Color(0f, 0f, 0f, 0.35f * alpha);
                UITheme.Glow(new Rect(r.x - 20f, r.y - 14f, r.width + 40f, r.height + 28f), glow);
                UITheme.Panel(r, UITheme.Gold, true, alpha);

                var titleSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, alpha) } };
                var bodySt = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(UITheme.Ink.r, UITheme.Ink.g, UITheme.Ink.b, alpha) } };
                GUI.Label(new Rect(x + 14f, y + 8f, w - 28f, 22f), t.title, titleSt);
                GUI.Label(new Rect(x + 14f, y + 30f, w - 28f, 26f), t.body, bodySt);

                y += h + gap;
            }

            MenuScale.End();
        }
    }
}
