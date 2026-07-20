using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Host-authoritative session layer sitting on top of an INetTransport. Owns the player
    /// SLOT table and routes messages. It does NOT run the sim itself; the game's mode
    /// driver (ScrimmageGame / striker GameManager) queries this for the local input frame,
    /// the per-slot inputs (host side), and publishes snapshots (host) / consumes them
    /// (client). This keeps the netcode decoupled from the physics loop.
    ///
    /// Slot model (per the design): slot 0 is the KEEPER, slots 1..N are SHOOTERS. Joining
    /// players fill the lowest free shooter slot; AI fills any slot no human holds. Works
    /// for both scrimmage and striker mode (striker = 1 keeper + however many shooters).
    /// </summary>
    public class NetSession
    {
        public const int MaxSlots = 8;   // 1 keeper + up to 7 shooters

        public INetTransport Transport { get; private set; }
        public bool IsHost => Transport != null && Transport.IsHost;
        public bool Active => Transport != null && Transport.IsRunning;

        // Slot -> the peer that owns it (PeerId.None = AI-controlled). Index 0 = keeper.
        readonly PeerId[] _slotOwner = new PeerId[MaxSlots];
        // Host-side: latest input frame received per slot (clients') + the host's own.
        readonly InputFrame[] _slotInput = new InputFrame[MaxSlots];
        // The local player's assigned slot + role (client + host).
        public int LocalSlot { get; private set; } = -1;
        public NetRole LocalRole { get; private set; } = NetRole.Spectator;

        // Client-side: the most recent snapshot from the host (the driver interpolates to it).
        public Snapshot LatestSnapshot { get; private set; }
        public bool HasSnapshot { get; private set; }

        // Raised on clients when the host sends a tagged match event (goal, kickoff, etc).
        public event Action<string> MatchEvent;
        // Raised when this peer's slot assignment arrives (client) or is set (host).
        public event Action<int, NetRole> SlotAssigned;

        public NetSession(INetTransport transport)
        {
            Transport = transport;
            Transport.MessageReceived += OnMessage;
            Transport.PeerJoined += OnPeerJoined;
            Transport.PeerLeft += OnPeerLeft;
            Transport.Connected += OnConnectedToHost;
        }

        // ---- lifecycle ----
        public void Host(int maxPlayers)
        {
            for (int i = 0; i < MaxSlots; i++) _slotOwner[i] = PeerId.None;
            Transport.StartHost(Mathf.Clamp(maxPlayers, 1, MaxSlots));
            // The host takes a slot immediately. Default: host is a shooter (slot 1) so the
            // keeper (slot 0) can be a joining human or AI; a striker-only host with no
            // joiners still works (AI keeper + host shooter).
            AssignLocal(1, NetRole.Shooter);
            _slotOwner[1] = Transport.LocalPeer;
        }

        public void JoinLobby(ulong lobbyOrHost) => Transport.Join(lobbyOrHost);
        public void Leave() => Transport.Shutdown();

        public void Poll() => Transport?.Poll();

        // ---- slot table queries (used by the mode driver) ----
        public bool SlotIsHuman(int slot) => slot >= 0 && slot < MaxSlots && _slotOwner[slot].IsValid;
        public bool SlotIsLocal(int slot) => slot == LocalSlot;
        public InputFrame InputForSlot(int slot) => _slotInput[slot];

        // ---- host: gather inputs + broadcast state ----
        // The host sets its own input each tick; clients' arrive over the wire.
        public void SetLocalInput(in InputFrame f)
        {
            if (LocalSlot >= 0) _slotInput[LocalSlot] = f;
            if (!IsHost && Active) Transport.Send(Transport.HostPeer, NetCodec.Input(f), NetChannel.Unreliable);
        }

        public void BroadcastSnapshot(in Snapshot s)
        {
            if (!IsHost) return;
            LatestSnapshot = s; HasSnapshot = true;
            Transport.SendToAll(NetCodec.Snap(s), NetChannel.Unreliable);
        }

        public void BroadcastEvent(string tag)
        {
            if (IsHost) Transport.SendToAll(NetCodec.Event(tag), NetChannel.Reliable);
        }

        // ---- message routing ----
        void OnMessage(PeerId from, byte[] data)
        {
            var r = new NetReader(data);
            switch (r.Type)
            {
                case MsgType.Hello:      // host: a client announced itself -> give it a slot
                    if (IsHost) GrantSlot(from);
                    break;
                case MsgType.PlayerInput: // host: store the client's input into its slot
                    if (IsHost)
                    {
                        int slot = SlotOf(from);
                        if (slot >= 0) _slotInput[slot] = NetCodec.ReadInput(r);
                    }
                    break;
                case MsgType.AssignSlot:  // client: the host told us our slot
                    AssignLocal(r.U8(), (NetRole)r.U8());
                    break;
                case MsgType.Snapshot:    // client: newest state to interpolate toward
                    LatestSnapshot = NetCodec.ReadSnap(r); HasSnapshot = true;
                    break;
                case MsgType.MatchEvent:  // client: a match event
                    MatchEvent?.Invoke(r.Str());
                    break;
            }
        }

        void OnConnectedToHost()
        {
            // Announce ourselves to the HOST (the transport resolves the host peer on
            // connect); the host replies with AssignSlot.
            Transport.Send(Transport.HostPeer, NetCodec.Hello(PlayerProfile.PlayerName), NetChannel.Reliable);
        }

        void OnPeerJoined(PeerId p) { /* host waits for the peer's Hello to grant a slot */ }

        void OnPeerLeft(PeerId p)
        {
            int slot = SlotOf(p);
            if (slot >= 0) _slotOwner[slot] = PeerId.None;   // slot reverts to AI
        }

        // Host: give a newly-hello'd client the lowest free SHOOTER slot (1..N); if none,
        // and slot 0 (keeper) is free, give them the keeper; else spectator.
        void GrantSlot(PeerId peer)
        {
            for (int s = 1; s < MaxSlots; s++)
                if (!_slotOwner[s].IsValid) { _slotOwner[s] = peer; Transport.Send(peer, NetCodec.AssignSlot((byte)s, NetRole.Shooter), NetChannel.Reliable); return; }
            if (!_slotOwner[0].IsValid) { _slotOwner[0] = peer; Transport.Send(peer, NetCodec.AssignSlot(0, NetRole.Keeper), NetChannel.Reliable); return; }
            Transport.Send(peer, NetCodec.AssignSlot(255, NetRole.Spectator), NetChannel.Reliable);
        }

        void AssignLocal(int slot, NetRole role)
        {
            LocalSlot = slot; LocalRole = role;
            SlotAssigned?.Invoke(slot, role);
        }

        int SlotOf(PeerId p)
        {
            for (int i = 0; i < MaxSlots; i++) if (_slotOwner[i].Equals(p)) return i;
            return -1;
        }
    }
}
