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
        public const int MaxSlots = 8;      // slot 0 keeper, 1..6 shooters, 7 crosser
        public const int CrosserSlot = MaxSlots - 1;   // slot 7 = the crosser role

        // Role a given slot represents: 0 = keeper, MaxSlots-1 = crosser, else shooter.
        public static NetRole RoleOfSlot(int slot)
            => slot == 0 ? NetRole.Keeper : slot == CrosserSlot ? NetRole.Crosser : NetRole.Shooter;

        public INetTransport Transport { get; private set; }
        public bool IsHost => Transport != null && Transport.IsHost;
        public bool Active => Transport != null && Transport.IsRunning;

        // Slot -> the peer that owns it (PeerId.None = AI-controlled). Index 0 = keeper.
        readonly PeerId[] _slotOwner = new PeerId[MaxSlots];
        // Host-side: latest input frame received per slot (clients') + the host's own.
        readonly InputFrame[] _slotInput = new InputFrame[MaxSlots];
        // Host-side: highest input tick applied per slot, so a reordered older input frame
        // (UDP can deliver out of order) doesn't overwrite a newer one.
        readonly uint[] _slotInputTick = new uint[MaxSlots];
        // The local player's assigned slot + role (client + host).
        public int LocalSlot { get; private set; } = -1;
        public NetRole LocalRole { get; private set; } = NetRole.Spectator;

        // Client-side: the most recent snapshot from the host (the driver interpolates to it).
        public Snapshot LatestSnapshot { get; private set; }
        public bool HasSnapshot { get; private set; }
        uint _lastSnapshotTick;   // drop reordered/stale snapshots (UDP can deliver out of order)

        // ---- lobby state ----
        readonly string[] _slotName = new string[MaxSlots];
        readonly bool[] _slotReady = new bool[MaxSlots];
        // Host-only: per-slot AI enable. A non-human slot with _slotAi[i] true is an AI
        // ("Clanker"); false = an open, unfilled slot. Defaults all-on (AI fills by default;
        // the host toggles individual slots off in the lobby).
        readonly bool[] _slotAi = new bool[MaxSlots];
        public MatchConfig Config;                 // host authors it; clients receive it
        public LobbySlot[] Roster { get; private set; } = new LobbySlot[0];   // client mirror + host snapshot
        public bool MatchStarted { get; private set; }

        // Raised on clients when the host sends a tagged match event (goal, kickoff, etc).
        public event Action<string> MatchEvent;
        // Raised when this peer's slot assignment arrives (client) or is set (host).
        public event Action<int, NetRole> SlotAssigned;
        // Raised on any peer when the lobby roster/config changes (redraw the lobby UI).
        public event Action RosterChanged;
        // Raised on all peers when the host starts the match.
        public event Action MatchStarting;
        // Post-goal replay coordination: host tells everyone to start / end; clients vote to
        // skip and the host ends once every human has voted.
        public event Action ReplayStarted;
        public event Action ReplayEnded;
        readonly HashSet<int> _skipVotes = new HashSet<int>();

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
            for (int i = 0; i < MaxSlots; i++) { _slotOwner[i] = PeerId.None; _slotName[i] = null; _slotReady[i] = false; _slotAi[i] = true; }
            Transport.StartHost(Mathf.Clamp(maxPlayers, 1, MaxSlots));
            // The host takes a slot immediately. Default: host is a shooter (slot 1) so the
            // keeper (slot 0) can be a joining human or AI; a striker-only host with no
            // joiners still works (AI keeper + host shooter).
            AssignLocal(1, NetRole.Shooter);
            _slotOwner[1] = Transport.LocalPeer;
            _slotName[1] = PlayerProfile.PlayerName;
            RebuildRoster();
        }

        // Host: author the match config (mode/stadium/etc) and push it to everyone.
        public void SetConfig(MatchConfig cfg) { if (IsHost) { Config = cfg; PushRoster(); } }

        public void JoinLobby(ulong lobbyOrHost) => Transport.Join(lobbyOrHost);
        public void Leave() => Transport.Shutdown();

        public void Poll() => Transport?.Poll();

        // ---- lobby actions ----
        // Local player toggles ready. Host applies + re-pushes; client tells the host.
        public void SetReady(bool ready)
        {
            if (LocalSlot < 0 || LocalSlot >= MaxSlots) return;
            if (IsHost) { _slotReady[LocalSlot] = ready; PushRoster(); }
            else Transport.Send(Transport.HostPeer, NetCodec.Ready(ready), NetChannel.Reliable);
        }

        public bool LocalReady => LocalSlot >= 0 && LocalSlot < MaxSlots && _slotReady[LocalSlot];

        // Host: toggle AI on/off for a non-human slot (the lobby's per-slot AI button). A slot
        // a human holds is never affected. Re-pushes the roster so everyone sees the change.
        // Host-authoritative: clients only render the state, they don't call this.
        public void SetSlotAi(int slot, bool on)
        {
            if (!IsHost || slot < 0 || slot >= MaxSlots) return;
            if (_slotOwner[slot].IsValid) return;   // human-held: leave it
            _slotAi[slot] = on;
            PushRoster();
        }

        // The current roster row for a slot (authoritative on host + client), or a default
        // (all-false) LobbySlot if out of range. Drivers read this to decide spawn/AI/empty.
        public LobbySlot RosterSlot(int slot)
        {
            var r = Roster;
            if (r != null) for (int i = 0; i < r.Length; i++) if (r[i].slot == slot) return r[i];
            return default;
        }

        // Host: are all HUMAN-held slots ready? (AI slots don't gate.)
        public bool AllReady()
        {
            for (int i = 0; i < MaxSlots; i++)
                if (_slotOwner[i].IsValid && !_slotReady[i]) return false;
            return true;
        }

        // Host: launch the match for everyone.
        public void StartMatch()
        {
            if (!IsHost) return;
            MatchStarted = true;
            Transport.SendToAll(NetCodec.Start(), NetChannel.Reliable);
            MatchStarting?.Invoke();
        }

        // ---- post-goal replay ----
        // Host: tell everyone (incl. self) to roll the replay; clear any prior skip votes.
        public void BeginReplay()
        {
            if (!IsHost) return;
            _skipVotes.Clear();
            Transport.SendToAll(NetCodec.ReplayStart(), NetChannel.Reliable);
            ReplayStarted?.Invoke();
        }

        // Any human clicks to skip. Host tallies locally; clients send a vote to the host.
        public void VoteSkip()
        {
            if (IsHost) { _skipVotes.Add(LocalSlot); TryEndReplay(); }
            else Transport.Send(Transport.HostPeer, NetCodec.SkipVote(), NetChannel.Reliable);
        }

        // Host: end the replay for everyone (all humans voted, or the buffer ran out).
        public void EndReplayHost()
        {
            if (!IsHost) return;
            _skipVotes.Clear();
            Transport.SendToAll(NetCodec.ReplayEnd(), NetChannel.Reliable);
            ReplayEnded?.Invoke();
        }

        // Count of human-held slots (for the skip tally).
        int HumanCount()
        {
            int n = 0; for (int i = 0; i < MaxSlots; i++) if (_slotOwner[i].IsValid) n++; return n;
        }

        void TryEndReplay()
        {
            if (IsHost && _skipVotes.Count >= HumanCount()) EndReplayHost();
        }

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
                    if (IsHost) GrantSlot(from, r.Str());
                    break;
                case MsgType.ReadyToggle: // host: a client set its ready state
                    if (IsHost) { int s = SlotOf(from); if (s >= 0) { _slotReady[s] = r.B(); PushRoster(); } }
                    break;
                case MsgType.RequestSlot: // host: a client wants to claim a slot (role pick)
                    if (IsHost) ApplySlotRequest(from, r.U8());
                    break;
                case MsgType.RosterSync:  // client: full roster + config from host
                    NetCodec.ReadRoster(r, out var cfg, out var slots);
                    Config = cfg; Roster = slots;
                    RosterChanged?.Invoke();
                    break;
                case MsgType.StartMatch:  // client: host started the match
                    MatchStarted = true;
                    MatchStarting?.Invoke();
                    break;
                case MsgType.ReplayStart: // client: host says roll the replay
                    ReplayStarted?.Invoke();
                    break;
                case MsgType.SkipVote:    // host: a client voted to skip the replay
                    if (IsHost) { int sv = SlotOf(from); if (sv >= 0) _skipVotes.Add(sv); TryEndReplay(); }
                    break;
                case MsgType.ReplayEnd:   // client: host ended the replay
                    ReplayEnded?.Invoke();
                    break;
                case MsgType.PlayerInput: // host: store the client's input into its slot
                    if (IsHost)
                    {
                        int slot = SlotOf(from);
                        if (slot >= 0)
                        {
                            var f = NetCodec.ReadInput(r);
                            // Drop a reordered/stale frame so a late-arriving older input can't
                            // overwrite a newer one already applied for this slot.
                            if (f.tick >= _slotInputTick[slot])
                            {
                                _slotInput[slot] = f;
                                _slotInputTick[slot] = f.tick;
                            }
                        }
                    }
                    break;
                case MsgType.AssignSlot:  // client: the host told us our slot
                    AssignLocal(r.U8(), (NetRole)r.U8());
                    break;
                case MsgType.Snapshot:    // client: newest state to interpolate toward
                {
                    var snap = NetCodec.ReadSnap(r);
                    // Drop a reordered/stale snapshot (UDP can deliver out of order) so the
                    // client doesn't rubber-band back to an older world state.
                    if (!HasSnapshot || snap.tick > _lastSnapshotTick)
                    {
                        LatestSnapshot = snap; HasSnapshot = true; _lastSnapshotTick = snap.tick;
                    }
                    break;
                }
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
            if (slot >= 0) { _slotOwner[slot] = PeerId.None; _slotName[slot] = null; _slotReady[slot] = false; }   // reverts to AI
            if (IsHost) PushRoster();
        }

        // Host: give a newly-hello'd client the lowest free SHOOTER slot (1..N); if none,
        // and slot 0 (keeper) is free, give them the keeper; else spectator. Then re-push
        // the full roster (+ config) so everyone, including the new joiner, is in sync.
        void GrantSlot(PeerId peer, string name)
        {
            int granted = -1;
            // Lowest free SHOOTER slot (1..MaxSlots-2), then keeper (0), then crosser
            // (MaxSlots-1). Players re-pick any free role in the lobby afterward.
            for (int s = 1; s < CrosserSlot; s++)
                if (!_slotOwner[s].IsValid) { granted = s; break; }
            if (granted < 0 && !_slotOwner[0].IsValid) granted = 0;
            if (granted < 0 && !_slotOwner[CrosserSlot].IsValid) granted = CrosserSlot;

            NetRole role = granted < 0 ? NetRole.Spectator : RoleOfSlot(granted);
            if (granted >= 0) { _slotOwner[granted] = peer; _slotName[granted] = string.IsNullOrEmpty(name) ? "PLAYER" : name; }
            Transport.Send(peer, NetCodec.AssignSlot((byte)(granted < 0 ? 255 : granted), role), NetChannel.Reliable);
            PushRoster();
        }

        // Client picks a role in the lobby by requesting its slot. Host validates the target
        // is free, moves the requester there (freeing their old slot), re-assigns + re-pushes.
        // Mirrors SetReady's host-vs-client routing.
        public void RequestSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            if (IsHost) ApplySlotRequest(Transport.LocalPeer, slot);
            else Transport.Send(Transport.HostPeer, NetCodec.RequestSlot((byte)slot), NetChannel.Reliable);
        }

        // Host: move `peer` into `target` if it's free, clearing their previous slot. The
        // mover's ready flag resets (new role = re-confirm). No-op if the slot is taken.
        void ApplySlotRequest(PeerId peer, int target)
        {
            if (target < 0 || target >= MaxSlots) return;
            if (_slotOwner[target].IsValid) return;          // taken (incl. by the requester's own slot)
            int cur = SlotOf(peer);
            string name = cur >= 0 ? _slotName[cur] : PlayerProfile.PlayerName;
            if (cur >= 0) { _slotOwner[cur] = PeerId.None; _slotName[cur] = null; _slotReady[cur] = false; }
            _slotOwner[target] = peer;
            _slotName[target] = string.IsNullOrEmpty(name) ? "PLAYER" : name;
            _slotReady[target] = false;
            // Tell the mover their new slot/role (host updates its own LocalSlot directly).
            if (peer.Equals(Transport.LocalPeer)) AssignLocal(target, RoleOfSlot(target));
            else Transport.Send(peer, NetCodec.AssignSlot((byte)target, RoleOfSlot(target)), NetChannel.Reliable);
            PushRoster();
        }

        void AssignLocal(int slot, NetRole role)
        {
            LocalSlot = slot; LocalRole = role;
            SlotAssigned?.Invoke(slot, role);
        }

        // Host: build the roster array from the slot tables and broadcast it to all clients,
        // and refresh the host's own mirror + fire RosterChanged locally.
        void RebuildRoster()
        {
            var list = new List<LobbySlot>();
            int clanker = 0;                     // AI slots numbered 1..N in ascending slot order
            for (int i = 0; i < MaxSlots; i++)
            {
                bool human = _slotOwner[i].IsValid;
                bool ai = !human && _slotAi[i];  // a non-human slot the host left AI-on
                string name;
                if (human) name = _slotName[i] ?? "PLAYER";
                else if (ai) name = "Clanker " + (++clanker);
                else name = "Open";              // unfilled, AI toggled off
                list.Add(new LobbySlot { slot = (byte)i, human = human, ai = ai, ready = _slotReady[i],
                                         role = (byte)RoleOfSlot(i), name = name });
            }
            Roster = list.ToArray();
        }

        void PushRoster()
        {
            if (!IsHost) return;
            RebuildRoster();
            Transport.SendToAll(NetCodec.Roster(Config, Roster), NetChannel.Reliable);
            RosterChanged?.Invoke();
        }

        int SlotOf(PeerId p)
        {
            for (int i = 0; i < MaxSlots; i++) if (_slotOwner[i].Equals(p)) return i;
            return -1;
        }
    }
}
