using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>Which tackle path made the attempt. Split because the three have different geometry and
    /// only one of them (Slide) is invisible to the per-player stat table.</summary>
    public enum ProbeTackle { Ai, Human, Slide }

    /// <summary>
    /// DEV-ONLY match measurement harness. Changes no gameplay.
    ///
    /// WHY IT EXISTS: five gameplay complaints survived three rounds of fixes that were verified by
    /// COMPILING instead of by playing. Nobody had a number for how often a shot scores, how often the
    /// keeper saves, or how often a tackle steals - so every "fixed" claim was unfalsifiable. This
    /// produces those numbers, and that is all it does.
    ///
    /// SWITCHING IT ON: F9 in a running match, or MatchProbe.On = true from anywhere (that is how an
    /// automated run over the editor bridge arms it). Off by default, every time.
    ///
    /// COST WHEN OFF: in a shipped player, zero - every body below is inside
    /// #if UNITY_EDITOR || DEVELOPMENT_BUILD, so the methods compile to empty and the call sites drop
    /// out. In the editor with On == false it is one Input.GetKeyDown plus one bool test per frame and
    /// nothing allocates. Candid limit: the F9 poll HAS to sit before the On test or the thing could
    /// never be switched on, so editor-off is negligible rather than free.
    ///
    /// DERIVED WHERE POSSIBLE, so the harness cannot contradict the post-match board: shots, goals,
    /// saves and standing-tackle wins are per-frame DELTAS off MatchGame's existing PlayerStat table
    /// and live score. Three things need their own call site because the game counts them nowhere:
    ///   - tackle ATTEMPTS. The game counts only WINS. That single gap is why the steal rate the user
    ///     complains about had never been measured by anybody.
    ///   - SLIDE-tackle wins. TrySlideTackle knocks the ball loose with its own KickTo and never calls
    ///     WinBall, so NoteTackle never sees it and slide steals are missing from the board too. Bug
    ///     recorded here, not fixed here.
    ///   - shots ON TARGET, latched off the ball's launch velocity on the frame a shot registers.
    /// </summary>
    public static class MatchProbe
    {
        public static bool On;

        // Shooting / keeping.
        public static int Shots, OnTarget, Goals, Saves;

        // Tackling. Attempts split by path; wins split because they arrive by two different routes.
        public static int AtkAi, AtkHuman, AtkSlide, WinTackle, WinSlide;

        // Floor integrity, for "guys go through the ground". LIVE and PUPPET are counted separately and
        // must never be added: ActiveRagdoll.FloorRescue deliberately skips kinematic bodies, so a
        // violation on a kinematic display puppet means "something writes positions under the turf" -
        // a different bug from "the invariant failed", and the split is the whole diagnostic value.
        public static int FloorFramesLive, FloorFramesPuppet;
        public static float FloorWorstY = float.MaxValue;
        public static int FloorWorstBone = -1;

        // One line an automated run can read straight off the bridge.
        public static string Report()
        {
            int atk = AtkAi + AtkHuman + AtkSlide, won = WinTackle + WinSlide;
            return "SHT " + Shots + " ON " + OnTarget + " (" + Pct(OnTarget, Shots) + ")"
                 + " G " + Goals + " SV " + Saves + " SAVE% " + Pct(Saves, OnTarget)
                 + " | TKL att " + atk + " won " + won + " (" + Pct(won, atk) + ")"
                 + " ai " + AtkAi + " hum " + AtkHuman + " slide " + AtkSlide + "/" + WinSlide
                 + " | FLOOR live " + FloorFramesLive + " puppet " + FloorFramesPuppet
                 + " worstY " + (FloorWorstY == float.MaxValue ? "-" : FloorWorstY.ToString("F3"));
        }

        static string Pct(int n, int d) => d <= 0 ? "n/a" : (100f * n / d).ToString("F0") + "%";

        public static void Reset()
        {
            Shots = OnTarget = Goals = Saves = 0;
            AtkAi = AtkHuman = AtkSlide = WinTackle = WinSlide = 0;
            FloorFramesLive = FloorFramesPuppet = 0;
            FloorWorstY = float.MaxValue; FloorWorstBone = -1;
            _seeded = false; _log.Clear();
        }

        /// <summary>A tackle was ATTEMPTED - the lunge committed, before anyone knows if it reaches.</summary>
        public static void TackleAttempt(ProbeTackle kind)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!On) return;
            if (kind == ProbeTackle.Ai) AtkAi++;
            else if (kind == ProbeTackle.Human) AtkHuman++;
            else AtkSlide++;
#endif
        }

        /// <summary>A SLIDE tackle knocked the ball off a carrier. Explicit because this path never
        /// reaches NoteTackle, so the delta the rest of the harness reads cannot see it.</summary>
        public static void SlideWin()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (On) WinSlide++;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static int _lastShots, _lastGoals, _lastSaves, _lastTackles;
        static bool _seeded;
        const int LogKeep = 6;
        static readonly List<string> _log = new List<string>(LogKeep + 1);
#else
        static bool _seeded;
        static readonly List<string> _log = new List<string>();
#endif

        /// <summary>
        /// Once per frame from the END of MatchGame.Update, which is on purpose: the AI brains have
        /// already run by then, so a shot struck this frame still carries its launch velocity on the
        /// rigidbody and the on-target verdict is read off the real strike rather than a frame later.
        /// </summary>
        public static void Tick(MatchGame g, BallController ball, List<Footballer> bodies)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F9)) { On = !On; if (On) { Reset(); Overlay.Ensure(); } }
            if (!On || g == null) return;

            int shots = 0, saves = 0, tackles = 0;
            var stats = g.Stats;
            for (int i = 0; i < stats.Count; i++)
            { shots += stats[i].shots; saves += stats[i].saves; tackles += stats[i].tackles; }
            int goals = g.HomeScore + g.AwayScore;

            if (!_seeded)
            { _lastShots = shots; _lastGoals = goals; _lastSaves = saves; _lastTackles = tackles; _seeded = true; }

            int dShot = shots - _lastShots, dGoal = goals - _lastGoals;
            int dSave = saves - _lastSaves, dTkl = tackles - _lastTackles;
            _lastShots = shots; _lastGoals = goals; _lastSaves = saves; _lastTackles = tackles;

            // Deltas can only go up within a match; a negative one means a rematch reset the table, so
            // clamp rather than subtract and quietly corrupt the run.
            if (dGoal > 0) Goals += dGoal;
            if (dSave > 0) Saves += dSave;
            if (dTkl > 0) WinTackle += dTkl;

            if (dShot > 0)
            {
                Shots += dShot;
                // A headed or volleyed goal never went through a shot hook, so AttributeGoal back-fills
                // the scorer's SHT to match G. A shot delta on a GOAL frame is therefore retro-credit for
                // a strike that already happened, not a live launch to classify - and by now the ball is
                // behind the line, so reading its velocity would produce a garbage verdict. A goal is on
                // target by definition: bank it and move on.
                if (dGoal > 0) { OnTarget += dShot; Note("GOAL retro"); }
                else if (ball != null && ball.Rb != null)
                {
                    bool hit = GoalBound(ball.Rb.position, ball.Rb.linearVelocity, g.HalfLength,
                                         out float cx, out float cy);
                    if (hit) OnTarget += dShot;
                    Note((hit ? "ON  " : "OFF ") + "x " + cx.ToString("F2") + "  y " + cy.ToString("F2"));
                }
            }

            // FLOOR SCAN. One increment per FRAME per category, not per bone, so the number reads as
            // "frames with a violation" and one badly sunk body cannot inflate it 13x. Cost: 13 float
            // compares per body, 130 at 5-a-side, which is noise next to the servo already running.
            bool badLive = false, badPuppet = false;
            float floor = SimConfig.BodyFloorClampY;
            for (int i = 0; bodies != null && i < bodies.Count; i++)
            {
                var r = bodies[i] != null ? bodies[i].Ragdoll : null;
                if (r == null || r.Pelvis == null) continue;
                bool kin = r.Pelvis.isKinematic;
                for (int b = 0; b < (int)Bone.Count; b++)
                {
                    var rb = r.Rb((Bone)b);
                    if (rb == null) continue;
                    float y = rb.position.y;
                    if (y >= floor) continue;
                    if (kin) badPuppet = true; else badLive = true;
                    if (y < FloorWorstY) { FloorWorstY = y; FloorWorstBone = b; }
                }
            }
            if (badLive) FloorFramesLive++;
            if (badPuppet) FloorFramesPuppet++;

            Overlay.Ensure();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static void Note(string s)
        { _log.Add(s); if (_log.Count > LogKeep) _log.RemoveAt(0); }

        /// <summary>
        /// Would this ball, left alone, cross the goal line inside the frame?
        ///
        /// LIMITS, stated because they decide whether the save rate below it means anything. Drag-free
        /// parabola: the ball's linearDamping is 0.02 (measured on the live prefab), which over the
        /// 0.35-0.90 s an AI shot is airborne costs under 1% of range - about 0.18 m on a 20 m strike -
        /// against the 0.52 m margin the AI's own aim clamp leaves inside the post, so it cannot flip a
        /// verdict at the aim points the AI actually uses. It also ignores deflections AND the keeper,
        /// which is the point: "on target" has to mean "goalbound when struck" or SAVE% computed over it
        /// is circular.
        /// </summary>
        static bool GoalBound(Vector3 p, Vector3 v, float halfLength, out float x, out float y)
        {
            x = 0f; y = 0f;
            if (Mathf.Abs(v.z) < 0.5f) return false;              // not travelling at a goal
            float lineZ = v.z > 0f ? halfLength : -halfLength;
            float t = (lineZ - p.z) / v.z;
            if (t <= 0f || t > 4f) return false;                  // behind us, or a lob with no chance
            x = p.x + v.x * t;
            y = p.y + v.y * t + 0.5f * Physics.gravity.y * t * t;
            return Mathf.Abs(x) <= SimConfig.GoalWidth * 0.5f && y >= 0f && y <= SimConfig.GoalHeight;
        }

        /// <summary>
        /// The readout hosts itself. Deliberate: MatchGame then needs exactly ONE new line (the Tick
        /// call) and no OnGUI patch, so the harness can be pulled back out in one edit.
        /// </summary>
        class Overlay : MonoBehaviour
        {
            static Overlay _inst;

            public static void Ensure()
            {
                if (_inst != null) return;
                var go = new GameObject("MatchProbeOverlay");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<Overlay>();
            }

            void OnGUI()
            {
                if (!On) return;
                int atk = AtkAi + AtkHuman + AtkSlide, won = WinTackle + WinSlide;
                var sb = new System.Text.StringBuilder(256);
                sb.Append("PROBE  F9 off\n");
                sb.Append("SHT ").Append(Shots).Append("   ON ").Append(OnTarget)
                  .Append(" ").Append(Pct(OnTarget, Shots))
                  .Append("   G ").Append(Goals).Append("   SV ").Append(Saves)
                  .Append("   SAVE% ").Append(Pct(Saves, OnTarget)).Append('\n');
                sb.Append("TKL att ").Append(atk).Append("  won ").Append(won)
                  .Append(" ").Append(Pct(won, atk))
                  .Append("   ai ").Append(AtkAi).Append("  hum ").Append(AtkHuman)
                  .Append("  slide ").Append(AtkSlide).Append('/').Append(WinSlide).Append('\n');
                sb.Append("FLOOR live ").Append(FloorFramesLive)
                  .Append("  puppet ").Append(FloorFramesPuppet)
                  .Append("  worst ").Append(FloorWorstY == float.MaxValue ? "-" : FloorWorstY.ToString("F3"))
                  .Append(FloorWorstBone >= 0 ? " " + ((Bone)FloorWorstBone).ToString() : "");
                for (int i = 0; i < _log.Count; i++) sb.Append('\n').Append(_log[i]);

                var st = new GUIStyle(GUI.skin.box)
                { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = false };
                st.normal.textColor = Color.white;
                GUI.Box(new Rect(8f, 8f, 430f, 34f + 16f * (3 + _log.Count)), sb.ToString(), st);
            }
        }
#endif
    }
}
