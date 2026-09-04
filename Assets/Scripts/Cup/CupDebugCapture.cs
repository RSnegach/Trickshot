#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// EDITOR-ONLY verification helper for the cup's choreography: schedules screenshots that
    /// fire on a condition (a round phase, a coin-toss stage, anything) plus a delay, and keeps a
    /// timeline of every director / driver phase change with sampled probes, so a beat can be
    /// captured MID-MOTION from a single play-through. Driven from Unity MCP `execute_code`
    /// (each of those round trips costs seconds of game time, far too coarse for a 1.4 s coin
    /// flight). Never referenced by gameplay code; safe to leave in the tree.
    ///
    /// Typical use from execute_code:
    ///   var c = CupDebugCapture.Instance;
    ///   c.OnRoundPhase(RoundPhase.WhistleRaise, 0.3f, dir + "09-whistle-raise.png");
    ///   c.When(() => toss stage == Flight, 0.5f, dir + "06-cointoss-flip.png");
    ///   c.Probe("coin", 0.1f, () => coinGo.transform.position.ToString());
    ///   ... later: c.Dump() returns the timeline.
    /// </summary>
    public sealed class CupDebugCapture : MonoBehaviour
    {
        sealed class Job
        {
            public string Path;
            public Func<bool> Trigger;
            public float Delay;
            public float FireAt = -1f;
            public bool Armed;      // trigger seen; waiting for the delay
            public bool Done;
            public int Repeat;      // extra captures after the first, Interval apart
            public float Interval;
            public int Shot;
        }

        sealed class Probe
        {
            public string Name;
            public Func<string> Sample;
            public float Interval, NextAt;
            public string Last;
        }

        static CupDebugCapture _inst;

        /// <summary>The one helper (created on first use, survives nothing past play mode).</summary>
        public static CupDebugCapture Instance
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("CupDebugCapture");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<CupDebugCapture>();
                }
                return _inst;
            }
        }

        readonly List<Job> _jobs = new List<Job>();
        readonly List<Probe> _probes = new List<Probe>();
        readonly StringBuilder _log = new StringBuilder();
        float _clock;
        CupPhase _lastCupPhase = CupPhase.Ended;
        RoundPhase _lastRoundPhase = RoundPhase.Idle;
        CupRoundDriver _lastDriver;
        bool _lastToss;
        string _lastTossStage = "";

        /// <summary>Seconds since the helper was created (unscaled).</summary>
        public float Clock => _clock;

        /// <summary>Capture `path` `delay` seconds after `trigger` first reads true.</summary>
        public void When(Func<bool> trigger, float delay, string path)
        {
            _jobs.Add(new Job { Path = path, Trigger = trigger, Delay = delay });
        }

        /// <summary>Capture a burst: the first `delay` after the trigger, then `repeat` more `interval` apart (files get -0, -1, ... suffixes).</summary>
        public void Burst(Func<bool> trigger, float delay, int repeat, float interval, string path)
        {
            _jobs.Add(new Job { Path = path, Trigger = trigger, Delay = delay, Repeat = repeat, Interval = interval });
        }

        /// <summary>Capture `delay` seconds after the current round driver enters `phase`.</summary>
        public void OnRoundPhase(RoundPhase phase, float delay, string path)
        {
            When(() => { var d = Trickshot.CupDirector.Instance; return d != null && d.Driver != null && d.Driver.Phase == phase; }, delay, path);
        }

        /// <summary>Capture a burst after the driver enters `phase`.</summary>
        public void BurstOnRoundPhase(RoundPhase phase, float delay, int repeat, float interval, string path)
        {
            Burst(() => { var d = Trickshot.CupDirector.Instance; return d != null && d.Driver != null && d.Driver.Phase == phase; }, delay, repeat, interval, path);
        }

        /// <summary>Capture `delay` seconds after the director enters `phase`.</summary>
        public void OnCupPhase(CupPhase phase, float delay, string path)
        {
            When(() => { var d = Trickshot.CupDirector.Instance; return d != null && d.Phase == phase; }, delay, path);
        }

        /// <summary>Capture now (end of frame).</summary>
        public void Now(string path)
        {
            When(() => true, 0f, path);
        }

        /// <summary>Log a sampled value every `interval` seconds whenever it changes.</summary>
        public void AddProbe(string name, float interval, Func<string> sample)
        {
            _probes.Add(new Probe { Name = name, Sample = sample, Interval = interval, NextAt = 0f });
        }

        public void ClearProbes() => _probes.Clear();
        public void ClearJobs() => _jobs.Clear();

        /// <summary>Append a line to the timeline.</summary>
        public void Note(string text)
        {
            _log.Append(_clock.ToString("0.00")).Append("  ").Append(text).Append('\n');
        }

        /// <summary>The timeline so far (cleared when `clear`).</summary>
        public string Dump(bool clear = true)
        {
            string s = _log.ToString();
            if (clear) _log.Length = 0;
            return s;
        }

        /// <summary>Pending (not yet fired) capture paths, for a sanity check.</summary>
        public string Pending()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _jobs.Count; i++) if (!_jobs[i].Done) sb.Append(System.IO.Path.GetFileName(_jobs[i].Path)).Append(_jobs[i].Armed ? "(armed) " : " ");
            return sb.ToString();
        }

        /// <summary>The coin toss's private stage name, or "" (a probe convenience).</summary>
        public static string TossStage()
        {
            var d = Trickshot.CupDirector.Instance;
            var toss = d != null ? d.Toss : null;
            if (toss == null) return "";
            var f = toss.GetType().GetField("_stage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var v = f != null ? f.GetValue(toss) : null;
            return v != null ? v.ToString() : "";
        }

        void Update()
        {
            _clock += Time.unscaledDeltaTime;
            TrackPhases();
            for (int i = 0; i < _probes.Count; i++)
            {
                var p = _probes[i];
                if (_clock < p.NextAt) continue;
                p.NextAt = _clock + p.Interval;
                string v;
                try { v = p.Sample(); } catch (Exception e) { v = "ERR " + e.GetType().Name + ": " + e.Message; }
                if (v == p.Last) continue;
                p.Last = v;
                Note(p.Name + " = " + v);
            }
            for (int i = 0; i < _jobs.Count; i++)
            {
                var j = _jobs[i];
                if (j.Done) continue;
                if (!j.Armed)
                {
                    bool hit;
                    try { hit = j.Trigger(); } catch (Exception e) { Note("trigger threw " + e.Message + " for " + j.Path); j.Done = true; continue; }
                    if (!hit) continue;
                    j.Armed = true;
                    j.FireAt = _clock + j.Delay;
                }
                if (_clock < j.FireAt) continue;
                string path = j.Repeat > 0 || j.Shot > 0 ? Suffix(j.Path, j.Shot) : j.Path;
                ScreenCapture.CaptureScreenshot(path);
                Note("shot " + System.IO.Path.GetFileName(path) + " (" + Describe() + ")");
                j.Shot++;
                if (j.Shot > j.Repeat) j.Done = true;
                else j.FireAt = _clock + j.Interval;
            }
        }

        static string Suffix(string path, int n)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            string ext = System.IO.Path.GetExtension(path);
            return System.IO.Path.Combine(dir, name + "-" + n + ext);
        }

        void TrackPhases()
        {
            var d = Trickshot.CupDirector.Instance;
            if (d == null) return;
            if (d.Phase != _lastCupPhase) { _lastCupPhase = d.Phase; Note("cup -> " + d.Phase); }
            var drv = d.Driver;
            if (drv != _lastDriver) { _lastDriver = drv; _lastRoundPhase = RoundPhase.Idle; Note("driver " + (drv != null ? "bound" : "gone")); }
            if (drv != null && drv.Phase != _lastRoundPhase)
            {
                _lastRoundPhase = drv.Phase;
                Note("round -> " + drv.Phase + " (" + Describe() + ")");
            }
            bool toss = d.Toss != null;
            if (toss != _lastToss) { _lastToss = toss; Note("toss " + (toss ? "began" : "ended")); }
            string ts = TossStage();
            if (ts != _lastTossStage) { _lastTossStage = ts; if (ts.Length > 0) Note("toss stage " + ts); }
        }

        /// <summary>A one-line state summary for the timeline.</summary>
        public static string Describe()
        {
            var d = Trickshot.CupDirector.Instance;
            if (d == null) return "no director";
            var drv = d.Driver;
            if (drv == null) return "cup " + d.Phase;
            return "kick " + drv.KickIndex + " kicker " + drv.Kicker + " score " + drv.ScoreA + "-" + drv.ScoreB
                   + " last " + (drv.LastOutcome.HasValue ? drv.LastOutcome.Value.ToString() : "-")
                   + " rig " + (d.Rig != null ? d.Rig.Current.ToString() : "-")
                   + " cam " + (d.Cam != null ? d.Cam.transform.position.ToString("0.0") : "-");
        }
    }
}
#endif
