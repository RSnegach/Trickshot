namespace Trickshot
{
    /// <summary>How the last striker contact was made, used to pick a goal callout
    /// (BICYCLE KICK / HEADER / DIVING HEADER / VOLLEY / THIRD LEG / plain). Set on the ball at contact.</summary>
    public enum ShotType
    {
        Normal,
        Header,
        DivingHeader,
        Bicycle,
        Volley,
        // Adult mode: struck with the erect appendage's hitbox (AnatomySim). Routed through the
        // header's redirect in BallController so it is a real, goal-ward strike and not a body trap.
        ThirdLeg,
    }
}
