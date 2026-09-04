using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// TRICKSHOT CUP: a penalty. The player's own body runs up and drives the ball low into the
    /// corner past a keeper who throws himself the right way and is beaten anyway; the ball hits
    /// the net and the taker turns to the camera and fist-pumps. A small gold trophy on a plinth
    /// sits in the foreground as the mode's signature.
    ///
    /// The taker, the keeper, the ball and the trophy are the only things in frame - no goal, no
    /// pitch - so the "net" is the invisible catcher every scene uses, and the corner is where the
    /// ball comes to rest just past the keeper's outstretched gloves.
    ///
    /// THE KEEPER IS THE REAL BRAIN, NOT A POSE. Nothing here animates him: he is a Goalkeeper AI
    /// presented with a ball he must reach, and he decides to dive on his own. That means the
    /// shot has to be SOLVED against his decision tree rather than guessed, because two of its
    /// gates are numeric and unforgiving:
    ///   - He dives only when the predicted crossing offset beats his DEAD BAND, the distance he
    ///     could sidestep in the flight time left: KeeperStrafeSpeed * lerp(0.45, 2.0, ability)
    ///     * 0.55 * (tRem - 0.08), floored at 0.65 m. At the stage's ability 0.6 that is about
    ///     4.2 m/s of reach. He commits after a 0.16 s reaction (sqrt-shaped, ability 0.6), so a
    ///     0.44 s flight leaves him ~0.27 s and a band of ~0.8 m - and the corner is 2.9 m away.
    ///     A slow ball would let him step across and catch it instead.
    ///   - A LOW ball (predicted under 1.0 m) that is past his splay reach (1.6 * 1.06 = 1.7 m)
    ///     but inside his low-dive reach (4.5 * 1.06 = 4.8 m) gets the flat, full-layout LOW dive,
    ///     which is the picture a penalty wants. He also refuses to commit at all if the crossing
    ///     point is outside halfGoal + 1.2 m, and halfGoal reads the MUTABLE SimConfig.GoalWidth
    ///     that a practice goal editor leaves behind: at the editor's 0.6x minimum that gate is
    ///     3.0 m, so the corner sits at 2.9 m rather than the wider 3.3 a regulation goal allows.
    ///   - He is BEATEN because the dive's reach is bounded: the impulse is capped at 3.79 m/s
    ///     (KeeperDiveHorizBase * 1.06), the turf bleeds it, and the Dive pose puts the gloves in
    ///     a bar at the shoulder line rather than along the dive. In the ~0.27 s left the body
    ///     covers about 0.85 m and the gloves reach roughly 1.6 m from where he stood; the ball's
    ///     near edge crosses at 2.7 m. He gets nowhere near it even if he reacted instantly.
    /// </summary>
    public class CupScene : DeadBallScene
    {
        // The spot. 9 m rather than the regulation 11: the same compression FreeKickScene applies
        // to its wall. The keeper's decisions are made in flight TIME, not metres, so the numbers
        // above hold at either range; what 9 m buys is a keeper large enough to read in a panel.
        const float PenaltyDist = 9.0f;
        // On his line, the way SimConfig.KeeperPenaltyStart stands him (0.08 m off it).
        const float LineStandoff = 0.08f;
        // The corner: see the gate arithmetic in the class summary for why 2.9 and not wider. Low
        // (0.5 m at the line) so the LOW band is chosen with margin under its 1.0 m threshold; at
        // this project's 2x gravity the flight still peaks at ~0.85 m mid-way, which reads as a
        // driven shot rather than a daisy-cutter.
        const float CornerX = 2.9f;
        const float CornerY = 0.5f;
        // Spot to line in 0.44 s: about 22 m/s off the boot. Fast enough to beat the dead band by
        // a wide margin (see above), slow enough that a 9 m flight is watchable at the menu's
        // slow-mo. The Crosser's own driven-serve default (0.95 * 0.8 = 0.76 s) would give him
        // 0.6 s to react and a 2 m band, so the ball is RE-LAUNCHED with this time on the frame
        // the crosser fires (TickAfterStrike).
        const float FlightTime = 0.44f;
        // How deep the invisible net sits behind the line: the ball is caught just past it so it
        // comes to rest beside the beaten keeper rather than a metre behind him, where an
        // elevated camera would stack the two.
        const float NetDepth = 0.7f;
        // Struck to the -X corner: with the camera on the -X side that corner lands at the far
        // left of the frame and the keeper dives OUT toward it, while the +X corner would end up
        // near the frame's centre on top of his gloves and read as a save.
        const float CornerSide = -1f;

        // Where the camera looks FROM (see Frame). Also the way the scorer turns to celebrate, so
        // the fist pump is seen from the front rather than over his shoulder.
        static readonly Vector3 CamDir = new Vector3(-0.52f, 0.30f, -0.80f);

        // The trophy's plinth, in the foreground on the camera's side, clear of the run-up (2.6 m
        // behind the spot, off to -X) and far from the keeper's dive at the other end.
        static readonly Vector3 TrophyAt = new Vector3(-4.0f, 0f, 1.6f);
        const float PlinthW = 0.5f, PlinthH = 0.5f;

        // Keeper kit: the yellow the design asked for, with dark limbs so the gloves read.
        static readonly Color KeeperTorso = new Color(0.95f, 0.80f, 0.18f);
        static readonly Color KeeperLimb = new Color(0.18f, 0.18f, 0.20f);
        // The Cosmetics gold (Cosmetics.Accessories' private Gold()) so the cup matches the
        // player's own gold accessories, and a dark stone for the plinth.
        static readonly Color Gold = new Color(0.85f, 0.70f, 0.30f);
        static readonly Color Stone = new Color(0.28f, 0.29f, 0.32f);

        ActiveRagdoll _keeperRag;
        Goalkeeper _keeper;
        Celebration _celeb;
        Vector3 _line, _aim, _keeperHome;
        Quaternion _keeperFacing;
        bool _relaunched, _netted;

        Mesh _trophyMesh;

        // Run-up, plant, the 0.45 s windup, the 0.44 s flight (about 1.5 s to the net), then the
        // 1.6 s fist pump and a beat of him holding it.
        protected override float HoldSeconds => 3.5f;

        // Driven, not lofted: the Crosser's swing is the same either way and the ball's real arc
        // comes from the re-launch, but the flag documents the shot.
        protected override bool Lofted => false;

        protected override Vector3 Aim => _aim;

        public override void Build()
        {
            BuildTaker(Vector3.zero);

            _line = Spot + new Vector3(0f, 0f, PenaltyDist);
            _aim = _line + new Vector3(CornerSide * CornerX, CornerY, 0f);

            // He stands just in FRONT of his line (on the -Z side, the way he faces) and the shot
            // comes at him from the spot that way.
            _keeperHome = _line - new Vector3(0f, 0f, LineStandoff);
            _keeperFacing = Quaternion.LookRotation(Vector3.back, Vector3.up);
            _keeperRag = BuildAiBody("MsKeeper", _keeperHome, _keeperFacing, KeeperTorso, KeeperLimb, gloves: true);
            _keeper = _keeperRag.gameObject.AddComponent<Goalkeeper>();
            // The 4-arg Init is the one that takes a goal centre: the 2-arg overload is welded to
            // the real pitch's SimConfig.GoalCenter, thousands of metres from this stage. outSign
            // is -1 because he must face -Z, INTO the shot - the brain reads a ball as incoming
            // only when it travels against that direction, and with the sign wrong he never
            // reacts at all (KeeperScene learned this the hard way).
            _keeper.Init(_keeperRag, Ball, _line, -1f);
            // The penalty rule: he neither rushes nor guards off his line until the ball is
            // struck (the cup's round driver sets the same flag). Without it GuardSpot would
            // walk him 1.3 m off his line during the run-up.
            _keeper.HoldLine = true;
            _keeper.ResetTo(_keeperHome);

            // The fist pump is the real emote on the real body, played at the net.
            _celeb = Rag.gameObject.AddComponent<Celebration>();
            _celeb.Init(Rag);

            // The net: catch the ball just past the line so it dies beside the keeper. Same
            // catcher every scene uses (zero bounce, NetBackstop deadens the contact).
            BuildCatcher(new Vector3(30f, 8f, 0.25f), _line + new Vector3(0f, 4f, NetDepth));

            BuildTrophy(Origin + TrophyAt);
        }

        /// <summary>
        /// The trophy: a lathe cup with two torus handles, ONE mesh and ONE gold material, on a
        /// stone plinth. Built to size in metres (about 0.42 m tall, the podium's TrophyHeight) so
        /// it reads as a trophy beside a 1.8 m body and not as a goblet.
        /// </summary>
        void BuildTrophy(Vector3 at)
        {
            var stone = M(Stone, 0.35f, 0.05f);
            Make.Box("MsPlinth", new Vector3(PlinthW, PlinthH, PlinthW),
                     at + new Vector3(0f, PlinthH * 0.5f, 0f), stone, Root, collider: false);

            // Profile is (radius, y) bottom to top with the solid on the left: the foot, a stem
            // with a knop, the bowl's outside up to the rim, then back DOWN the inside to a pole
            // on the bowl's floor. Traversed that way the lathe's (dy, -dr) normal points out on
            // the outside and toward the axis on the inside, so the bowl is hollow and lit right
            // without a second surface.
            var profile = new[]
            {
                new Vector2(0f,     0f),
                new Vector2(0.085f, 0f),
                new Vector2(0.085f, 0.018f),
                new Vector2(0.050f, 0.030f),
                new Vector2(0.024f, 0.060f),
                new Vector2(0.024f, 0.110f),
                new Vector2(0.042f, 0.135f),
                new Vector2(0.024f, 0.160f),
                new Vector2(0.024f, 0.185f),
                new Vector2(0.055f, 0.200f),
                new Vector2(0.075f, 0.240f),
                new Vector2(0.100f, 0.300f),
                new Vector2(0.115f, 0.370f),
                new Vector2(0.118f, 0.400f),
                new Vector2(0.104f, 0.400f),
                new Vector2(0.090f, 0.340f),
                new Vector2(0.060f, 0.260f),
                new Vector2(0f,     0.235f),
            };
            var body = MeshGen.Lathe(profile, 36);

            // Handles: a 250-degree torus arc stood up in the XY plane with its opening toward the
            // bowl, centred a little outside the rim so both ends bury themselves in the wall.
            // Torus arcs start at local +Z and sweep toward +X about +Y; rotating 90 deg about X
            // stands the ring up ((x, y, z) -> (x, -z, y)), which leaves the 110-degree gap centred
            // 215 deg round from +X, so a further -35 deg about Z turns the gap to face -X exactly.
            const float handleR = 0.05f, handleTube = 0.011f, handleArc = 250f;
            var stand = Quaternion.AngleAxis(-35f, Vector3.forward) * Quaternion.Euler(90f, 0f, 0f);
            var right = MeshGen.Torus(handleR, handleTube, 24, 8, handleArc);
            MeshGen.Transform(right, new Vector3(0.125f, 0.325f, 0f), stand);
            var left = MeshGen.Torus(handleR, handleTube, 24, 8, handleArc);
            MeshGen.Transform(left, new Vector3(-0.125f, 0.325f, 0f),
                              Quaternion.AngleAxis(180f, Vector3.up) * stand);

            _trophyMesh = MeshGen.Combine(body, right, left);   // destroys the three parts
            _trophyMesh.name = "MsTrophy";

            var go = new GameObject("MsTrophy");
            go.transform.SetParent(Root, true);
            go.transform.position = at + new Vector3(0f, PlinthH, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = _trophyMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = M(Gold, 0.85f, 0.75f);
        }

        public override void Reset()
        {
            // Cancel BEFORE the base resets the body: End() restores the controller flags and
            // kills any body spin, which must land on the live body it changed, not on the one
            // ResetTo is about to re-place. Playing is only ever true on a live body here, so
            // this never writes to kinematic bones.
            if (_celeb != null && _celeb.Playing) _celeb.Cancel();
            // The swing pose was switched off for the celebration; the next run needs it.
            Kicker.Cosmetic = true;
            base.Reset();
            _keeper.HoldLine = true;
            _keeper.ResetTo(_keeperHome);
            _relaunched = false;
            _netted = false;
        }

        public override void Freeze()
        {
            base.Freeze();
            // The emote runs from its own Update and would keep posing a frozen body.
            if (_celeb != null) _celeb.enabled = false;
        }

        public override void Thaw()
        {
            base.Thaw();
            if (_celeb != null) _celeb.enabled = true;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);   // run-up, plant, swing, strike; TickAfterStrike below once the ball is away
            // The brain has no Update of its own; it only reacts while it is ticked.
            _keeper.Tick();
        }

        protected override void TickAfterStrike(float dt)
        {
            // THE FRAME THE CROSSER FIRES. Its Launch has just placed the ball on the spot and
            // given it the driven-serve default arc; replace that with the penalty's own flight
            // before the physics step sees either. Same target, our time - see FlightTime.
            if (!_relaunched && Kicker.JustServed)
            {
                _relaunched = true;
                _keeper.HoldLine = false;   // the ball is struck: the rule that held him releases
                Ball.LaunchTo(_aim, FlightTime, Vector3.zero, 0f);
            }

            // THE NET: the ball is over the line. He wheels toward the camera and pumps.
            if (_relaunched && !_netted && Ball.Rb.position.z >= _line.z)
            {
                _netted = true;
                // The Crosser's follow-through clears every pose override at the top of its own
                // pose pass, which runs AFTER Celebration's Update each frame and would wipe the
                // emote before physics ever saw it. Cosmetic off skips that pass entirely; the
                // strike is long over, so nothing is lost.
                Kicker.Cosmetic = false;
                // That pass is also where the upright lock re-engages after the contact hop. It
                // has had 0.44 s (against a 0.30 s grace), but assert the standing flags anyway:
                // Play() snapshots them and End() puts them back, and a snapshot of an unlocked
                // body would drop him limp when the pump finished.
                Rag.UprightLock = true;
                Rag.BalanceEnabled = true;
                Rag.MoveInput = Vector3.zero;
                // Turn to the crowd: the pelvis servo follows FacingRotation through the emote,
                // so he swings round toward the lens while the arm goes up.
                Vector3 toCam = new Vector3(CamDir.x, 0f, CamDir.z).normalized;
                Rag.FacingRotation = Quaternion.LookRotation(toCam, Vector3.up);
                _celeb.Play(Celebration.Emote.FistPump);
            }
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Elevated three-quarter from behind the taker's left shoulder: the classic penalty
            // shot. The taker is large in the near right, the keeper on his line in the far left,
            // the ball's path runs away between them into the far-left corner, and the trophy
            // sits low in the near left. Fitted to the box the whole beat happens in - the run-up
            // mark at one end, the keeper's dive at the other - so the framing survives any of
            // those numbers moving. The centre is biased toward the taker (the near, magnified
            // end is what sets the fit) and sits low so the run-up mark's feet clear the bottom
            // edge at this tilt.
            fov = 44f;
            FitCamera(Origin + new Vector3(0.30f, 0.55f, 3.75f), new Vector3(3.1f, 1.5f, 6.0f),
                      CamDir, fov, PanelAspect, out camPos, out lookAt);
        }

        public override void Destroy()
        {
            // The trophy mesh is generated, so nothing frees it with its GameObject; the gold and
            // stone materials went through M() and go with the base.
            if (_trophyMesh != null) { Object.Destroy(_trophyMesh); _trophyMesh = null; }
            base.Destroy();
        }
    }
}
