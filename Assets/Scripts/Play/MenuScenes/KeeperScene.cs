using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// GOALKEEPER: the player's own body, in gloves, saves a shot and gets back to his feet.
    ///
    /// Just the keeper and the ball - no shooter, no goal, no ground. The ball is launched by code
    /// from off-camera, which is also the only way to make him save anything: every launcher in
    /// Goalkeeper is private, so a save is produced by PRESENTING A BALL HE MUST REACH and letting
    /// the AI decide for itself. Nothing here poses him.
    ///
    /// FIVE SHOTS, PICKED AT RANDOM each time the panel is hovered, because one repeated dive gets
    /// old on a screen the player sees every session. Each is aimed at a different branch of the
    /// same decision tree in Goalkeeper.Tick, so they look genuinely different:
    ///
    ///   Layout - wide, in the 1.0-1.7 m mid band and outside his dead band: a full horizontal
    ///            dive, thrown either way.
    ///   Lunge  - low and just inside AiKeeperSplayReach: a grounded splay in place, arm and leg
    ///            thrown at the ball without the full layout.
    ///   Catch  - slow, near-central and chest high, inside KeeperClaimReach and under the chest
    ///            speed ceiling: KeeperHands gathers it in instead of parrying it away.
    ///   Spread - low and central, inside AiKeeperSplitWidth: the planted block, which the code
    ///            deliberately keeps OUT of the dive state so he stays on his feet.
    ///
    /// Those gates are numeric and unforgiving, so each variant carries the arithmetic that puts it
    /// on the right side of them - see BuildShots.
    /// </summary>
    public class KeeperScene : MenuScene
    {
        /// <summary>
        /// One presented shot. The flight time is load-bearing rather than decoration: the keeper's
        /// DEAD BAND is how far he could walk in the time remaining, so a slow ball wide of him is
        /// a sidestep while the same ball delivered quickly is a dive.
        /// </summary>
        readonly struct Shot
        {
            public readonly Vector3 From, Aim;
            public readonly float Time;
            public Shot(Vector3 from, Vector3 aim, float time) { From = from; Aim = aim; Time = time; }
        }

        const float ShotFromZ = 9f;      // struck from this far out, off camera
        const float TShoot = 0.35f;      // a beat of him set before the ball comes
        // Long enough to show the save AND him getting back up: the recovery to Ready is part of
        // the beat, and a panel that cuts before he stands reads as broken.
        const float THold = 4.4f;

        Vector3 _line;                   // the goal line he defends; nothing is drawn there
        ActiveRagdoll _rag;
        Goalkeeper _keeper;
        Vector3 _home;
        Quaternion _facing;
        Shot[] _shots;
        int _pick;
        bool _shot;

        public override void Build()
        {
            _line = Origin;
            // He stands just in FRONT of his line (on the -Z side, the way he faces) and every shot
            // comes at him from further out that way.
            _home = _line - new Vector3(0f, 0f, 0.6f);
            _facing = Quaternion.LookRotation(Vector3.back, Vector3.up);

            BuildFloor(60f, 60f, _home);
            BuildBall(_home + new Vector3(0f, SimConfig.BallRadius, -ShotFromZ));

            _rag = BuildPlayerBody("MsKeeper", _home, _facing, gloves: true);
            _keeper = _rag.gameObject.AddComponent<Goalkeeper>();
            // The 4-arg Init is the one that takes a goal centre: the 2-arg overload is welded to
            // the real pitch's SimConfig.GoalCenter, thousands of metres from this stage. outSign
            // is -1 because he must face -Z, INTO the shot - the brain reads a ball as incoming
            // only when it travels against that direction, and with the sign wrong he never
            // reacts at all.
            _keeper.Init(_rag, Ball, _line, -1f);
            _keeper.ResetTo(_home);

            BuildShots();

            // Catch whatever he parries away, so it does not roll off across the stage.
            BuildCatcher(new Vector3(30f, 8f, 0.25f), _line + new Vector3(0f, 4f, 1.8f));
        }

        void BuildShots()
        {
            // Launch points sit off camera, roughly on the line of the shot so each arc reads as a
            // struck ball rather than a lob dropped in from nowhere.
            Vector3 Out(float x, float y) => _line + new Vector3(x, y, -ShotFromZ);

            _shots = new[]
            {
                // LAYOUT, both ways. Wide (2.4 m, against a dead band of roughly 1.5 m at this
                // flight time) and inside the 1.0-1.7 m mid band, which is the branch that throws
                // him out flat rather than stepping him across.
                new Shot(Out(-0.6f, 0.95f), _line + new Vector3( 2.40f, 1.30f, 0f), 0.45f),
                new Shot(Out( 0.6f, 0.95f), _line + new Vector3(-2.40f, 1.30f, 0f), 0.45f),

                // LUNGE. Low (under AiKeeperLowBallHeight 1.0) and just inside AiKeeperSplayReach
                // (1.6 scaled by ability, about 1.7 m here): a grounded splay in place, which is a
                // visibly different save from the layout above.
                new Shot(Out(-0.5f, 0.75f), _line + new Vector3(1.45f, 0.45f, 0f), 0.42f),

                // CATCH. Central, chest high and SLOW. KeeperHands.CanClaim wants the ball inside
                // KeeperClaimReach (0.62 m scaled by ability), within its chest band, and under the
                // chest speed ceiling - a hard shot can never be caught, only parried, so this one
                // is deliberately gentle. Slightly to his right so the gather reads as hands
                // rather than a ball hitting his chest.
                new Shot(Out(0.15f, 1.15f), _line + new Vector3(0.30f, 1.15f, 0f), 0.95f),

                // SPREAD. Low and central, inside AiKeeperSplitWidth (1.2 m): the planted block,
                // which Goalkeeper deliberately keeps out of the dive state so he stays on his feet
                // with everything thrown wide.
                new Shot(Out(0f, 0.65f), _line + new Vector3(0.45f, 0.35f, 0f), 0.40f),
            };
        }

        public override void Reset()
        {
            // A different save each time the panel is entered. Random is right here: this is
            // presentation, nothing carries across runs, and a repeated pick is harmless.
            _pick = _shots != null && _shots.Length > 0 ? Random.Range(0, _shots.Length) : 0;
            _keeper.ResetTo(_home);
            if (_shots != null) Ball.ResetTo(_shots[_pick].From);
            _shot = false;
            Clock = 0f;
            Done = false;
        }

        public override void Tick(float dt)
        {
            Clock += dt;
            if (!_shot && Clock >= TShoot)
            {
                _shot = true;
                var s = _shots[_pick];
                Ball.ResetTo(s.From);
                Ball.LaunchTo(s.Aim, s.Time, Vector3.zero, 0f);
            }
            // The brain has no Update of its own; it only reacts while it is ticked.
            _keeper.Tick();
            if (Clock >= THold) Done = true;
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Centred on the KEEPER, not on any one shot: he starts in the middle of the panel and
            // whichever save comes throws him from there, so the box is symmetric about him and
            // wide enough either way to hold a full layout plus the landing. Pulled back further
            // than a body needs - the extra margin IS the point, since a dive that leaves the
            // frame reads as the panel being broken.
            fov = 44f;
            FitCamera(_home + new Vector3(0f, 1.15f, 0.3f), new Vector3(4.2f, 2.0f, 0.6f),
                      new Vector3(-0.10f, 0.30f, -1f), fov, PanelAspect, out camPos, out lookAt);
        }
    }
}
