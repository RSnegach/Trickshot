namespace Trickshot
{
    /// <summary>
    /// Tiny static bridge so any game-mode driver can make the crowd celebrate a goal
    /// without holding a direct reference. GameBootstrap registers the built Crowd here;
    /// drivers call CrowdCheer.Celebrate() on a goal. No-ops if no crowd is registered
    /// (e.g. a mode built without a stadium).
    /// </summary>
    public static class CrowdCheer
    {
        static Crowd _crowd;

        public static void Register(Crowd crowd) => _crowd = crowd;

        public static void Celebrate()
        {
            if (_crowd != null) _crowd.Celebrate();
        }
    }
}
