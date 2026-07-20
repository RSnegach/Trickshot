using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Pumps the network transport every frame for the lifetime of a networked match. Added
    /// to the match root by GameBootstrap when a session is active, so inputs, snapshots and
    /// match events keep flowing while the mode driver runs. Torn down with the match.
    /// </summary>
    public class NetPump : MonoBehaviour
    {
        void Update() => Multiplayer.Poll();
    }
}
