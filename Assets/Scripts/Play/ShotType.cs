namespace Trickshot
{
    /// <summary>How the last striker contact was made, used to pick a goal callout
    /// (BICYCLE KICK / HEADER / DIVING HEADER / plain). Set on the ball at contact.</summary>
    public enum ShotType
    {
        Normal,
        Header,
        DivingHeader,
        Bicycle,
    }
}
