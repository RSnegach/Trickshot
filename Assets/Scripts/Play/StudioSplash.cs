using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The first thing on screen: black, the studio mark fading in, a hold, then the black lifting
    /// off the finished main menu. Cosmetic on its face, structural underneath: the menu scene is
    /// BUILT while the screen is black - the backdrop stadium and crowd, the sky's environment
    /// lighting update, the first music load - so the hitches those cost land where nothing is
    /// moving, instead of as the menu's first stutter. `build` runs once, mid-hold; the black then
    /// fades out over whatever it built.
    ///
    /// The mark is a placeholder wordmark ("RS") until the studio logo replaces it: swap the label
    /// in OnGUI for a GUI.DrawTexture of the logo and nothing else needs to change.
    /// </summary>
    public class StudioSplash : MonoBehaviour
    {
        const float FadeIn = 1.0f, Hold = 0.8f, FadeOut = 0.8f;
        const float BuildAt = FadeIn + Hold * 0.35f;   // build under full black, with time to spare

        System.Action _build;
        float _t;
        bool _built;
        GUIStyle _mark;

        public void Init(System.Action build) { _build = build; }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (!_built && _t >= BuildAt)
            {
                _built = true;
                var b = _build; _build = null;
                b?.Invoke();
                // Whatever the build cost, the clock must not jump past the hold in one step: the
                // reveal starts from the hold, not from wherever the hitch left the timer.
                _t = Mathf.Min(_t, FadeIn + Hold * 0.5f);
            }
            if (_t >= FadeIn + Hold + FadeOut) Destroy(gameObject);
        }

        void OnGUI()
        {
            GUI.depth = -1000;   // over everything the menu draws

            float black, mark;
            if (_t < FadeIn) { black = 1f; mark = Mathf.SmoothStep(0f, 1f, _t / FadeIn); }
            else if (_t < FadeIn + Hold) { black = 1f; mark = 1f; }
            else
            {
                float k = Mathf.Clamp01((_t - FadeIn - Hold) / FadeOut);
                black = 1f - k;
                mark = 1f - k;
            }

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, black);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            if (_mark == null)
                _mark = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _mark.fontSize = Mathf.Max(24, Mathf.RoundToInt(Screen.height * 0.22f));
            GUI.color = new Color(1f, 1f, 1f, mark);
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "RS", _mark);
            GUI.color = prev;
        }
    }
}
