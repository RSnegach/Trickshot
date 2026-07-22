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
        // Host-side per-slot appearance (from each player's Hello). Copied into the roster rows
        // and broadcast so every client can build remote bodies with the right look.
        readonly PlayerAppearance[] _slotAppearance = new PlayerAppearance[MaxSlots];
        // Per-slot painted-jersey PNG (too big for the roster row, so it rides a chunked side
        // channel keyed by slot). Null = that slot has no custom jersey (falls back to team kit).
        // The decoded Texture2D is cached lazily in _slotJerseyTex on first JerseyForSlot() use.
        readonly byte[][] _slotJerseyPng = new byte[MaxSlots][];
        readonly Texture2D[] _slotJerseyTex = new Texture2D[MaxSlots];
        // In-flight jersey reassembly buffers, keyed by slot (a slot only transfers one at a time).
        readonly Dictionary<int, JerseyRx> _jerseyRx = new Dictionary<int, JerseyRx>();
        const int JerseyChunkBytes = 1000;   // payload bytes per chunk (under the ~1.2KB UDP MTU)
        class JerseyRx { public byte[] buf; public uint total; public int have; public bool[] got; }
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
        // Set-pieces: host broadcasts the shootout tally; clients read the latest here + event.
        public event Action<ShootoutState> ShootoutUpdated;
        public ShootoutState LatestShootout { get; private set; }
        // Raised on any peer when a slot's networked jersey finishes reassembling (arg = slot), so
        // a live match can swap that body's torso material.
        public event Action<int> JerseyUpdated;

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
            _slotAppearance[1] = PlayerProfile.Appearance;   // host's own look
            _slotJerseyPng[1] = PlayerProfile.JerseyPng;     // host's painted kit (local, no transfer)
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

        // Re-sync the local player's appearance after they re-customize in the lobby (the initial
        // Hello / host self-set captured it BEFORE customization). Host applies to its own slot +
        // re-pushes the roster; a client tells the host, which applies it and re-pushes.
        public void UpdateLocalAppearance()
        {
            if (IsHost)
            {
                if (LocalSlot >= 0 && LocalSlot < MaxSlots) _slotAppearance[LocalSlot] = PlayerProfile.Appearance;
                PushRoster();
            }
            else Transport.Send(Transport.HostPeer, NetCodec.Loadout(PlayerProfile.Appearance), NetChannel.Reliable);
            PushLocalJersey();
        }

        // Re-sync the local player's painted jersey. The jersey is far too big for the roster row,
        // so it rides its own chunked side channel: the host stores it locally + broadcasts the
        // chunks to all peers; a client sends the chunks to the host (which stores + re-broadcasts).
        // Mirrors UpdateLocalAppearance's host-vs-client split. Called on join + on re-customize.
        public void PushLocalJersey()
        {
            byte[] png = PlayerProfile.JerseyPng;
            if (LocalSlot < 0 || LocalSlot >= MaxSlots) return;
            if (IsHost)
            {
                _slotJerseyPng[LocalSlot] = png;
                _slotJerseyTex[LocalSlot] = null;   // invalidate cached decode
                if (png != null && png.Length > 0) BroadcastJersey((byte)LocalSlot, png);
            }
            else if (png != null && png.Length > 0)
            {
                SendJerseyChunks(Transport.HostPeer, (byte)LocalSlot, png);
            }
        }

        // Split a jersey PNG into reliable chunks and send them to ONE peer.
        void SendJerseyChunks(PeerId to, byte slot, byte[] png)
        {
            uint total = (uint)((png.Length + JerseyChunkBytes - 1) / JerseyChunkBytes);
            for (uint i = 0; i < total; i++)
                Transport.Send(to, JerseyChunkAt(slot, i, total, png), NetChannel.Reliable);
        }

        // Host: broadcast a slot's jersey chunks to all peers (SendToAll already skips the host).
        // The origin client re-receiving its own jersey is harmless (idempotent reassembly).
        void BroadcastJersey(byte slot, byte[] png)
        {
            uint total = (uint)((png.Length + JerseyChunkBytes - 1) / JerseyChunkBytes);
            for (uint i = 0; i < total; i++)
                Transport.SendToAll(JerseyChunkAt(slot, i, total, png), NetChannel.Reliable);
        }

        // Build the i-th jersey chunk message for a PNG.
        static byte[] JerseyChunkAt(byte slot, uint i, uint total, byte[] png)
        {
            int off = (int)i * JerseyChunkBytes;
            int len = Mathf.Min(JerseyChunkBytes, png.Length - off);
            var chunk = new byte[len];
            Array.Copy(png, off, chunk, 0, len);
            return NetCodec.JerseyChunk(slot, i, total, (uint)png.Length, chunk);
        }

        // The decoded jersey texture for a slot, or null if that slot has no networked jersey.
        // Decodes lazily from the stored PNG and caches the Texture2D.
        public Texture2D JerseyForSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return null;
            if (_slotJerseyTex[slot] != null) return _slotJerseyTex[slot];
            var png = _slotJerseyPng[slot];
            if (png == null || png.Length == 0) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(png)) { _slotJerseyTex[slot] = tex; return tex; }
            return null;
        }

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

        // Host: push the set-pieces shootout tally to everyone (reliable), and update the
        // host's own LatestShootout + fire the event locally so its HUD reads the same value.
        public void BroadcastShootout(in ShootoutState s)
        {
            if (!IsHost) return;
            LatestShootout = s;
            Transport.SendToAll(NetCodec.Shootout(s), NetChannel.Reliable);
            ShootoutUpdated?.Invoke(s);
        }

        // Reassemble a jersey chunk. On the HOST the authoritative slot is the SENDER's slot (a
        // client can't spoof another slot's kit); on a CLIENT the slot is the message field (the
        // host broadcasts on behalf of every slot). When all chunks are in: store the PNG, drop the
        // cached decode, fire JerseyUpdated, and (host) re-broadcast so every peer gets this kit.
        void OnJerseyChunk(PeerId from, JerseyChunkMsg m)
        {
            int slot = IsHost ? SlotOf(from) : m.slot;
            if (slot < 0 || slot >= MaxSlots) return;
            if (m.total == 0 || m.totalBytes == 0) return;

            if (!_jerseyRx.TryGetValue(slot, out var rx) || rx.total != m.total || rx.buf == null
                || rx.buf.Length != m.totalBytes)
            {
                rx = new JerseyRx { buf = new byte[m.totalBytes], total = m.total, have = 0, got = new bool[m.total] };
                _jerseyRx[slot] = rx;
            }
            if (m.index >= m.total || m.chunk == null) return;
            if (!rx.got[m.index])
            {
                rx.got[m.index] = true;
                rx.have++;
                int off = (int)m.index * JerseyChunkBytes;
                int len = Mathf.Min(m.chunk.Length, rx.buf.Length - off);
                if (len > 0) Array.Copy(m.chunk, 0, rx.buf, off, len);
            }
            if (rx.have < rx.total) return;

            // Complete.
            byte[] png = rx.buf;
            _jerseyRx.Remove(slot);
            _slotJerseyPng[slot] = png;
            _slotJerseyTex[slot] = null;   // invalidate cached decode
            if (IsHost) BroadcastJersey((byte)slot, png);   // relay this slot's kit to all peers
            JerseyUpdated?.Invoke(slot);
        }

        // ---- message routing ----
        void OnMessage(PeerId from, byte[] data)
        {
            var r = new NetReader(data);
            switch (r.Type)
            {
                case MsgType.Hello:      // host: a client announced itself -> give it a slot
                    if (IsHost) { string hn = r.Str(); var ha = NetCodec.ReadAppearance(r); GrantSlot(from, hn, ha); }
                    break;
                case MsgType.ReadyToggle: // host: a client set its ready state
                    if (IsHost) { int s = SlotOf(from); if (s >= 0) { _slotReady[s] = r.B(); PushRoster(); } }
                    break;
                case MsgType.UpdateLoadout: // host: a client re-customized -> update its slot appearance
                    if (IsHost) { var la = NetCodec.ReadAppearance(r); int s = SlotOf(from); if (s >= 0) { _slotAppearance[s] = la; PushRoster(); } }
                    break;
                case MsgType.JerseyChunk: // a jersey PNG chunk (client->host, or host->clients broadcast)
                    OnJerseyChunk(from, NetCodec.ReadJerseyChunk(r));
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
                    // Now that we know our slot, send our painted jersey up to the host (chunked).
                    PushLocalJersey();
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
                case MsgType.ShootoutState: // client: latest set-pieces tally
                {
                    var so = NetCodec.ReadShootout(r);
                    LatestShootout = so;
                    ShootoutUpdated?.Invoke(so);
                    break;
                }
            }
        }

        void OnConnectedToHost()
        {
            // Announce ourselves to the HOST (the transport resolves the host peer on
            // connect); the host replies with AssignSlot. The Hello carries the tiny appearance
            // struct inline; the bulky painted jersey follows over its own chunked channel once
            // we know our slot (deferred to the AssignSlot handler so LocalSlot is set first).
            Transport.Send(Transport.HostPeer, NetCodec.Hello(PlayerProfile.PlayerName, PlayerProfile.Appearance), NetChannel.Reliable);
        }

        void OnPeerJoined(PeerId p) { /* host waits for the peer's Hello to grant a slot */ }

        void OnPeerLeft(PeerId p)
        {
            int slot = SlotOf(p);
            if (slot >= 0)
            {
                _slotOwner[slot] = PeerId.None; _slotName[slot] = null; _slotReady[slot] = false;   // reverts to AI
                _slotJerseyPng[slot] = null; _slotJerseyTex[slot] = null; _jerseyRx.Remove(slot);   // drop their kit
            }
            if (IsHost) PushRoster();
        }

        // Host: give a newly-hello'd client the lowest free SHOOTER slot (1..N); if none,
        // and slot 0 (keeper) is free, give them the keeper; else spectator. Then re-push
        // the full roster (+ config) so everyone, including the new joiner, is in sync.
        void GrantSlot(PeerId peer, string name, PlayerAppearance appearance)
        {
            int granted = -1;
            // Lowest free SHOOTER slot (1..MaxSlots-2), then keeper (0), then crosser
            // (MaxSlots-1). Players re-pick any free role in the lobby afterward.
            for (int s = 1; s < CrosserSlot; s++)
                if (!_slotOwner[s].IsValid) { granted = s; break; }
            if (granted < 0 && !_slotOwner[0].IsValid) granted = 0;
            if (granted < 0 && !_slotOwner[CrosserSlot].IsValid) granted = CrosserSlot;

            NetRole role = granted < 0 ? NetRole.Spectator : RoleOfSlot(granted);
            if (granted >= 0) { _slotOwner[granted] = peer; _slotName[granted] = string.IsNullOrEmpty(name) ? "PLAYER" : name; _slotAppearance[granted] = appearance; }
            Transport.Send(peer, NetCodec.AssignSlot((byte)(granted < 0 ? 255 : granted), role), NetChannel.Reliable);
            PushRoster();
            // Send the new peer every ALREADY-KNOWN slot jersey. Appearance rides the roster row so
            // it reaches the joiner automatically, but jerseys are a side channel - without this a
            // late joiner sees existing players in default kits. (Their OWN jersey arrives when they
            // push it after AssignSlot.)
            for (int s = 0; s < MaxSlots; s++)
                if (s != granted && _slotJerseyPng[s] != null && _slotJerseyPng[s].Length > 0)
                    SendJerseyChunks(peer, (byte)s, _slotJerseyPng[s]);
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
            PlayerAppearance appr = cur >= 0 ? _slotAppearance[cur] : PlayerProfile.Appearance;
            byte[] jersey = cur >= 0 ? _slotJerseyPng[cur] : null;   // move the kit with the player
            if (cur >= 0)
            {
                _slotOwner[cur] = PeerId.None; _slotName[cur] = null; _slotReady[cur] = false;
                _slotJerseyPng[cur] = null; _slotJerseyTex[cur] = null;
            }
            _slotOwner[target] = peer;
            _slotName[target] = string.IsNullOrEmpty(name) ? "PLAYER" : name;
            _slotAppearance[target] = appr;   // move the player's look with them
            _slotJerseyPng[target] = jersey; _slotJerseyTex[target] = null;
            _slotReady[target] = false;
            // Tell the mover their new slot/role (host updates its own LocalSlot directly).
            if (peer.Equals(Transport.LocalPeer)) AssignLocal(target, RoleOfSlot(target));
            else Transport.Send(peer, NetCodec.AssignSlot((byte)target, RoleOfSlot(target)), NetChannel.Reliable);
            PushRoster();
            // Re-broadcast the moved kit at its new slot so every peer re-keys it (the roster row
            // only carries the small appearance struct, not the jersey).
            if (jersey != null && jersey.Length > 0) BroadcastJersey((byte)target, jersey);
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
                // Humans carry their synced look; AI/open slots get default appearance.
                var appr = human ? _slotAppearance[i] : PlayerAppearance.Default;
                list.Add(new LobbySlot { slot = (byte)i, human = human, ai = ai, ready = _slotReady[i],
                                         role = (byte)RoleOfSlot(i), name = name, appearance = appr });
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
