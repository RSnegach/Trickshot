namespace Trickshot
{
    /// <summary>
    /// Common surface for whichever character the player drives (Striker or
    /// KeeperController), so GameManager can tick/reset it without knowing which mode
    /// is active.
    /// </summary>
    public interface IPlayerController
    {
        void Tick();
        void ForceRecover();
    }
}
