using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The coin toss ceremony at the start of every round (design 7.1, 6.11, 6.12). Runs BETWEEN
    /// the driver's Configure and Begin, in its Idle phase, and calls `onDone` when the flip has
    /// settled and been announced; the flow then calls Begin() (the driver's Intro entry re-parks
    /// everyone, so nothing walked here has to walk back).
    ///
    /// The beats:
    ///  1. APPROACH. The referee walks from his mark to the ceremony spot (the penalty spot, two
    ///     metres short of it so the dead ball is not underfoot) and the captains jog to 1.2 m
    ///     either side of him, facing him - the human's shooter body and the AI nation's body
    ///     (Solo: the player alone with the referee). The rig cuts to the static wide shot from
    ///     the goal side. The HEADS / TAILS overlay is up from the first frame, so a decisive
    ///     caller never waits for a walk.
    ///  2. THE CALL. Every human present picks HEADS or TAILS (director.CallCoin: the director
    ///     records every pick, the official caller's decides kick-off, the rest are predictions).
    ///     Picks can be changed until the flip. The flip starts once the official call is in AND
    ///     everyone is on their mark; CupTuning.CoinCallTimeout with no official call picks HEADS
    ///     for the caller (a remote caller gets a short grace for the host's echo).
    ///  3. THE FLIP. The referee flicks his right hand and a gold coin (one mesh, one material,
    ///     both freed) leaves it on a scripted ballistic arc - CupTuning.CoinFlightSeconds up and
    ///     down, spinning end over end at ~CoinSpinRps - lands CoinLandDistance in front of him,
    ///     bounces once and settles face-up on the SEEDED result. The result is the round's Coin
    ///     stream's first draw (CupSalts.Coin), the same number the flow would compute; the first
    ///     kicker follows from it and the official call through CupRoundRules.FirstKickerFromCall,
    ///     so the coin on screen can never disagree with who kicks first. Recorded on the driver
    ///     (SetCoinOutcome, SetFirstKicker) the moment the flip starts.
    ///  4. THE HOLD, the flash ("HEADS" / "TAILS", neutral, with "GHANA KICK FIRST" under it),
    ///     and in Co-op the calls band (name, call, check or cross) for CupTuning.CallsBandSeconds.
    ///
    /// Bodies are only moved where they are simulated (Local / Host); on a Client the overlay,
    /// the coin and the camera run and the snapshot walks the puppets. The overlay is drawn from
    /// the director's GUI hook list (in front of the HUD) and obeys the IMGUI modal rules: both
    /// buttons and the click blocker are allocated on every pass, a hidden button is parked
    /// off-screen and disabled rather than skipped.
    /// </summary>
    public sealed class CupCoinToss : MonoBehaviour
    {
        // ---- local tunables (feel; the designed beats are in CupTuning) -----------------------
        /// <summary>The referee stands this far short of the penalty spot (the ball sits ON the spot in Penalties).</summary>
        public const float CeremonyBack = 2f;
        /// <summary>The captains' approach pace (m/s): a jog, so an 8 m approach lands inside the call window.</summary>
        public const float CaptainJogSpeed = 3.2f;
        /// <summary>Walkers still short of their marks after this are snapped there (s).</summary>
        public const float ApproachTimeout = 4.5f;
        /// <summary>Extra wait for a REMOTE official caller's echo before the timeout picks heads for him (s).</summary>
        public const float RemoteCallGrace = 1.5f;
        /// <summary>The referee's hand flick before the coin leaves it, and how long the arm stays up after.</summary>
        public const float FlickSeconds = 0.35f;
        public const float ArmHoldSeconds = 0.4f;
        public const float ArmDropSeconds = 0.5f;
        /// <summary>The one small bounce on landing.</summary>
        public const float BounceSeconds = 0.3f;
        public const float BounceHeight = 0.10f;
        public const float BounceDrift = 0.08f;
        /// <summary>The hand point along a forearm (the AddGlove offset).</summary>
        public const float HandDrop = 0.31f;
        /// <summary>
        /// The gravity the SCRIPTED arc is solved with (m/s^2). Real gravity, on purpose: the
        /// project's Physics.gravity is doubled (SimConfig.Gravity, -19.6, the arcade ball feel),
        /// and a coin that must stay up CoinFlightSeconds under that apexes 5.7 m above the turf -
        /// clean out of the top of the wide shot for most of the flight (measured in play mode).
        /// Under 9.81 the same 1.4 s flight from a hand at ~1.7 m apexes near 3.3 m, inside the frame.
        /// </summary>
        public const float ArcGravity = 9.81f;
        /// <summary>Overlay geometry (design 6.11 / 6.12), in MenuScale's 1280x760 canvas.</summary>
        public const float ButtonW = 180f, ButtonH = 60f, ButtonGap = 24f;
        public const float BandW = 220f, BandRowH = 26f;

        enum Stage { Approach, Flick, Flight, Bounce, Hold, Band, Done }

        sealed class Walker
        {
            public CupBody Body;
            public Vector3 Target;
            public Quaternion Facing;
            public float Speed, GaitPhase, GaitAmount;
            public bool Arrived;
        }

        CupDirector _director;
        CupRoundDriver _driver;
        CupCameraRig _rig;
        Action _onDone;
        Stage _stage = Stage.Approach;
        float _t, _age;
        bool _sim;

        // Marks.
        Vector3 _refSpot;
        Quaternion _refFacing = Quaternion.identity;
        CupReferee _ref;
        bool _refArrived;
        readonly List<Walker> _walkers = new List<Walker>();

        // The call.
        CupSide _callerSide;
        int _callerSlot = -1;
        CoinFace? _localPick;
        CoinFace? _officialCall;
        CoinFace _result;
        CoinFace _aiCall;
        CupSide _firstKicker;
        bool _flipStarted;
        bool _warnedPhase;

        // The coin.
        GameObject _coinGo;
        Mesh _coinMesh;
        Material _coinMat;
        MeshRenderer _coinRenderer;
        Vector3 _p0, _p1, _spinAxis;
        float _v0, _revs;
        Quaternion _restRot = Quaternion.identity;

        // Overlay.
        static GUIStyle _header, _hint, _tag, _button, _bandTitle, _bandName, _bandCall;
        bool _hooked;

        // ==========================================================================================
        // Public surface
        // ==========================================================================================

        /// <summary>
        /// Start the ceremony for a configured round. Returns null (and fires onDone at once) when
        /// there is no scene to run it in, so a flow can always chain onDone -> Begin.
        /// </summary>
        public static CupCoinToss Begin(CupDirector director, CupRoundDriver driver, CupCameraRig rig, Action onDone)
        {
            if (director == null || driver == null || !driver.Configured || driver.Setup == null || driver.Setup.Root == null)
            {
                CupLog.Warn("CupCoinToss.Begin: no configured round to run the ceremony in");
                onDone?.Invoke();
                return null;
            }
            var go = new GameObject("CupCoinToss");
            go.transform.SetParent(driver.Setup.Root, false);
            var toss = go.AddComponent<CupCoinToss>();
            toss._director = director;
            toss._driver = driver;
            toss._rig = rig;
            toss._onDone = onDone;
            toss.Setup();
            return toss;
        }

        /// <summary>The ceremony has finished (onDone fired) or was cancelled.</summary>
        public bool Done => _stage == Stage.Done;
        /// <summary>Calls are still being taken (before the flip).</summary>
        public bool CallingOpen => !_flipStarted && _stage == Stage.Approach;
        /// <summary>The face the coin shows, once the flip has started.</summary>
        public CoinFace? Result => _flipStarted ? _result : (CoinFace?)null;
        /// <summary>The official caller's call, once the flip has started.</summary>
        public CoinFace? OfficialCall => _officialCall;
        /// <summary>Who kicks first, once the flip has started.</summary>
        public CupSide? FirstKicker => _flipStarted ? _firstKicker : (CupSide?)null;
        /// <summary>The local human's current pick (official or prediction).</summary>
        public CoinFace? LocalPick => _localPick;

        /// <summary>The local human's HEADS / TAILS (the overlay's buttons; a keybind could call it too). Ignored once the flip has started.</summary>
        public void Call(CoinFace face)
        {
            if (!CallingOpen || !LocalCanCall) return;
            _localPick = face;
            if (_director.Phase != CupPhase.CoinToss && !_warnedPhase)
            {
                // The director drops CallCoin outside its CoinToss phase; the ceremony still runs
                // on the local pick, but predictions and tallies are lost until the flow sets it.
                _warnedPhase = true;
                CupLog.Warn("CupCoinToss: director phase is " + _director.Phase + ", not CoinToss - calls are not being recorded");
            }
            _director.CallCoin(face);
        }

        /// <summary>Abort the ceremony (a leaver, End Match): cleans up, never fires onDone.</summary>
        public void Cancel()
        {
            if (_stage == Stage.Done) return;
            _stage = Stage.Done;
            _onDone = null;
            StopWalkers();
            Unhook();
            Destroy(gameObject);
        }

        // ==========================================================================================
        // Setup
        // ==========================================================================================

        void Setup()
        {
            var s = _driver.Setup;
            _sim = _driver.SimulatesBodies;

            // The ceremony spot: on the goal axis, two metres short of the penalty spot, the
            // referee facing the goal - which is where the camera is, so the flip is seen face on
            // and the captains flank him in profile.
            Vector3 ps = CupSpots.Ground(CupSpots.PenaltySpot);
            _refSpot = ps - Vector3.forward * CeremonyBack;
            _refFacing = Quaternion.LookRotation(Vector3.forward, Vector3.up);

            // The official caller (MakeRoundSetup ran CoinCallerFor) and the seeded result: the
            // Coin stream's FIRST draw, exactly what the flow would compute for itself. The second
            // draw is an AI caller's call, for the round where a leaver's side has to call.
            _callerSide = s.CoinCaller;
            _callerSlot = s.CoinCallerSlot;
            var coin = s.Stream(CupSalts.Coin(s.Stage, _driver.Data.Index));
            _result = coin.Coin();
            _aiCall = coin.Coin();

            // Walkers: the referee to the spot, the captains to 1.2 m either side facing him. The
            // team's captain takes the side its lineup is on (x < 0) so his walk is the short one.
            _ref = _driver.RefereeActor;
            var teamSide = s.TeamSide;
            var other = CupSides.Other(teamSide);
            AddCaptain(_driver.CaptainBody(teamSide), -CupTuning.CaptainOffset);
            if (s.Style != CupStyle.Solo) AddCaptain(_driver.CaptainBody(other), CupTuning.CaptainOffset);
            _refArrived = _ref == null || !_ref.Alive || !_sim;

            BuildCoin();
            if (_rig != null) _rig.CoinTossView(_refSpot, Vector3.forward);
            GameInput.CaptureCursor(false);   // the buttons need a pointer; the driver captures at Begin
            _director.AddGuiHook(Draw);
            _hooked = true;
        }

        void AddCaptain(CupBody body, float x)
        {
            if (body == null || !body.Alive || body.Parked) return;
            Vector3 mark = _refSpot + Vector3.right * x;
            Vector3 toRef = _refSpot - mark; toRef.y = 0f;
            var w = new Walker
            {
                Body = body,
                Target = mark,
                Facing = Quaternion.LookRotation(toRef.normalized, Vector3.up),
                Speed = CaptainJogSpeed,
                Arrived = !_sim,
            };
            _walkers.Add(w);
            if (_sim)
            {
                var rag = body.Ragdoll;
                if (body.Celeb != null && body.Celeb.Playing) body.Celeb.Cancel();
                if (body.Striker != null) body.Striker.ControlEnabled = false;
                rag.ClearPoseOverrides();
                rag.SetPose(RagdollPose.Stand, 5f);
            }
        }

        /// <summary>
        /// The coin: a CupTuning.CoinDiameter x CoinThickness gold disc with a raised boss on the
        /// HEADS face and a ring on the TAILS face, so the two faces read differently under one
        /// material. Combined into ONE mesh (Combine frees its inputs) and hidden until the flick.
        /// </summary>
        void BuildCoin()
        {
            float r = CupTuning.CoinDiameter * 0.5f;
            float h = CupTuning.CoinThickness;
            var disc = MeshGen.Cylinder(r, r, h, 40);
            MeshGen.Transform(disc, new Vector3(0f, -h * 0.5f, 0f));   // centred on its own middle
            var boss = MeshGen.Cylinder(r * 0.42f, r * 0.36f, h * 0.35f, 24);
            MeshGen.Transform(boss, new Vector3(0f, h * 0.5f, 0f));    // HEADS: a raised boss on the top face
            var ring = MeshGen.Torus(r * 0.7f, h * 0.3f, 32, 8);
            MeshGen.Transform(ring, new Vector3(0f, -h * 0.5f, 0f));   // TAILS: a ring on the bottom face
            _coinMesh = MeshGen.Combine(disc, boss, ring);
            _coinMesh.name = "CupCoin";
            _coinMat = Make.Mat(new Color(0.85f, 0.70f, 0.30f), 0.85f, 0.75f);   // the Cosmetics gold

            _coinGo = new GameObject("CupCoin");
            _coinGo.transform.SetParent(transform, false);
            _coinGo.AddComponent<MeshFilter>().sharedMesh = _coinMesh;
            _coinRenderer = _coinGo.AddComponent<MeshRenderer>();
            _coinRenderer.sharedMaterial = _coinMat;
            _coinRenderer.enabled = false;
        }

        // ==========================================================================================
        // Per frame
        // ==========================================================================================

        void Update()
        {
            if (_stage == Stage.Done || PauseMenu.Frozen) return;
            float dt = Time.deltaTime;
            _age += dt;
            _t += dt;
            switch (_stage)
            {
                case Stage.Approach: TickApproach(dt); break;
                case Stage.Flick:
                    if (_t >= FlickSeconds) Launch();
                    break;
                case Stage.Flight:
                    PlaceInFlight(_t);
                    if (_t >= CupTuning.CoinFlightSeconds) { PlaceInFlight(CupTuning.CoinFlightSeconds); _stage = Stage.Bounce; _t = 0f; }
                    break;
                case Stage.Bounce:
                    PlaceInBounce(_t);
                    if (_t >= BounceSeconds) { PlaceInBounce(BounceSeconds); _stage = Stage.Hold; _t = 0f; }
                    break;
                case Stage.Hold:
                    if (_t >= CupTuning.CoinHoldSeconds) Announce();
                    break;
                case Stage.Band:
                    if (_t >= CupTuning.CallsBandSeconds) Finish();
                    break;
            }
        }

        void LateUpdate()
        {
            if (_stage == Stage.Done || PauseMenu.Frozen || !_sim) return;
            float dt = Time.deltaTime;

            // The captains: a stride while they walk, a stand watching the referee - and the coin
            // once it is in the air - when they have arrived.
            Vector3 coinPos = _coinGo != null && _coinRenderer != null && _coinRenderer.enabled ? _coinGo.transform.position : Vector3.zero;
            bool coinUp = _stage == Stage.Flight || _stage == Stage.Bounce;
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.Body == null || !w.Body.Alive || w.Body.Parked) continue;
                var rag = w.Body.Ragdoll;
                rag.ClearPoseOverrides();
                if (!w.Arrived) CupPoses.WalkGait(rag, ref w.GaitPhase, dt, w.GaitAmount);
                else if (coinUp) CupPoses.LookAt(rag, coinPos, 60f, 60f);
            }

            // The referee's toss arm: raised over the flick, held while the coin is up, eased down
            // after; his head follows the coin. His walk gait is his own (CupReferee.MoveToward).
            if (_ref != null && _ref.Alive && _refArrived && _stage != Stage.Approach)
            {
                var rag = _ref.Body;
                float k;
                if (_stage == Stage.Flick) k = Mathf.SmoothStep(0f, 1f, _t / FlickSeconds);
                else if (_stage == Stage.Flight) k = 1f - Mathf.SmoothStep(0f, 1f, (_t - ArmHoldSeconds) / ArmDropSeconds);
                else k = 0f;
                rag.ClearPoseOverrides();
                if (k > 0f)
                {
                    // Right arm forward and a touch out (right limb: +Z is out), forearm up so the
                    // coin sits on an open hand at chest height; the flick is the last 30 deg.
                    float flick = _stage == Stage.Flick ? Mathf.Clamp01((_t - FlickSeconds * 0.6f) / (FlickSeconds * 0.4f)) : 1f;
                    rag.SetPoseOverride(Bone.UpperArmR, new Vector3(-55f * k - 25f * flick * k, 0f, 8f * k));
                    rag.SetPoseOverride(Bone.ForearmR, new Vector3(-95f * k + 30f * flick * k, 0f, 0f));
                    rag.SetPoseOverride(Bone.Torso, new Vector3(-3f * k, 0f, 0f));
                }
                if (coinUp) CupPoses.LookAt(rag, coinPos, 40f, 70f);
            }
        }

        // ==========================================================================================
        // Approach and the call
        // ==========================================================================================

        void TickApproach(float dt)
        {
            if (_sim)
            {
                // The referee walks on his own gait; the captains on the shared steer.
                if (!_refArrived && _ref != null && _ref.Alive)
                {
                    float d = _ref.MoveToward(_refSpot, CupTuning.WalkSpeed, dt);
                    if (d <= CupChoreo.ArriveRadius) { _ref.Stop(_refFacing); _refArrived = true; }
                }
                for (int i = 0; i < _walkers.Count; i++)
                {
                    var w = _walkers[i];
                    if (w.Arrived || w.Body == null || !w.Body.Alive) continue;
                    float d = CupPoses.Steer(w.Body.Ragdoll, w.Target, w.Speed, CupChoreo.TurnRate, dt);
                    w.GaitAmount = CupPoses.GaitAmount(w.Body.Ragdoll.MoveInput.magnitude);
                    if (d <= CupChoreo.ArriveRadius) { CupPoses.Stop(w.Body.Ragdoll, w.Facing); w.Arrived = true; w.GaitAmount = 0f; }
                }
                if (_age >= ApproachTimeout) SnapStragglers();
            }

            var call = ResolveOfficialCall();
            if (!call.HasValue)
            {
                bool local = _callerSlot >= 0 && _callerSlot == _director.LocalSlot;
                float limit = CupTuning.CoinCallTimeout + (local || _callerSlot < 0 ? 0f : RemoteCallGrace);
                if (_age >= limit)
                {
                    // Design 2.7 / 10: an idle official caller calls heads. Through the director
                    // when it is us, so the tally and the band see the same call everyone else does.
                    call = CoinFace.Heads;
                    if (local) { _localPick = CoinFace.Heads; _director.CallCoin(CoinFace.Heads); }
                }
            }
            if (call.HasValue && AllArrived) StartFlip(call.Value);
        }

        /// <summary>The official call as it stands: an AI's seeded call, the local pick, or the caller's echoed CupPlayer.CoinCall.</summary>
        CoinFace? ResolveOfficialCall()
        {
            if (_callerSlot < 0) return _aiCall;
            if (_callerSlot == _director.LocalSlot && _localPick.HasValue) return _localPick;
            var p = _director.PlayerAt(_callerSlot);
            if (p != null && p.CoinCall.HasValue) return p.CoinCall;
            return null;
        }

        bool AllArrived
        {
            get
            {
                if (!_refArrived) return false;
                for (int i = 0; i < _walkers.Count; i++) if (!_walkers[i].Arrived) return false;
                return true;
            }
        }

        void SnapStragglers()
        {
            var ball = _driver.Ball;
            if (!_refArrived && _ref != null && _ref.Alive) { _ref.Snap(_refSpot, _refFacing); _refArrived = true; }
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.Arrived) continue;
                if (w.Body != null && w.Body.Alive) CupBodies.Stand(w.Body, w.Target, w.Facing, ball);
                w.Arrived = true;
                w.GaitAmount = 0f;
            }
        }

        void StopWalkers()
        {
            if (!_sim) return;
            if (_ref != null && _ref.Alive) _ref.Stop(_refArrived ? _refFacing : _ref.MarkFacing);
            for (int i = 0; i < _walkers.Count; i++)
            {
                var w = _walkers[i];
                if (w.Body == null || !w.Body.Alive || w.Body.Parked) continue;
                CupPoses.Stop(w.Body.Ragdoll, w.Arrived ? w.Facing : w.Body.Ragdoll.FacingRotation);
                w.GaitAmount = 0f;
            }
        }

        // ==========================================================================================
        // The flip
        // ==========================================================================================

        /// <summary>
        /// Lock the outcome: the official call, the seeded result, and the first kicker under the
        /// rule (a correct call kicks first). Recorded on the driver at once so the wire state and
        /// the intro card carry it; the authority marks every prediction right or wrong here too.
        /// </summary>
        void StartFlip(CoinFace call)
        {
            _flipStarted = true;
            _officialCall = call;
            _firstKicker = CupRoundRules.FirstKickerFromCall(_callerSide, call, _result);
            _driver.SetCoinOutcome(call, _result);
            _driver.SetFirstKicker(_firstKicker);
            if (_director.IsAuthority && !CallsResolved()) _director.ResolveCoinCalls(_result);
            StopWalkers();
            _stage = Stage.Flick;
            _t = 0f;
        }

        /// <summary>Has someone (the flow) already run ResolveCoinCalls for this toss? True when every call carries a verdict.</summary>
        bool CallsResolved()
        {
            var players = _director.Players;
            bool any = false;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!p.CoinCall.HasValue) continue;
                any = true;
                if (!p.CoinCallRight.HasValue) return false;
            }
            return any;
        }

        /// <summary>The coin leaves the hand: the arc from the hand point to the landing spot, the spin count that lands on the result.</summary>
        void Launch()
        {
            Vector3 fwd = _refFacing * Vector3.forward;
            _p1 = _refSpot + fwd * CupTuning.CoinLandDistance + Vector3.up * (CupTuning.CoinThickness * 0.5f);
            _p0 = HandPoint() ?? (_refSpot + fwd * 0.35f + Vector3.up * 1.05f);
            float T = CupTuning.CoinFlightSeconds;
            float g = ArcGravity;
            // A true ballistic arc between the two heights in exactly T seconds: from a hand at
            // ~1.7 m this apexes near 3.3 m, inside the wide shot's frame (see ArcGravity).
            _v0 = (_p1.y - _p0.y + 0.5f * g * T * T) / T;
            // Spin about the camera's horizontal (across the flight), so the faces flash at it. The
            // revolution count is the nearest whole number to CoinSpinRps * T plus a half turn for
            // TAILS, so a constant tumble ends exactly face-up on the result.
            _spinAxis = Vector3.Cross(Vector3.up, fwd).normalized;
            int whole = Mathf.Max(1, Mathf.RoundToInt(CupTuning.CoinSpinRps * T));
            _revs = whole + (_result == CoinFace.Tails ? 0.5f : 0f);
            // At rest the boss (HEADS) faces up: identity; TAILS is the half turn.
            _restRot = Quaternion.AngleAxis(_result == CoinFace.Tails ? 180f : 0f, _spinAxis);
            if (_coinRenderer != null) _coinRenderer.enabled = true;
            PlaceInFlight(0f);
            _stage = Stage.Flight;
            _t = 0f;
        }

        /// <summary>The referee's right hand: HandDrop down the forearm from its pivot (the AddGlove hand point).</summary>
        Vector3? HandPoint()
        {
            if (_ref == null || !_ref.Alive) return null;
            var fa = _ref.Body.Phys(Bone.ForearmR);
            if (fa == null) return null;
            return fa.position + fa.rotation * Vector3.down * HandDrop;
        }

        void PlaceInFlight(float t)
        {
            if (_coinGo == null) return;
            float T = CupTuning.CoinFlightSeconds;
            float g = ArcGravity;
            float u = Mathf.Clamp01(t / T);
            Vector3 p = Vector3.Lerp(_p0, _p1, u);
            p.y = _p0.y + _v0 * t - 0.5f * g * t * t;
            if (u >= 1f) p = _p1;
            _coinGo.transform.position = p;
            _coinGo.transform.rotation = Quaternion.AngleAxis(360f * _revs * u, _spinAxis);
        }

        void PlaceInBounce(float t)
        {
            if (_coinGo == null) return;
            float u = Mathf.Clamp01(t / BounceSeconds);
            Vector3 fwd = _refFacing * Vector3.forward;
            Vector3 p = _p1 + fwd * (BounceDrift * u);
            p.y = _p1.y + BounceHeight * 4f * u * (1f - u);
            _coinGo.transform.position = p;
            // A decaying wobble about the flight axis that dies out exactly flat on the result.
            float wobble = 22f * (1f - u) * Mathf.Sin(u * Mathf.PI * 2f);
            _coinGo.transform.rotation = Quaternion.AngleAxis(wobble, fwd) * _restRot;
        }

        // ==========================================================================================
        // The flash, the band, the end
        // ==========================================================================================

        void Announce()
        {
            string nation = "";
            int n = _driver.NationOf(_firstKicker);
            if (n >= 0 && CupNations.IsValid(n)) nation = CupNations.Name(n);
            _driver.Announce(CupText.CoinName(_result) + "\n" + CupText.KickFirst(nation));
            _t = 0f;
            if (_driver.Setup.Style == CupStyle.Coop) _stage = Stage.Band;
            else Finish();
        }

        void Finish()
        {
            if (_stage == Stage.Done) return;
            _stage = Stage.Done;
            StopWalkers();
            if (_coinRenderer != null) _coinRenderer.enabled = false;   // pocketed under the cut
            Unhook();
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke();
            Destroy(gameObject);
        }

        void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            if (_director != null) _director.RemoveGuiHook(Draw);
        }

        void OnDestroy()
        {
            Unhook();
            if (_coinMesh != null) { Destroy(_coinMesh); _coinMesh = null; }
            if (_coinMat != null) { Destroy(_coinMat); _coinMat = null; }
        }

        // ==========================================================================================
        // Overlay (design 6.11) and the calls band (6.12)
        // ==========================================================================================

        /// <summary>The local human may call: they have a slot and are still in the cup (a body or a spectator both count).</summary>
        bool LocalCanCall
        {
            get
            {
                if (_director == null || _director.LocalSlot < 0) return false;
                var me = _director.LocalPlayer;
                return me != null && me.Active;
            }
        }

        bool LocalIsOfficial => _callerSlot >= 0 && _callerSlot == _director.LocalSlot;

        void Draw()
        {
            // Under the pause menu the overlay hides: both draw at IMGUI depth 0 in no fixed
            // order, and a click blocker beneath the menu's buttons would eat their clicks. The
            // condition changes only in Update, so Layout and Repaint always agree.
            if (_stage == Stage.Done || PauseMenu.Paused) return;
            MenuScale.Begin();
            try { DrawInner(); }
            finally { MenuScale.End(); }
        }

        void DrawInner()
        {
            Styles();
            float w = MenuScale.Width, h = MenuScale.Height;
            bool calling = CallingOpen;
            bool showButtons = calling && LocalCanCall;

            // The two buttons live at the same place every pass; when they are not for showing
            // they are parked off-screen and disabled (never skipped - control ids).
            float cx = w * 0.5f, by = h * 0.70f - ButtonH * 0.5f;
            var heads = showButtons ? new Rect(cx - ButtonW - ButtonGap * 0.5f, by, ButtonW, ButtonH) : new Rect(-1000f, -1000f, ButtonW, ButtonH);
            var tails = showButtons ? new Rect(cx + ButtonGap * 0.5f, by, ButtonW, ButtonH) : new Rect(-1000f, -1000f, ButtonW, ButtonH);
            UITheme.ClickBlocker(w, h, heads, tails);

            if (calling)
            {
                UITheme.Shadowed(new Rect(0f, 104f, w, 46f), CupText.CoinTossHeader, _header, UITheme.Gold, 0.7f, 2f);
                UITheme.Label(new Rect(0f, 150f, w, 22f), CallerLine(), _hint);
            }

            if (showButtons && LocalIsOfficial)
                UITheme.Shadowed(new Rect(0f, by - 30f, w, 22f), CupText.DecidesKickOff, _tag, UITheme.Gold, 0.6f, 1f);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = showButtons;
            DrawCallButton(heads, CoinFace.Heads, showButtons);
            DrawCallButton(tails, CoinFace.Tails, showButtons);
            GUI.enabled = prevEnabled;

            if (showButtons) UITheme.Label(new Rect(0f, by + ButtonH + 12f, w, 20f), PickLine(), _hint);

            if (_stage == Stage.Band) DrawBand(w, h);
        }

        void DrawCallButton(Rect r, CoinFace face, bool shown)
        {
            bool picked = shown && _localPick.HasValue && _localPick.Value == face;
            if (picked)
            {
                UITheme.Glow(new Rect(r.x - 18f, r.y - 14f, r.width + 36f, r.height + 28f), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.22f));
            }
            if (UITheme.Button(r, CupText.CoinName(face), _button)) Call(face);
            if (picked) UITheme.FrameOutline(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), UITheme.Gold);
        }

        /// <summary>Who decides, and the clock on them: the line under the header.</summary>
        string CallerLine()
        {
            float limit = CupTuning.CoinCallTimeout;
            int left = Mathf.Max(0, Mathf.CeilToInt(limit - _age));
            if (_callerSlot < 0) return "The AI captain calls";
            if (LocalIsOfficial)
            {
                if (_localPick.HasValue) return "Your call: " + CupText.CoinName(_localPick.Value) + "  -  change it until the flip";
                return "Your call decides kick-off  -  HEADS in " + left + " s if you do not";
            }
            var p = _director.PlayerAt(_callerSlot);
            string name = p != null ? p.DisplayName : "Player " + _callerSlot;
            if (p != null && p.CoinCall.HasValue) return CupText.Decides(name) + "  -  " + CupText.CoinName(p.CoinCall.Value);
            return CupText.Decides(name) + "  -  waiting for the call";
        }

        string PickLine()
        {
            if (LocalIsOfficial) return "";
            if (_localPick.HasValue) return "Your prediction: " + CupText.CoinName(_localPick.Value);
            return "Predict the flip";
        }

        /// <summary>Co-op (design 6.12): a band on the right for CallsBandSeconds - every team member who called, their call, a check or a cross.</summary>
        void DrawBand(float w, float h)
        {
            var rows = new List<CupPlayer>();
            var players = _director.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p != null && p.Active && p.CoinCall.HasValue) rows.Add(p);
            }
            float inT = Mathf.Clamp01(_t / 0.25f);
            float outT = Mathf.Clamp01((_t - (CupTuning.CallsBandSeconds - 0.3f)) / 0.3f);
            float alpha = Mathf.Min(inT, 1f - outT);
            if (alpha <= 0f) return;

            float bandH = 52f + Mathf.Max(1, rows.Count) * BandRowH + 12f;
            var r = new Rect(w - 24f - BandW + (1f - inT) * 40f, h * 0.5f - bandH * 0.5f, BandW, bandH);
            UITheme.Panel(r, UITheme.Gold, true, alpha);
            UITheme.Shadowed(new Rect(r.x, r.y + 12f, r.width, 28f), CupText.Calls, _bandTitle, WithAlpha(UITheme.Gold, alpha), 0.6f, 1f);
            UITheme.Fill(new Rect(r.x + 16f, r.y + 46f, r.width - 32f, 1f), new Color(1f, 1f, 1f, 0.09f * alpha));

            float y = r.y + 52f;
            if (rows.Count == 0)
            {
                UITheme.Label(new Rect(r.x + 16f, y, r.width - 32f, BandRowH), "no calls", _bandCall);
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var p = rows[i];
                bool right = p.CoinCall.Value == _result;   // computed here, not from the echo: same on host and client
                var keep = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                UITheme.Label(new Rect(r.x + 16f, y, 108f, BandRowH), p.DisplayName, _bandName);
                UITheme.Label(new Rect(r.x + 124f, y, 60f, BandRowH), CupText.CoinName(p.CoinCall.Value), _bandCall);
                GUI.color = keep;
                Mark(new Vector2(r.xMax - 26f, y + BandRowH * 0.5f), right, alpha);
                y += BandRowH;
            }
        }

        /// <summary>A green check or a red cross, drawn as rotated strokes (no font glyph to depend on).</summary>
        static void Mark(Vector2 c, bool right, float alpha)
        {
            if (right)
            {
                var col = WithAlpha(UITheme.Green, alpha);
                Stroke(new Vector2(c.x - 4f, c.y + 2f), 8f, 3f, 45f, col);
                Stroke(new Vector2(c.x + 3f, c.y - 1f), 15f, 3f, -45f, col);
            }
            else
            {
                var col = WithAlpha(UITheme.Red, alpha);
                Stroke(c, 13f, 3f, 45f, col);
                Stroke(c, 13f, 3f, -45f, col);
            }
        }

        /// <summary>A filled bar of `length` x `thickness` centred on `c`, rotated by `angle` about its centre, correct under MenuScale's matrix.</summary>
        static void Stroke(Vector2 c, float length, float thickness, float angle, Color col)
        {
            var m = GUI.matrix;
            Vector3 pivot = m.MultiplyPoint3x4(new Vector3(c.x, c.y, 0f));   // the centre in SCREEN space
            GUI.matrix = Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, angle), Vector3.one)
                       * Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one) * m;
            UITheme.Fill(new Rect(c.x - length * 0.5f, c.y - thickness * 0.5f, length, thickness), col);
            GUI.matrix = m;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static void Styles()
        {
            if (_header != null) return;
            _header = new GUIStyle { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            _tag = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _bandTitle = new GUIStyle { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _bandName = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            _bandCall = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            UIFont.Heavy(_header);
            UIFont.Heavy(_tag);
            UIFont.Heavy(_button);
            UIFont.Heavy(_bandTitle);
        }
    }
}
