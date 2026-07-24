using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Rocket-League-style quickchat feed for multiplayer. Lives for the match (created by each
    /// Net*Match driver, torn down with it). Owns:
    ///   - the rolling feed in the top-left: "Name: phrase" lines, newest at the bottom, each shown
    ///     ~FeedHold seconds then fading over the last ~FeedFade seconds;
    ///   - the Tab custom-text entry box (censored on submit, then sent);
    ///   - a client-side self-limit that mirrors the host anti-spam so the local player gets
    ///     instant "you're doing that too much" feedback (the host is still authoritative).
    ///
    /// Input is read by the driver (which owns the GameInput), which calls Open()/Digit() here.
    /// While Typing is true the driver suspends gameplay control so keystrokes don't drive play.
    /// </summary>
    public class QuickChatFeed : MonoBehaviour
    {
        const int   MaxLines = 5;
        const float FeedHold = 5f;    // seconds fully visible
        const float FeedFade = 1f;    // seconds fading at the end
        const int   MaxCustomLen = 100;

        // Client self-limit (matches NetSession's authoritative window/burst).
        const int   QcBurst = 4;
        const float QcWindow = 3.5f;
        const float QcMute = 3f;

        struct Line { public string text; public float born; }
        readonly List<Line> _lines = new List<Line>();

        NetSession _s;

        // ---- text-entry state ----
        public bool Typing { get; private set; }
        // True whenever ANY quickchat text field is open.
        public static bool AnyOpen { get; private set; }
        // Frame the field last closed. The field consumes Escape via an IMGUI event (in Draw),
        // but PauseMenu polls the raw Escape key in Update; the two pipelines can be a frame apart,
        // so the field would close on one frame and pause open on the next. PauseMenu checks
        // EscapeOwned, which stays true through the frame AFTER a close to swallow that trailing
        // key read. -100 = never closed yet.
        static int s_closedFrame = -100;
        public static bool EscapeOwned => AnyOpen || (Time.frameCount - s_closedFrame) <= 1;
        string _draft = "";
        // ---- local spam guard ----
        readonly Queue<float> _sendTimes = new Queue<float>();
        float _mutedUntil;
        string _notice; float _noticeUntil;

        public void Bind(NetSession s)
        {
            _s = s;
            if (_s != null) _s.QuickChatReceived += OnReceived;
        }

        void OnDestroy()
        {
            if (_s != null) _s.QuickChatReceived -= OnReceived;
            if (Typing) AnyOpen = false;   // don't leave the flag stuck if torn down mid-type
        }

        // ---------- receiving ----------
        void OnReceived(int slot, int presetId, string custom)
        {
            string phrase = presetId == 255 ? custom : QuickChat.PhraseByIndex(presetId);
            if (string.IsNullOrEmpty(phrase)) return;
            string name = NameForSlot(slot);
            AddLine(string.IsNullOrEmpty(name) ? phrase : name + ": " + phrase);
        }

        string NameForSlot(int slot)
        {
            if (_s == null) return "";
            var rs = _s.RosterSlot(slot);
            return string.IsNullOrEmpty(rs.name) ? ("Player " + slot) : rs.name;
        }

        void AddLine(string text)
        {
            _lines.Add(new Line { text = text, born = Time.unscaledTime });
            while (_lines.Count > MaxLines) _lines.RemoveAt(0);
        }

        // ---------- sending (called by the driver on key input) ----------
        // A number key 1-6 was pressed: send its assigned preset.
        public void SendPreset(int key)
        {
            if (Typing || _s == null) return;
            if (!LocalAllow()) return;
            _s.SendQuickChat((byte)QuickChat.PhraseIndexForKey(key), null);
        }

        // Tab pressed: open (or, if already typing, submit) the custom box.
        public void ToggleTextEntry()
        {
            if (Typing) { SubmitDraft(); return; }
            Typing = true;
            AnyOpen = true;
            _draft = "";
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void CloseTextEntry()
        {
            Typing = false;
            AnyOpen = false;
            s_closedFrame = Time.frameCount;   // keep EscapeOwned true one more frame (see field)
            _draft = "";
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void SubmitDraft()
        {
            string msg = (_draft ?? "").Trim();
            if (msg.Length > 0 && _s != null && LocalAllow())
            {
                if (msg.Length > MaxCustomLen) msg = msg.Substring(0, MaxCustomLen);
                _s.SendQuickChat(255, ChatCensor.Clean(msg));   // censor at send (host re-censors too)
            }
            CloseTextEntry();
        }

        // Local self-limit; also drives the "Chat disabled (spamming)" notice for instant feedback.
        bool LocalAllow()
        {
            float now = Time.unscaledTime;
            if (now < _mutedUntil) return false;
            while (_sendTimes.Count > 0 && now - _sendTimes.Peek() > QcWindow) _sendTimes.Dequeue();
            if (_sendTimes.Count >= QcBurst)
            {
                _mutedUntil = now + QcMute;
                _sendTimes.Clear();
                _notice = "Chat disabled (spamming)";
                _noticeUntil = now + QcMute;
                return false;
            }
            _sendTimes.Enqueue(now);
            return true;
        }

        // ---------- drawing ----------
        // Called from the driver's OnGUI.
        public void Draw()
        {
            float now = Time.unscaledTime;

            // Rolling feed, top-left, newest at the bottom.
            var style = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            float x = 14f, y0 = 90f, rowH = 22f;
            // Prune fully-expired lines lazily as we draw.
            for (int i = _lines.Count - 1; i >= 0; i--)
                if (now - _lines[i].born > FeedHold + FeedFade) _lines.RemoveAt(i);

            for (int i = 0; i < _lines.Count; i++)
            {
                float age = now - _lines[i].born;
                float a = age <= FeedHold ? 1f : Mathf.Clamp01(1f - (age - FeedHold) / FeedFade);
                var c = Color.white; c.a = a;
                style.normal.textColor = c;
                // subtle shadow for legibility over the pitch
                var sh = new GUIStyle(style); sh.normal.textColor = new Color(0f, 0f, 0f, a * 0.6f);
                float ry = y0 + i * rowH;
                GUI.Label(new Rect(x + 1f, ry + 1f, 560f, rowH), _lines[i].text, sh);
                GUI.Label(new Rect(x, ry, 560f, rowH), _lines[i].text, style);
            }

            // Spam notice (brief).
            if (_notice != null && now < _noticeUntil)
            {
                var ns = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.5f, 0.45f) } };
                GUI.Label(new Rect(x, y0 - rowH, 560f, rowH), _notice, ns);
            }

            // Custom text-entry box (bottom-left, above the control legend).
            if (Typing)
            {
                float bw = 520f, bh = 30f, by = Screen.height - 90f;
                var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.DrawTexture(new Rect(x, by, bw, bh), Texture2D.whiteTexture);
                GUI.color = prev;

                // Handle Enter/Esc BEFORE the TextField draws: GUI.TextField consumes the Return
                // and Escape KeyDown internally, so a check after it never sees them (that was why
                // Enter didn't send). Capture the intent now, act after drawing so we don't mutate
                // state mid-control. Consume the event so it doesn't fall through (e.g. to pause).
                bool submit = false, cancel = false;
                var e = Event.current;
                if (e.type == EventType.KeyDown)
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { submit = true; e.Use(); }
                    else if (e.keyCode == KeyCode.Escape) { cancel = true; e.Use(); }
                }

                GUI.SetNextControlName("QCInput");
                var ts = new GUIStyle(GUI.skin.textField) { fontSize = 15 };
                _draft = GUI.TextField(new Rect(x + 6f, by + 3f, bw - 12f, bh - 6f), _draft ?? "", MaxCustomLen, ts);
                GUI.FocusControl("QCInput");

                if (submit) SubmitDraft();
                else if (cancel) CloseTextEntry();
            }
        }
    }
}
