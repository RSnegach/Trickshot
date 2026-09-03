using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// FREE KICK / SET PIECES: the player's body curls a dead ball up over a defensive wall, which
    /// hops as the ball leaves. The taker, the ball and the wall are the only things in frame.
    ///
    /// DefensiveWall's blockers are kinematic mannequins, not ragdolls, so the wall costs nothing
    /// while frozen and its hop is a scripted lift rather than a jump. Its facing is taken from the
    /// direction ball -> SimConfig.GoalCenter, which from this stage (x = 0, well down -Z) is +Z:
    /// exactly the way the ball is struck, so the wall faces the shooter with no special casing.
    /// </summary>
    public class FreeKickScene : DeadBallScene
    {
        // Further out than the old 3.6: at that range the ball was still climbing off the boot as
        // it reached the wall and clipped the blockers instead of clearing them. More distance
        // gives the arc room to get over the top, which is the whole shape of the shot.
        const float WallDist = 6.2f;
        const float ClearZ = 11f;         // where the ball is aimed, past the wall
        const int WallCount = 4;

        DefensiveWall _wall;
        bool _hopped;

        protected override float HoldSeconds => 3.6f;
        protected override bool Lofted => true;   // it has to clear the wall

        // High and to the far side: an over-the-wall curler, not a drilled shot.
        // High enough to clear a 1.85 m wall standing 6.2 m out, and wide enough to bend around
        // its edge rather than through the middle of it.
        protected override Vector3 Aim => Origin + new Vector3(2.2f, 2.6f, ClearZ);

        public override void Build()
        {
            BuildTaker(Vector3.zero);

            _wall = new DefensiveWall();
            // Explicit centre, NOT the ball-relative overload: both overloads take the ball->goal
            // direction from the readonly SimConfig.GoalCenter (0,0,17), and from a sub-stage
            // thousands of metres up +Z that points back down the pitch - which put the wall
            // BEHIND the taker. The lateral axis is +X here, so the wall fans across it.
            _wall.BuildFacing(Root, BallHome + new Vector3(0.35f, 0f, WallDist), Vector3.forward, WallCount);

            BuildCatcher(new Vector3(30f, 8f, 0.2f), Origin + new Vector3(0f, 4f, ClearZ + 3f));
        }

        public override void Reset()
        {
            base.Reset();
            _wall.Ground();   // snap the wall down and cancel any hop in flight
            _hopped = false;
        }

        protected override void TickAfterStrike(float dt)
        {
            // Hop as the ball actually leaves, the way the free-kick driver does it (the strike
            // edge is the ball moving, not a taker event).
            if (!_hopped && Ball.Speed > 2.5f) { _wall.TriggerJump(); _hopped = true; }
            _wall.Tick();
        }

        public override void Frame(out Vector3 camPos, out Vector3 lookAt, out float fov)
        {
            // Behind and to the side of the taker, low, so the ball visibly rises over the wall.
            // Behind and beside the taker, close, so he and the wall both fit the panel.
            // Side on and fitted to taker + wall + the arc over it: the point of this panel is the
            // ball rising OVER something, which a camera behind the taker flattens away.
            fov = 46f;
            // Pulled back a little so the taker, the wall and the air the ball travels through
            // are all in shot - the arc over the top is the thing worth seeing.
            float zA = -RunUpDist - 0.6f, zB = WallDist + 1.6f;
            FitCamera(Origin + new Vector3(0.25f, 1.35f, Mathf.Lerp(zA, zB, 0.5f)),
                      new Vector3((zB - zA) * 0.5f, 1.75f, 0.4f),
                      new Vector3(1f, 0.22f, -0.10f), fov, PanelAspect, out camPos, out lookAt);
        }

        public override void Destroy()
        {
            _wall?.Clear();
            base.Destroy();
        }
    }
}
