using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// MATCH: the player's body slides in and takes the ball off a running opponent; both go down.
    ///
    /// Two figures and a ball, nothing else in frame. The carrier is an AI in away red who runs
    /// across the shot with the ball at his feet, driven the way Footballer drives a bot carry
    /// (steer the body, register as the ball's carrier, push it along every stride) rather than
    /// through the human Dribble component, which would need a ticked Striker and its capture
    /// gates.
    ///
    /// The tackle IS the real slide: a ScriptedInput gives the Striker both click edges inside
    /// SitWindow with both legs held and the stick forward, which is exactly what UpdateSit needs
    /// to commit, and the controller does the lunge, the pose and the hip drop itself. Contact
    /// replicates MatchGame.TrySlideTackle by hand - deterministically, because the shipped path
    /// runs a stochastic Dribble.ContestTackle that would leave the ball with the carrier some of
    /// the time, and a menu vignette has to land its beat every time.
    /// </summary>
    public class MatchScene : MenuScene
    {
        // Slow enough that a slide launched at SlideLunge (8.5 m/s) closes on him rather than
        // trailing behind: the old 3.0 let him run out from under the tackle every time.
        const float CarrySpeed = 1.7f;
        // He commits IMMEDIATELY on hover - a two-second vignette has no room for a run-up.
        const float TSlide = 0.04f;
        // TWO SECONDS, total. Long enough for the slide, the collision and both bodies hitting the
        // turf; short enough that a glance at the panel catches the whole thing.
        const float THold = 2.0f;
        // Generous against MatchGame's 1.7: these are two bodies on an empty stage rather than a
        // crowded box, and a menu vignette has to land its beat rather than sometimes whiff.
        const float TackleRange = 1.9f;
        const float TackleKnock = 4.5f;  // MatchGame's ball-win kick

        ActiveRagdoll _rag, _foeRag;
        Striker _striker;
        Knockdown _knock, _foeKnock;
        ScriptedInput _input;

        Vector3 _spot, _foeSpot, _ballHome;
        Quaternion _facing, _foeFacing;
        float _gait, _foeGait, _touch;
        bool _slid, _won;

        public override void Build()
        {
            // CLOSE TOGETHER, so the slide connects almost at once and the whole collision - both
            // bodies going down - happens inside the panel. The carrier crosses along +X; the
            // tackler starts just short of him and nearer the camera, and slides up into his path.
            // The slide launches at up to SlideLaunchMax and covers ground fast, so the gap is
            // deliberately about one slide's worth, not a run-up's worth.
            // 3.6 m apart along the approach. Solved against the real slide: it launches at
            // SlideLunge 8.5 m/s and decays by SlideFriction each frame, so from here it closes to
            // TackleRange after about a third of a second and roughly two metres of travel - long
            // enough to READ as a slide, early enough that both bodies are on the turf for most of
            // a two-second panel. Any closer and the tackle lands before the slide is visible.
            _foeSpot = Origin + new Vector3(-0.55f, 0f, 0.95f);
            _spot = Origin + new Vector3(-2.55f, 0f, -1.95f);
            _foeFacing = Quaternion.LookRotation(Vector3.right, Vector3.up);
            // AIMED AT THE INTERCEPT, not at where the carrier is standing now. The slide travels
            // along the striker's facing YAW (Striker.UpdateSit takes the direction from _facingYaw,
            // never from his velocity), so a facing locked onto the carrier's start point sends him
            // through empty turf behind a man who has already moved on. Solving where the two meet
            // is what makes the tackle connect instead of trail.
            _facing = Quaternion.LookRotation(InterceptDir(), Vector3.up);
            _ballHome = _foeSpot + new Vector3(0.5f, SimConfig.BallRadius, 0f);

            BuildFloor(60f, 60f, Origin);
            BuildBall(_ballHome);

            _rag = BuildPlayerBody("MsTackler", _spot, _facing, gloves: false);
            _input = new ScriptedInput();
            _striker = _rag.gameObject.AddComponent<Striker>();
            _striker.Init(_input, _rag);
            _striker.SetBall(Ball);
            _striker.IgnoreAcrobat = true;
            _knock = _rag.gameObject.AddComponent<Knockdown>();
            _knock.Init(_rag);

            _foeRag = BuildAiBody("MsCarrier", _foeSpot, _foeFacing,
                                  new Color(0.75f, 0.2f, 0.2f), new Color(0.5f, 0.13f, 0.13f));
            _foeKnock = _foeRag.gameObject.AddComponent<Knockdown>();
            _foeKnock.Init(_foeRag);
        }

        /// <summary>
        /// The flat direction from the tackler's spot to the point where a slide launched now
        /// meets the carrier. Closed form: the slide covers SlideLunge per second and the carrier
        /// CarrySpeed along his own line, so the meeting time solves the triangle between them.
        /// Falls back to a straight line at the carrier if the two speeds make no intercept.
        /// </summary>
        Vector3 InterceptDir()
        {
            Vector3 rel = _foeSpot - _spot; rel.y = 0f;
            Vector3 vFoe = (_foeFacing * Vector3.forward) * CarrySpeed;
            // |rel + vFoe*t| = SlideLunge*t  ->  quadratic in t.
            float a = vFoe.sqrMagnitude - SimConfig.SlideLunge * SimConfig.SlideLunge;
            float bq = 2f * Vector3.Dot(rel, vFoe);
            float c = rel.sqrMagnitude;
            float tMeet = -1f;
            if (Mathf.Abs(a) < 1e-4f) { if (Mathf.Abs(bq) > 1e-4f) tMeet = -c / bq; }
            else
            {
                float disc = bq * bq - 4f * a * c;
                if (disc >= 0f)
                {
                    float s = Mathf.Sqrt(disc);
                    float t1 = (-bq + s) / (2f * a), t2 = (-bq - s) / (2f * a);
                    // The soonest meeting that is actually in the future.
                    tMeet = Mathf.Min(t1 > 0f ? t1 : float.MaxValue, t2 > 0f ? t2 : float.MaxValue);
                    if (tMeet == float.MaxValue) tMeet = -1f;
                }
            }
            Vector3 aim = tMeet > 0f ? rel + vFoe * tMeet : rel;
            aim.y = 0f;
            return aim.sqrMagnitude > 1e-4f ? aim.normalized : Vector3.forward;
        }

        public override void Reset()
        {
            _striker.ForceRecover();
            _knock.Cancel();
            _foeKnock.Cancel();
            _rag.ResetTo(_spot, _facing);
            _foeRag.ResetTo(_foeSpot, _foeFacing);
            // Drop the bot carry both ways round, or the ball keeps phasing through the carrier
            // and the static holder claim outlives the scene.
            Dribble.SetCarryCollision(Ball, _foeRag, false);
            Ball.SetDribbleCarrier(null);
            Ball.ResetTo(_ballHome);
            _input.Clear();
            _gait = _foeGait = 0f;
            _touch = 0f;
            _slid = false; _won = false;
            Clock = 0f;
            Done = false;
        }

        public override void Freeze()
        {
            // Ball ownership is a process-wide static (Dribble._holder / BallController's carrier),
            // so a frozen scene must not keep claiming it - a real match built afterwards would
            // find the ball already owned by a menu body.
            Dribble.SetCarryCollision(Ball, _foeRag, false);
            Ball.SetDribbleCarrier(null);
            Dribble.ReleaseHolder();
            base.Freeze();
        }

        public override void Thaw()
        {
            base.Thaw();
            // Register the carry AFTER the bodies are live: SetCarryCollision suspends ball-vs-limb
            // contact for the carrier, and PhysX refuses an ignore on a disabled collider.
            Dribble.SetCarryCollision(Ball, _foeRag, true);
            Ball.SetDribbleCarrier(_foeRag);
        }

        public override void Tick(float dt)
        {
            Clock += dt;

            // ---- the carrier: run across, pushing the ball along in front of him. Once he is
            // felled this whole block stops, so the knockdown carries him rather than his own
            // locomotion dragging him out of the tackle.
            if (!_foeKnock.Down && !_won)
            {
                Vector3 dir = _foeFacing * Vector3.forward;
                _foeRag.UprightLock = true;
                _foeRag.LocomotionEnabled = true;
                _foeRag.MoveInput = dir * CarrySpeed;
                _foeRag.FacingRotation = _foeFacing;
                RunGait(_foeRag, ref _foeGait, dt);
                if (!_won)
                {
                    Vector3 feet = _foeRag.Pelvis.position; feet.y = 0f;
                    Vector3 ballFlat = Ball.Rb.position; ballFlat.y = 0f;
                    _touch -= dt;
                    if (_touch <= 0f || Dribble.NeedsCorrectiveTouch(feet, dir, ballFlat))
                    {
                        float interval = Dribble.StrideInterval(_foeRag, false);
                        _touch = interval;
                        Dribble.Touch(Ball, feet, dir, _foeRag.MoveInput, interval,
                                      Dribble.TouchDistance(_foeRag.GroundSpeed, 0.5f, false), 0f);
                    }
                }
            }

            // ---- the tackler: run in, then commit the slide.
            if (!_slid)
            {
                _input.MoveWish = new Vector2(0f, 1f);
                _input.Sprint = true;
                if (Clock >= TSlide)
                {
                    // Both click EDGES within SitWindow, both legs held, stick forward past the
                    // deadzone: the exact combination UpdateSit arms a slide on. Held from here so
                    // the edges land on this frame's Commit.
                    _input.LegL = true;
                    _input.LegR = true;
                    _slid = true;
                }
            }

            _input.Commit();
            _striker.Tick();
            // The gait rides on top of the controller, which clears pose overrides every tick.
            if (!_striker.IsSliding && !_knock.Down && Clock < TSlide)
                RunGait(_rag, ref _gait, dt);

            // ---- contact: the deterministic half of MatchGame.TrySlideTackle.
            if (_striker.IsSliding && !_won)
            {
                Vector3 a = _rag.Pelvis.position, b = _foeRag.Pelvis.position;
                a.y = 0f; b.y = 0f;
                if (Vector3.Distance(a, b) < TackleRange)
                {
                    _won = true;
                    Vector3 dir = (b - a); dir.y = 0f;
                    if (dir.sqrMagnitude < 0.01f) dir = _foeFacing * Vector3.forward;
                    dir.Normalize();
                    // Release the carry first: the ball must be free before it is knocked away.
                    Dribble.SetCarryCollision(Ball, _foeRag, false);
                    Ball.SetDribbleCarrier(null);
                    Vector3 fwd = Quaternion.Euler(0f, _striker.Yaw, 0f) * Vector3.forward;
                    Ball.KickTo(fwd * TackleKnock + Vector3.up * 0.4f, _rag);
                    // BOTH go down, as a connected slide does in a real match. Fell() force-recovers
                    // a busy striker before shoving him, so the tackler skips his own slide-limp and
                    // takes Knockdown's 1.4 s instead - which is why THold has to outlast that.
                    // BOTH go down, as a connected slide does in a real match. Fell() force-recovers
                    // a busy striker before shoving him, so the tackler skips his own slide-limp and
                    // takes Knockdown's limp instead.
                    _foeKnock.Fell(dir);
                    _knock.Fell(-dir);
                    // Hand the carrier's body fully to the fall: locomotion left on would have him
                    // jogging on his side.
                    _foeRag.LocomotionEnabled = false;
                    _foeRag.MoveInput = Vector3.zero;
                }
            }

            if (Clock >= THold) Done = true;
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Side on to the challenge, low enough that the slide reads along the ground.
            // Low and close: a slide reads along the ground, and both figures have to fit the
            // width as the tackler comes across.
            // Fitted to both bodies plus the ground the slide covers, side on and low so the
            // challenge reads along the turf rather than down onto it.
            // Centred on the CONTACT POINT, which sits near the carrier rather than midway - the
            // tackler covers most of the closing distance himself. Wide enough to hold the run-in,
            // the slide and both bodies after they are knocked apart, since the falls are the
            // payoff and a fall that leaves the frame is the same as no fall at all.
            fov = 46f;
            FitCamera(Origin + new Vector3(-1.2f, 0.80f, 0.10f), new Vector3(3.8f, 1.7f, 1.1f),
                      new Vector3(0.30f, 0.36f, -1f), fov, PanelAspect, out camPos, out lookAt);
        }

        public override void Destroy()
        {
            // Never leave a destroyed body owning the ball: the carrier claim is global state.
            if (Ball != null) { Ball.SetDribbleCarrier(null); Dribble.ReleaseHolder(); }
            base.Destroy();
        }
    }
}
