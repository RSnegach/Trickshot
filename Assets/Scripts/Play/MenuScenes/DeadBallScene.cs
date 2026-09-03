using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Shared shape for the two dead-ball vignettes: the player's body takes a short run-up and
    /// strikes a ball off the spot. Only the figure, the ball and (for the free kick) the wall are
    /// in frame.
    ///
    /// The strike goes through Crosser: it plays the real KickSwing windup on the body and launches
    /// the ball by code at contact, which is how every AI kick in this project is taken. The leg
    /// never physically touches the ball (IgnoreBody), so the arc is exactly the one asked for.
    /// The jog in is hand-driven the way the title reel does it, and deliberately does NOT tick the
    /// Crosser - Crosser.Tick clears pose overrides every frame and would wipe the stride.
    /// </summary>
    public abstract class DeadBallScene : MenuScene
    {
        protected const float RunUpDist = 2.6f;
        protected const float RunSpeed = 4.6f;
        protected const float PlantStop = 0.45f;
        protected const float RunTimeout = 2.0f;

        protected ActiveRagdoll Rag;
        protected Crosser Kicker;
        protected Vector3 Spot, RunStart, BallHome;
        protected Quaternion Facing;

        float _gait;
        bool _struck;
        protected bool Struck => _struck;

        /// <summary>Where the strike is aimed. Read once per run, so a scene may vary it.</summary>
        protected abstract Vector3 Aim { get; }

        /// <summary>How long the whole vignette runs.</summary>
        protected virtual float HoldSeconds => 3.6f;

        /// <summary>Lofted (over a wall) or driven (flat at a target).</summary>
        protected virtual bool Lofted => false;

        protected void BuildTaker(Vector3 spotLocal)
        {
            Spot = Origin + spotLocal;
            BallHome = Spot + new Vector3(0f, SimConfig.BallRadius, 0f);
            Facing = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            // Behind the ball and slightly to the side, the way a taker addresses a dead ball.
            RunStart = Spot + new Vector3(-0.9f, 0f, -RunUpDist);

            BuildFloor(60f, 70f, Spot + new Vector3(0f, 0f, 12f));
            BuildBall(BallHome);

            Rag = BuildPlayerBody("MsTaker", RunStart, Facing, gloves: false);
            // The launch point is the ball's resting spot at ground level, so the ball leaves the
            // turf flat instead of teleporting up to hip height (which SetOrigin would force).
            var launch = Make.Empty("MsKickPoint", BallHome, Rag.gameObject.transform).transform;
            Kicker = Rag.gameObject.AddComponent<Crosser>();
            Kicker.Init(null, Ball, launch, Rag);
            Kicker.AutoServe = false;
            Ball.IgnoreBody(Rag, true);
        }

        public override void Reset()
        {
            Kicker.Idle();
            Rag.ResetTo(RunStart, Facing);
            Ball.ResetTo(BallHome);
            Ball.IgnoreBody(Rag, true);
            _gait = 0f;
            _struck = false;
            Clock = 0f;
            Done = false;
        }

        public override void Tick(float dt)
        {
            Clock += dt;

            if (!_struck)
            {
                // Jog to the ball, then plant and swing. The upright lock is re-asserted every
                // frame of the jog so a body that ended the last run mid-air cannot start this one
                // lying down.
                float dist = Jog(Rag, Spot, RunSpeed, ref _gait, dt);
                if (dist <= PlantStop || Clock >= RunTimeout)
                {
                    StopJog(Rag, Facing, ref _gait);
                    Ball.ResetTo(BallHome);
                    Kicker.Arm(0f);
                    Kicker.ServeNow(Aim, Lofted, powerMul: 0f);
                    _struck = true;
                }
            }
            else
            {
                Kicker.Tick();   // plays the swing and fires at contact
                TickAfterStrike(dt);
            }

            if (Clock >= HoldSeconds) Done = true;
        }

        /// <summary>Anything the scene animates once the ball is away (a wall hop, a target).</summary>
        protected virtual void TickAfterStrike(float dt) { }
    }
}
