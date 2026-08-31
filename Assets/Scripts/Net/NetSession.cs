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
    /// Slot model, SINGLE-GOAL modes (striker / set pieces / accuracy): slot 0 is the KEEPER,
    /// slots 1..N are SHOOTERS, slot 7 is the crosser. Joining players fill the lowest free
    /// shooter slot; AI fills any slot no human holds.
    ///
    /// Slot model, SCRIMMAGE: the same eight slots carry TWO teams - 0-3 Home, 4-7 Away - so a
    /// slot decomposes into (team, shirt) with shirt 0 = keeper and 1..perSide-1 = outfield on
    /// each side. Shirt, not slot, is the identity everything downstream keys on (AI pace, the
    /// stat rows, the formation table), and nothing about it crosses the wire: every peer derives
    /// it from the slot with ScrimTeamOfSlot / ScrimShirtOfSlot, which is exactly why the derived
    /// values agree. That also means slot 4 is a KEEPER in scrimmage even though RoleOfSlot, which
    /// describes the single-goal layout, calls it a shooter - see RoleForSlot.
    /// </summary>
    public class NetSession
    {
        public const int MaxSlots = 8;      // slot 0 keeper, 1..6 shooters, 7 crosser
        public const int CrosserSlot = MaxSlots - 1;   // slot 7 = the crosser role

        // Role a given slot represents in a SINGLE-GOAL mode: 0 = keeper, MaxSlots-1 = crosser,
        // else shooter. This is what the wire's NetRole byte means. Scrimmage overlays a different
        // layout on the same slots - use RoleForSlot for anything the player reads.
        public static NetRole RoleOfSlot(int slot)
            => slot == 0 ? NetRole.Keeper : slot == CrosserSlot ? NetRole.Crosser : NetRole.Shooter;

        // ---- scrimmage slot layout: slot -> (team, shirt) ----
        // Slots 0-3 are Home, 4-7 are Away, and within a team the in-team index IS the shirt:
        // 0 = keeper, 1..perSide-1 = outfield. Both halves are the same size, so shirt is just the
        // slot modulo the half. Derived on every peer rather than synced, which is what makes
        // SimConfig.AiPace(team, shirt, keeper) produce the same pace host-side and client-side.
        public const int ScrimSlotsPerTeam = MaxSlots / 2;   // 4
        public static int ScrimTeamOfSlot(int slot) => slot < ScrimSlotsPerTeam ? 0 : 1;

        /// <summary>
        /// This slot's scrimmage shirt: 0 = keeper, 1.. = outfield. Returns -1 for anything that is
        /// not a slot, which matters because a refused joiner carries LocalSlot 255: without the
        /// guard 255 folds to 251 and any "shirt == 0 means keeper" test still answers sensibly,
        /// but 255 - ScrimSlotsPerTeam is a number nothing else in the codebase would recognise.
        /// </summary>
        public static int ScrimShirtOfSlot(int slot)
            => (slot < 0 || slot >= MaxSlots) ? -1
             : (slot < ScrimSlotsPerTeam ? slot : slot - ScrimSlotsPerTeam);

        /// <summary>
        /// The scrimmage team size the SEATING enforces: the host's chosen perSide, clamped to what
        /// the eight-slot board can hold. NetScrimmageMatch clamps its own copy to the identical
        /// range, and the two have to agree - a session that seats a player the match driver then
        /// refuses a body is worse than a refused join, because the refusal only shows up as a
        /// missing camera after the match has already started.
        ///
        /// 0 reads as the full board, not as "refuse everyone": Host() seats the host and installs
        /// the advert provider BEFORE HostSetupUI calls SetConfig, and cfg.perSide is an untrusted
        /// wire byte, so a config that has not been authored yet must not lock the lobby.
        /// </summary>
        int ScrimPerSide
        {
            get
            {
                int n = Config.perSide;
                return n <= 0 ? ScrimSlotsPerTeam : Mathf.Clamp(n, 2, ScrimSlotsPerTeam);
            }
        }

        /// <summary>
        /// What the lobby should CALL this slot in the mode that is actually configured. Scrimmage
        /// carries two keepers (slots 0 and 4) and RoleOfSlot only ever names slot 0, so the away
        /// keeper used to be published, labelled and read back as a shooter.
        /// </summary>
        NetRole RoleForSlot(int slot)
            => (GameMode)Config.mode == GameMode.Scrimmage
             ? (ScrimShirtOfSlot(slot) == 0 ? NetRole.Keeper : NetRole.Shooter)
             : RoleOfSlot(slot);

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

        // Client-side interpolation buffer: a short ring of recent snapshots stamped with their
        // local receive time. The driver renders remote bodies at (now - InterpDelay) by
        // interpolating between the two snapshots bracketing that render time, so uneven packet
        // arrival never teleports a body. Monotonic in tick + recv time (stale snaps are dropped).
        struct StampedSnap { public Snapshot snap; public float recv; }
        readonly StampedSnap[] _snapBuf = new StampedSnap[32];
        int _snapCount;   // valid entries, oldest at 0 .. newest at _snapCount-1 (shifted when full)

        // ---- lobby state ----
        readonly string[] _slotName = new string[MaxSlots];
        // Host-side per-slot appearance (from each player's Hello). Copied into the roster rows
        // and broadcast so every client can build remote bodies with the right look.
        readonly PlayerAppearance[] _slotAppearance = new PlayerAppearance[MaxSlots];
        // Each slot's PASSING build as a node mask (SkillTree.PackPassing). 0 = uninvested, which is
        // also what a slot reads before its owner's loadout lands and after they leave. The host
        // evaluates a remote player's pass off this instead of substituting a neutral constant, so
        // "passes behave according to stats" is true for a client and not just for whoever is hosting.
        readonly byte[] _slotPassMask = new byte[MaxSlots];
        // Per-slot painted-jersey PNG (too big for the roster row, so it rides a chunked side
        // channel keyed by slot). Null = that slot has no custom jersey (falls back to team kit).
        // The decoded Texture2D is cached lazily in _slotJerseyTex on first JerseyForSlot() use.
        readonly byte[][] _slotJerseyPng = new byte[MaxSlots][];
        readonly Texture2D[] _slotJerseyTex = new Texture2D[MaxSlots];
        // In-flight jersey reassembly buffers, keyed by slot (a slot only transfers one at a time).
        readonly Dictionary<int, JerseyRx> _jerseyRx = new Dictionary<int, JerseyRx>();
        const int JerseyChunkBytes = 1000;   // payload bytes per chunk (under the ~1.2KB UDP MTU)
        // Hard ceiling on a networked jersey. The atlas is 256x520 RGBA = ~520 KB RAW and PNG
        // compresses well below that, so 2 MB is generous; anything claiming more is a corrupt or
        // hostile packet and is dropped rather than allocated (see OnJerseyChunk).
        const uint MaxJerseyBytes = 2u * 1024u * 1024u;
        class JerseyRx { public byte[] buf; public uint total; public int have; public bool[] got; }
        readonly bool[] _slotReady = new bool[MaxSlots];
        // Host-only: per-slot AI enable. A non-human slot with _slotAi[i] true is an AI
        // ("Clanker"); false = an open, unfilled slot. Defaults OFF for all slots except the
        // keeper (slot 0), so the lobby starts empty apart from an AI goalkeeper; the host
        // toggles individual slots on in the lobby. (Set in Host().)
        readonly bool[] _slotAi = new bool[MaxSlots];
        public MatchConfig Config;                 // host authors it; clients receive it
        public LobbySlot[] Roster { get; private set; } = new LobbySlot[0];   // client mirror + host snapshot
        public bool MatchStarted { get; private set; }

        // Raised on clients when the host sends a tagged match event (goal, kickoff, etc).
        public event Action<string> MatchEvent;
        // Raised on clients when the host reports the ball was struck at a world position (3D SFX).
        public event Action<Vector3> BallKicked;
        // Raised on clients when the host reports the ball hit the woodwork: (position, impact speed).
        public event Action<Vector3, float> PostHit;
        // Raised on every peer (host + clients) when a quickchat is delivered: (slot, presetId, custom).
        // presetId 255 => use the custom string; else it's an index into QuickChat.Phrases.
        public event Action<int, int, string> QuickChatReceived;
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
            // AI defaults OFF for every slot EXCEPT the keeper (slot 0), so an unfilled lobby
            // starts empty (open) apart from an AI goalkeeper. The host toggles other slots' AI
            // on per-slot in the lobby. (The crosser still feeds balls on the host regardless of
            // its AI toggle - see NetStrikerMatch - so crosses come even with crosser AI off.)
            for (int i = 0; i < MaxSlots; i++) { _slotOwner[i] = PeerId.None; _slotName[i] = null; _slotReady[i] = false; _slotAi[i] = i == 0; }
            _maxPlayers = Mathf.Clamp(maxPlayers, 1, MaxSlots);   // enforced in GrantSlot
            Transport.StartHost(_maxPlayers);
            // Answer discovery probes with the LIVE lobby. A delegate rather than a snapshot, so the
            // occupancy a browser sees is the occupancy at the moment it asked. Note the config
            // arrives later, via SetConfig, and MatchConfig.publicLobby defaults to false - so
            // between here and there the host correctly advertises nothing.
            Transport.AdvertProvider = BuildAdvert;
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
            else Transport.Send(Transport.HostPeer,
                                NetCodec.Loadout(PlayerProfile.Appearance, SkillTree.PackPassing()),
                                NetChannel.Reliable);
            if (IsHost && LocalSlot >= 0 && LocalSlot < MaxSlots)
                _slotPassMask[LocalSlot] = SkillTree.PackPassing();
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

        // ---- discovery advertisement ----

        /// <summary>Humans holding a slot right now, the host included.</summary>
        public int PlayerCount => HumanCount();

        /// <summary>The player cap the host chose in Host(maxPlayers).</summary>
        public int MaxPlayers => _maxPlayers;

        /// <summary>
        /// True while an arriving human would actually be given a player slot. Deliberately mirrors
        /// GrantSlot rather than approximating it, because _maxPlayers is not the only limit: the
        /// MODE also decides which slots exist (SlotAllowed gates the crosser outside striker, and
        /// caps scrimmage at perSide shirts per team), so a lobby can be full while sitting under
        /// its cap. For scrimmage the two limits coincide exactly at every reachable team size -
        /// HostSetupUI sets maxPlayers = Clamp(perSide*2, 2, 8) and SlotAllowed admits
        /// 2*min(perSide, 4) slots: 3 -> 6 and 6, 5 -> 8 and 8, 11 -> 8 and 8, 2 -> 4 and 4.
        /// </summary>
        public bool HasFreeSlot
        {
            get
            {
                if (!IsHost || MatchStarted) return false;
                if (HumanCount() >= _maxPlayers) return false;
                for (int s = 0; s < MaxSlots; s++)
                    if (!_slotOwner[s].IsValid && SlotAllowed(s)) return true;
                return false;
            }
        }

        /// <summary>
        /// The host's display name as a browser should show it. Reads the host's CURRENT slot, not
        /// slot 1, because the host can move to another role in the lobby.
        /// </summary>
        public string HostName
        {
            get
            {
                if (LocalSlot >= 0 && LocalSlot < MaxSlots && !string.IsNullOrEmpty(_slotName[LocalSlot]))
                    return _slotName[LocalSlot];
                return PlayerProfile.PlayerName;
            }
        }

        /// <summary>Short mode line for a browser row. Scrimmage carries its team size, since that
        /// is what tells a joiner how big the game is.</summary>
        public string ModeLabel()
        {
            var mode = (GameMode)Config.mode;
            switch (mode)
            {
                case GameMode.Scrimmage:
                    int n = Mathf.Max(1, (int)Config.perSide);
                    return "Scrimmage " + n + "v" + n;
                case GameMode.SetPieces: return "Set Pieces";
                default: return mode.ToString();
            }
        }

        /// <summary>
        /// How this session answers "is there a game here?". Installed on the transport by Host() and
        /// invoked once per probe, so it always reports the live lobby instead of a stale snapshot.
        ///
        /// `visible` is the whole gate, and it is deliberately strict: public AND joinable. A full or
        /// already-started lobby drops off the list rather than appearing as an unjoinable row,
        /// because a row you can click but never enter is worse than no row at all - the refusal
        /// only surfaces after a connect attempt, which reads as the game being broken.
        /// </summary>
        LobbyAdvert BuildAdvert() => new LobbyAdvert
        {
            visible = IsHost && Config.publicLobby && HasFreeSlot,
            name = HostName,
            mode = ModeLabel(),
            players = HumanCount(),
            maxPlayers = _maxPlayers,
        };

        void TryEndReplay()
        {
            if (IsHost && _skipVotes.Count >= HumanCount()) EndReplayHost();
        }

        // ---- slot table queries (used by the mode driver) ----
        public bool SlotIsHuman(int slot) => slot >= 0 && slot < MaxSlots && _slotOwner[slot].IsValid;
        public bool SlotIsLocal(int slot) => slot == LocalSlot;
        public InputFrame InputForSlot(int slot) => _slotInput[slot];
        // Highest input tick the host has applied for a slot (host-side). Streamed per body so a
        // client can reconcile its predicted local body against the state produced by that input.
        public uint InputTickForSlot(int slot) => (slot >= 0 && slot < MaxSlots) ? _slotInputTick[slot] : 0u;

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

        // Host: tell every client the ball was struck at `pos` so they can play the 3D kick SFX
        // attenuated by distance to their own player. Unreliable (frequent, transient).
        public void BroadcastBallKick(Vector3 pos)
        {
            if (IsHost) Transport.SendToAll(NetCodec.BallKick(pos), NetChannel.Unreliable);
        }

        // Host: same for the woodwork clang. The speed rides along so the clients' mix matches.
        public void BroadcastPostHit(Vector3 pos, float speed)
        {
            if (IsHost) Transport.SendToAll(NetCodec.PostHit(pos, speed), NetChannel.Unreliable);
        }

        public void BroadcastEvent(string tag)
        {
            if (IsHost) Transport.SendToAll(NetCodec.Event(tag), NetChannel.Reliable);
        }

        // ---- quickchat ----
        // Rocket-League-style anti-spam, host-authoritative + per slot: up to QcBurst messages in a
        // rolling QcWindow; overflowing mutes that slot for QcMute (extended on repeat offenses).
        const int   QcBurst = 4;
        const float QcWindow = 3.5f;
        const float QcMute = 3f;
        readonly Queue<float>[] _qcTimes = NewQcTimes();
        readonly float[] _qcMutedUntil = new float[MaxSlots];
        static Queue<float>[] NewQcTimes()
        {
            var q = new Queue<float>[MaxSlots];
            for (int i = 0; i < MaxSlots; i++) q[i] = new Queue<float>();
            return q;
        }

        // True if `slot` may send right now; records the send and applies the mute if it overflows.
        bool QcAllow(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return false;
            float now = Time.unscaledTime;
            if (now < _qcMutedUntil[slot]) return false;
            var q = _qcTimes[slot];
            while (q.Count > 0 && now - q.Peek() > QcWindow) q.Dequeue();
            if (q.Count >= QcBurst)
            {
                // Overflow: mute (longer if they were recently muted -> escalating penalty).
                _qcMutedUntil[slot] = now + QcMute;
                q.Clear();
                return false;
            }
            q.Enqueue(now);
            return true;
        }

        // Local player wants to send a quickchat. Host applies + relays; a client asks the host.
        public void SendQuickChat(byte presetId, string custom)
        {
            if (!Active) return;
            if (IsHost)
            {
                if (!QcAllow(LocalSlot)) return;
                DeliverQuickChat(LocalSlot, presetId, custom);                 // host renders locally
                Transport.SendToAll(NetCodec.QuickChat((byte)LocalSlot, presetId, custom), NetChannel.Reliable);
            }
            else
            {
                Transport.Send(Transport.HostPeer, NetCodec.QuickChat((byte)LocalSlot, presetId, custom), NetChannel.Reliable);
            }
        }

        // Raise the received event on this peer. Custom text is re-censored here (defense in depth).
        void DeliverQuickChat(int slot, int presetId, string custom)
        {
            string safe = presetId == 255 ? ChatCensor.Clean(custom) : custom;
            QuickChatReceived?.Invoke(slot, presetId, safe);
        }

        // Host: push the set-pieces shootout tally to everyone (reliable), and update the
        // host's own LatestShootout + fire the event locally so its HUD reads the same value.
        // ---- post-match stats ----
        /// <summary>The received post-match table, or the host's own copy of what it sent.</summary>
        public StatRow[] LatestMatchStats { get; private set; }
        public bool HasMatchStats { get; private set; }

        /// <summary>
        /// Forget the table. Needed because NetSession OUTLIVES a match - it is created once and only
        /// dropped on Leave - so without this a second match in the same session opens still holding the
        /// previous one's board. Same shape as ClearSnapshotBuffer, and for the same reason.
        /// </summary>
        public void ClearMatchStats() { LatestMatchStats = null; HasMatchStats = false; }

        /// <summary>Host: publish the final table, once, and keep a local copy so the host draws the
        /// same board from the same data a client does.</summary>
        public void BroadcastMatchStats(StatRow[] rows)
        {
            LatestMatchStats = rows; HasMatchStats = rows != null;
            if (IsHost) Transport.SendToAll(NetCodec.MatchStats(rows), NetChannel.Reliable);
        }

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

            // SANITY-CHECK THE SIZES BEFORE ALLOCATING. total/totalBytes/index arrive as raw uints
            // from an untrusted UDP packet; allocating straight from them (as this used to) let a
            // corrupt or malicious packet ask for a ~4 GB byte[] and bool[], and
            // `(int)index * JerseyChunkBytes` overflowed NEGATIVE for a large index, throwing inside
            // Array.Copy. A real jersey is the 256x520 atlas encoded as PNG, so MaxJerseyBytes is a
            // generous ceiling and the chunk count must agree with it.
            if (m.totalBytes > MaxJerseyBytes) return;
            uint expectChunks = (m.totalBytes + JerseyChunkBytes - 1) / JerseyChunkBytes;
            if (m.total != expectChunks) return;      // count must match the declared byte length
            if (m.index >= m.total || m.chunk == null) return;
            if (m.chunk.Length > JerseyChunkBytes) return;

            if (!_jerseyRx.TryGetValue(slot, out var rx) || rx.total != m.total || rx.buf == null
                || rx.buf.Length != m.totalBytes)
            {
                rx = new JerseyRx { buf = new byte[m.totalBytes], total = m.total, have = 0, got = new bool[m.total] };
                _jerseyRx[slot] = rx;
            }
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

        // ---- client interpolation buffer ----
        // Append a freshly-received snapshot, stamped with the local receive time. Snapshots arrive
        // in tick order (stale ones already dropped), so we just append and shift when full.
        void PushSnap(in Snapshot s)
        {
            float now = Time.realtimeSinceStartup;
            if (_snapCount == _snapBuf.Length)
            {
                Array.Copy(_snapBuf, 1, _snapBuf, 0, _snapBuf.Length - 1);
                _snapCount--;
            }
            _snapBuf[_snapCount].snap = s;
            _snapBuf[_snapCount].recv = now;
            _snapCount++;
        }

        // Sample the interpolation buffer at renderTime = now - delaySeconds. Returns false until
        // there is enough buffered history. `frac` is the 0..1 blend between snapshot `a` and `b`;
        // when only one snapshot brackets renderTime (edge / underflow), a==b and frac=0. The
        // driver then interpolates each body's pos/yaw and the ball between a and b by `frac`.
        public bool SampleInterpolated(float delaySeconds, out Snapshot a, out Snapshot b, out float frac)
        {
            a = default; b = default; frac = 0f;
            if (_snapCount == 0) return false;
            float renderTime = Time.realtimeSinceStartup - delaySeconds;

            // Find the newest pair (i, i+1) whose recv times bracket renderTime.
            for (int i = _snapCount - 1; i >= 0; i--)
            {
                if (_snapBuf[i].recv <= renderTime)
                {
                    if (i == _snapCount - 1) { a = b = _snapBuf[i].snap; frac = 0f; return true; }   // no newer sample yet: hold newest known
                    float t0 = _snapBuf[i].recv, t1 = _snapBuf[i + 1].recv;
                    a = _snapBuf[i].snap; b = _snapBuf[i + 1].snap;
                    frac = t1 > t0 ? Mathf.Clamp01((renderTime - t0) / (t1 - t0)) : 0f;
                    return true;
                }
            }
            // renderTime is older than everything buffered (just started): show the oldest.
            a = b = _snapBuf[0].snap; frac = 0f;
            return true;
        }

        // Drop buffered history (e.g. on match teardown) so a new match starts clean.
        public void ClearSnapshotBuffer() { _snapCount = 0; HasSnapshot = false; }

        // ---- message routing ----
        // Every packet here is UNTRUSTED (raw UDP): it can be truncated, corrupt, or carry a bogus
        // type. NetReader throws (EndOfStreamException) the moment a field reads past the end, and
        // that used to escape all the way out through Transport.Poll() into whatever called
        // Multiplayer.Poll() - aborting the inbox drain and stopping keepalives, so a SINGLE bad
        // packet could kill the session. Decode inside a guard instead: log it once and drop just
        // that packet, then carry on draining.
        void OnMessage(PeerId from, byte[] data)
        {
            try { RouteMessage(from, data); }
            catch (System.Exception e)
            {
                Debug.LogWarning("NetSession: dropped a malformed packet ("
                                 + (data != null ? data.Length : 0) + " bytes): " + e.Message);
            }
        }

        void RouteMessage(PeerId from, byte[] data)
        {
            var r = new NetReader(data);
            // Host-authored message types are only ever legitimate FROM the host:
            //  - on a CLIENT, accept them only from the host peer (a stray/spoofed packet must not
            //    rewrite the roster, reassign our slot, or start the match);
            //  - on the HOST, reject them outright. The host authors these; a client sending one is
            //    always illegitimate. Without this a client could send StartMatch to force the host
            //    to start (ignoring ready state), or RosterSync to overwrite the host's own
            //    authoritative roster + config, or AssignSlot to push the host's LocalSlot to 255.
            //    The client-only handlers below are individually IsHost-gated, but these were not.
            if (IsHostOnly(r.Type) && (IsHost || !from.Equals(Transport.HostPeer))) return;
            switch (r.Type)
            {
                case MsgType.Hello:      // host: a client announced itself -> give it a slot
                    if (IsHost)
                    {
                        string hn = r.Str();
                        var ha = NetCodec.ReadAppearance(r);
                        // Tolerant read: a build older than the version byte sends nothing here, and
                        // 0 is not a valid version, so it is refused with a version message rather
                        // than throwing out of RouteMessage and being silently dropped.
                        byte hv = r.More ? r.U8() : (byte)0;
                        GrantSlot(from, hn, ha, hv);
                    }
                    break;
                case MsgType.ReadyToggle: // host: a client set its ready state
                    if (IsHost) { int s = SlotOf(from); if (s >= 0) { _slotReady[s] = r.B(); PushRoster(); } }
                    break;
                case MsgType.UpdateLoadout: // host: a client re-customized -> update its slot appearance
                    if (IsHost)
                    {
                        var la = NetCodec.ReadAppearance(r);
                        // Trailing passing mask. Guarded because it IS the compatibility mechanism here,
                        // not hygiene: this handler is reachable by a peer whose join was refused for
                        // VERSION (GrantSlot answers a refusal with AssignSlot, and the client pushes
                        // its loadout from that handler), so the grown message really does cross the
                        // version boundary and an older sender must still parse.
                        byte mask = r.More ? r.U8() : (byte)0;
                        int s = SlotOf(from);
                        if (s >= 0)
                        {
                            _slotAppearance[s] = la;
                            // Reject an UNREACHABLE claim outright rather than trimming it: a mask with
                            // a broken prerequisite chain could not have been bought, so it is a
                            // modified client and gets the uninvested floor, not its best-effort build.
                            _slotPassMask[s] = SkillTree.TryUnpackPassing(mask, out _) ? mask : (byte)0;
                            PushRoster();
                        }
                    }
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
                    // The reason byte is read tolerantly for the same reason Hello's version is:
                    // an older host does not send it, and a missing reason must not throw.
                    {
                        byte aSlot = r.U8();
                        var aRole = (NetRole)r.U8();
                        AssignLocal(aSlot, aRole, r.More ? (JoinRefusal)r.U8() : JoinRefusal.None);
                    }
                    // Now that we know our slot, send our painted jersey up to the host (chunked).
                    PushLocalJersey();
                    // ...and our loadout, which nothing else sends at join: UpdateLoadout only ever
                    // fired from the customize screens, so a player who joined and pressed Ready
                    // delivered no stats at all and passed as uninvested for the whole match.
                    // NOT gated on having a slot, deliberately - a spectator that later claims one has
                    // to have already delivered its numbers, because Hello carries none. It IS gated on
                    // the refusal reason: a version-refused peer must not send a message whose trailing
                    // field the other build may not know how to read.
                    if (!IsHost && RefusedBecause != JoinRefusal.Version)
                        Transport.Send(Transport.HostPeer,
                                       NetCodec.Loadout(PlayerProfile.Appearance, SkillTree.PackPassing()),
                                       NetChannel.Reliable);
                    break;
                case MsgType.MatchStats:  // client: the post-match table, once at full time
                    if (!IsHost) { LatestMatchStats = NetCodec.ReadMatchStats(r); HasMatchStats = LatestMatchStats != null; }
                    break;
                case MsgType.Snapshot:    // client: newest state to interpolate toward
                {
                    var snap = NetCodec.ReadSnap(r);
                    // Drop a reordered/stale snapshot (UDP can deliver out of order) so the
                    // client doesn't rubber-band back to an older world state.
                    if (!HasSnapshot || snap.tick > _lastSnapshotTick)
                    {
                        LatestSnapshot = snap; HasSnapshot = true; _lastSnapshotTick = snap.tick;
                        PushSnap(snap);   // append to the interpolation buffer (stamped with recv time)
                    }
                    break;
                }
                case MsgType.MatchEvent:  // client: a match event
                    MatchEvent?.Invoke(r.Str());
                    break;
                case MsgType.BallKick:    // client: the ball was struck at a world position
                    BallKicked?.Invoke(r.V3());
                    break;
                case MsgType.PostHit:     // client: the ball hit the woodwork
                    PostHit?.Invoke(r.V3(), r.F());
                    break;
                case MsgType.QuickChat:
                {
                    NetCodec.ReadQuickChat(r, out byte qslot, out byte qpreset, out string qcustom);
                    if (IsHost)
                    {
                        // Authoritative: the sender's slot is who the packet came FROM (not the wire
                        // field, which a client can't be trusted to set). Anti-spam + re-censor here,
                        // then relay to everyone and render locally.
                        int authSlot = SlotOf(from);
                        if (authSlot < 0 || !QcAllow(authSlot)) break;
                        string safe = qpreset == 255 ? ChatCensor.Clean(qcustom) : "";
                        DeliverQuickChat(authSlot, qpreset, safe);
                        Transport.SendToAll(NetCodec.QuickChat((byte)authSlot, qpreset, safe), NetChannel.Reliable);
                    }
                    else
                    {
                        // Client: host already stamped the authoritative slot + censored the text.
                        DeliverQuickChat(qslot, qpreset, qcustom);
                    }
                    break;
                }
                case MsgType.ShootoutState: // client: latest set-pieces tally
                {
                    var so = NetCodec.ReadShootout(r);
                    LatestShootout = so;
                    ShootoutUpdated?.Invoke(so);
                    break;
                }
            }
        }

        // ---- host-side slot policy ----
        // The player cap the host chose (Host(maxPlayers)). It used to be passed to the transport
        // and silently discarded, so GrantSlot only checked occupancy and 8 humans could join a
        // 2v2 lobby. Defaults to the full board until Host() sets it.
        int _maxPlayers = MaxSlots;

        // Name + appearance last seen for a peer, even if it holds no slot. Lets a slot-less
        // (spectator) peer claim a free slot later and arrive as THEMSELVES; without it
        // ApplySlotRequest fell back to the local PlayerProfile, which on the host is the host's.
        readonly Dictionary<ulong, (string name, PlayerAppearance appr)> _peerIdentity
            = new Dictionary<ulong, (string, PlayerAppearance)>();

        /// <summary>
        /// Clear a slot's buffered input + its highest-applied tick. MUST be called whenever a slot
        /// changes hands: the host drops any input whose tick isn't newer than _slotInputTick[slot]
        /// (reorder protection), while every client's driver starts its own tick counter at 0. So a
        /// slot inheriting the previous occupant's high tick silently swallowed ALL of the new
        /// player's input - they were simply unresponsive, with no error anywhere.
        /// </summary>
        void ResetSlotInput(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return;
            _slotInput[slot] = default;
            _slotInputTick[slot] = 0;
        }

        /// <summary>
        /// Is this slot a real playable role in the CURRENT mode? This is the ONE gate: GrantSlot,
        /// ApplySlotRequest, HasFreeSlot and RebuildRoster all route through it, so a rule added
        /// here cannot be honoured by the join path and missed by the lobby's role picker.
        ///
        /// Two rules live here.
        ///
        /// CROSSER: slot 7 only gets a body in Striker mode. The set-piece / accuracy drivers skip
        /// it outright (NetSetPieceMatch.SpawnBody: `if (crosser) return;`), so a player granted it
        /// there is stranded with no body and no camera.
        ///
        /// SCRIMMAGE PER-SIDE CAP: the eight slots are two teams of four, and a shirt at or above
        /// perSide is not a player in this match. Nothing spawned a body for it
        /// (NetScrimmageMatch.SpawnBody already refused shirt >= perSide) and no formation row
        /// exists for it, yet nothing stopped a human being SEATED there: GrantSlot handed out the
        /// lowest free slot 1..6 with no per-side notion at all, so a default 3-a-side lobby
        /// admitted six humans and the fourth arrival took slot 3 - shirt 3 with perSide 3. Note
        /// that the crosser clause above no longer needs to name Scrimmage: slot 7 is Away shirt 3
        /// there, so the shirt rule already answers for it, and it answers BETTER (allowed at 4 a
        /// side, refused at 3, which is what the driver does).
        /// </summary>
        bool SlotAllowed(int slot)
        {
            if (slot < 0 || slot >= MaxSlots) return false;
            var mode = (GameMode)Config.mode;
            if (mode == GameMode.Scrimmage) return ScrimShirtOfSlot(slot) < ScrimPerSide;
            if (slot != CrosserSlot) return true;
            // The crosser is a claimable human role in Striker only, by decision - not because
            // other modes lack a body for it. NetSetPieceMatch.cs:221 does spawn one for slot 7, but
            // set pieces are a dead-ball drill (free kick / penalty), not a live-play feed, and a
            // human crosser role has no place in that. This USED to also allow SetPieces (a past
            // pass reasoned "the driver builds a body there, so the seat should be claimable" and
            // added it), but that reasoning proved wrong for what set pieces actually are, so it is
            // reverted here.
            //
            // Accuracy is excluded for the original reason: it runs NetAccuracyMatch, which has zero
            // crosser references (verified by grep), so there is no body for a claimer to become and
            // publishing the row would put back the click-and-nothing-happens this gate exists to
            // prevent.
            return mode == GameMode.Striker;
        }

        // Message types only the HOST ever authors. A client accepts these from the host peer only
        // (see RouteMessage); the host itself ignores them (its own handlers are IsHost-gated).
        static bool IsHostOnly(MsgType t)
            => t == MsgType.AssignSlot || t == MsgType.RosterSync || t == MsgType.StartMatch
               || t == MsgType.Snapshot || t == MsgType.MatchEvent || t == MsgType.BallKick
               || t == MsgType.PostHit
               || t == MsgType.ReplayStart || t == MsgType.ReplayEnd || t == MsgType.ShootoutState;

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
                // Slot becomes OPEN (or AI, if the host left AI on for it - see RebuildRoster).
                _slotOwner[slot] = PeerId.None; _slotName[slot] = null; _slotReady[slot] = false;
                _slotJerseyPng[slot] = null; _slotJerseyTex[slot] = null; _jerseyRx.Remove(slot);   // drop their kit
                ResetSlotInput(slot);
            }
            _peerIdentity.Remove(p.Value);
            // Drop their build with them, or the next occupant of that slot inherits it.
            { int ls = SlotOf(p); if (ls >= 0 && ls < MaxSlots) _slotPassMask[ls] = 0; }
            if (IsHost) PushRoster();
        }

        // Host: give a newly-hello'd client the lowest free SHOOTER slot (1..N); if none,
        // and slot 0 (keeper) is free, give them the keeper; else spectator. Then re-push
        // the full roster (+ config) so everyone, including the new joiner, is in sync.
        void GrantSlot(PeerId peer, string name, PlayerAppearance appearance, byte version)
        {
            // FIRST, before the peer is recorded or given anything. A build that does not share our
            // wire format cannot be handled by any later branch: it would read every struct at the
            // wrong offsets, so it does not fail, it plays a garbled match. Three separately
            // downloaded platform folders make this the likeliest join failure in practice, so it
            // gets its own reason code and its own message on the joiner's screen.
            if (version != NetCodec.ProtocolVersion)
            {
                Transport.Send(peer, NetCodec.AssignSlot(255, NetRole.Spectator, JoinRefusal.Version),
                               NetChannel.Reliable);
                Debug.LogWarning("NetSession: refused a joiner on protocol v" + version
                               + " (this build is v" + NetCodec.ProtocolVersion + ").");
                return;
            }

            // Remember who this peer is even if they end up slot-less, so that if they later claim a
            // free slot in the lobby they arrive under THEIR OWN name + look. Without this,
            // ApplySlotRequest's fallback reads PlayerProfile, which on the host is the HOST's
            // profile - a slot-less joiner would appear in the roster as a copy of the host.
            _peerIdentity[peer.Value] = (string.IsNullOrEmpty(name) ? "PLAYER" : name, appearance);

            // Match already running: lock it. The joiner gets no slot (spectator/255) - the match
            // drivers build their body set once at Configure, so there is nothing to slot into.
            // They can join the next lobby. (Full join-in-progress is a separate, larger feature.)
            if (MatchStarted)
            {
                Transport.Send(peer, NetCodec.AssignSlot(255, NetRole.Spectator, JoinRefusal.MatchRunning),
                               NetChannel.Reliable);
                // ALSO send them the roster/config. This used to return early, so a joiner who
                // arrived mid-match never received a RosterSync and sat in a blank lobby forever
                // with a default MatchConfig and no way out but Leave. With the roster in hand the
                // browser can see it was refused and report why.
                RebuildRoster();
                Transport.Send(peer, NetCodec.Roster(Config, Roster), NetChannel.Reliable);
                return;
            }
            int granted = -1;
            // Lowest free SHOOTER slot (1..MaxSlots-2), then keeper (0), then crosser
            // (MaxSlots-1). Players re-pick any free role in the lobby afterward.
            //
            // Every branch below is SlotAllowed-gated, so the scrimmage per-side cap is enforced by
            // the same search that already skipped the crosser. In a 3-a-side lobby that leaves the
            // allowed set {0,1,2,4,5,6} and this walk fills 1, 2, 4, 5, 6, then 0 - six seats
            // against a maxPlayers of six, so the two limits bite together instead of one silently
            // over-admitting. The order is unchanged and is still lopsided (Away's keeper fills
            // before Home's), which the lobby's role picker exists to fix.
            for (int s = 1; s < CrosserSlot; s++)
                if (!_slotOwner[s].IsValid && SlotAllowed(s)) { granted = s; break; }
            if (granted < 0 && !_slotOwner[0].IsValid && SlotAllowed(0)) granted = 0;
            // The crosser is only a real playable role in STRIKER mode; the set-piece/accuracy
            // drivers skip that slot entirely (no body, no camera, never in the shooter rotation),
            // so handing it out there stranded the player. SlotAllowed gates it by mode.
            if (granted < 0 && !_slotOwner[CrosserSlot].IsValid && SlotAllowed(CrosserSlot)) granted = CrosserSlot;
            // Respect the host's player cap (Host(maxPlayers)); it used to be accepted and dropped,
            // so 8 humans could pile into a 2v2 lobby.
            if (granted >= 0 && HumanCount() >= _maxPlayers) granted = -1;

            NetRole role = granted < 0 ? NetRole.Spectator : RoleForSlot(granted);
            if (granted >= 0)
            {
                _slotOwner[granted] = peer;
                _slotName[granted] = string.IsNullOrEmpty(name) ? "PLAYER" : name;
                _slotAppearance[granted] = appearance;
                ResetSlotInput(granted);   // a reused slot must not keep the old occupant's tick
            }
            Transport.Send(peer, NetCodec.AssignSlot((byte)(granted < 0 ? 255 : granted), role,
                                                     granted < 0 ? JoinRefusal.NoSlot : JoinRefusal.None),
                           NetChannel.Reliable);
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
            if (!SlotAllowed(target)) return;                // e.g. crosser in a mode with no crosser body
            int cur = SlotOf(peer);
            // Identity comes from the slot they're moving FROM; a slot-less (spectator) requester
            // falls back to what their Hello told us, and only then to the local profile. Reading
            // PlayerProfile directly used to make a slot-less claimer show up as a copy of the HOST.
            bool known = _peerIdentity.TryGetValue(peer.Value, out var ident);
            string name = cur >= 0 ? _slotName[cur] : (known ? ident.name : PlayerProfile.PlayerName);
            PlayerAppearance appr = cur >= 0 ? _slotAppearance[cur]
                                             : (known ? ident.appr : PlayerProfile.Appearance);
            byte[] jersey = cur >= 0 ? _slotJerseyPng[cur] : null;   // move the kit with the player
            if (cur >= 0)
            {
                _slotOwner[cur] = PeerId.None; _slotName[cur] = null; _slotReady[cur] = false;
                _slotJerseyPng[cur] = null; _slotJerseyTex[cur] = null;
                ResetSlotInput(cur);          // the slot they left must not keep their tick history
            }
            _slotOwner[target] = peer;
            _slotName[target] = string.IsNullOrEmpty(name) ? "PLAYER" : name;
            _slotAppearance[target] = appr;   // move the player's look with them
            _slotJerseyPng[target] = jersey; _slotJerseyTex[target] = null;
            _slotReady[target] = false;
            // ...and the slot they moved INTO must not keep the previous occupant's tick, or the
            // host would drop every input this player sends (see ResetSlotInput).
            ResetSlotInput(target);
            // Tell the mover their new slot/role (host updates its own LocalSlot directly).
            if (peer.Equals(Transport.LocalPeer)) AssignLocal(target, RoleForSlot(target));
            else Transport.Send(peer, NetCodec.AssignSlot((byte)target, RoleForSlot(target)), NetChannel.Reliable);
            PushRoster();
            // Re-broadcast the moved kit at its new slot so every peer re-keys it (the roster row
            // only carries the small appearance struct, not the jersey).
            if (jersey != null && jersey.Length > 0) BroadcastJersey((byte)target, jersey);
        }

        /// <summary>
        /// True when the host refused us a player slot: the lobby was full, or a match was already
        /// running when we joined (GrantSlot sends AssignSlot(255, Spectator) for both). There is no
        /// spectator implementation, and the match drivers CLAMP LocalSlot into 0..MaxSlots-1 - so
        /// without checking this a refused joiner silently ends up sharing slot 0's body and camera.
        /// The UI reads this and bounces them out with a reason instead.
        /// </summary>
        public bool SlotRefused => LocalSlot < 0 || LocalSlot >= MaxSlots;

        /// <summary>
        /// True once the host has actually ANSWERED our join with an AssignSlot, whether it granted a
        /// slot or refused one. SlotRefused alone CANNOT be used to detect a refusal on a client,
        /// because a brand-new session already reads as refused: the field initialisers are
        /// LocalSlot = -1 and LocalRole = Spectator, which is bit-for-bit the state the host's
        /// AssignSlot(255, Spectator) refusal produces. Anything watching a join in progress has to
        /// gate on this flag, or it concludes the host refused us one frame after we pressed Join,
        /// before a single packet could possibly have made the round trip.
        /// </summary>
        public bool SlotAnswered { get; private set; }

        /// <summary>
        /// Why the host refused us a slot, when SlotRefused is true. Only meaningful once
        /// SlotAnswered is set. The UI reads this so it can name the actual cause: a full lobby, a
        /// match already in progress and a version mismatch are three different things for the
        /// player to do something about, and they used to all print as "no free slot".
        /// </summary>
        /// <summary>
        /// One slot's passing multipliers and Maestro, DERIVED on the host from the node mask that slot
        /// sent. Same SkillTree tables the owner's own client evaluates, so there is one source of truth
        /// rather than a second set of numbers that could drift from it.
        ///
        /// A slot that has sent nothing (an AI slot, a slot whose loadout has not arrived yet, a slot
        /// someone just left) reads as UNINVESTED. That is the floor, not a gift: the previous code
        /// handed every networked player 1.5 accuracy they had not bought, which also deleted the stat
        /// for anyone who HAD maxed it, since Accuracy01 clamps at 1.
        /// </summary>
        public void PassStatsForSlot(int slot, out float powerMul, out float accMul, out bool maestro)
        {
            powerMul = 1f; accMul = 1f; maestro = false;
            if (slot < 0 || slot >= MaxSlots) return;
            if (!SkillTree.TryUnpackPassing(_slotPassMask[slot], out var owned)) return;
            powerMul = SkillTree.Mul("passpower", owned);
            accMul   = SkillTree.Mul("passacc", owned);
            maestro  = SkillTree.HasPerk("maestro", owned);
        }

        public JoinRefusal RefusedBecause { get; private set; }

        // why defaults to None: every caller other than the AssignSlot handler is a GRANT
        // (the host seating itself, or a lobby role swap), and a grant has no refusal reason.
        void AssignLocal(int slot, NetRole role, JoinRefusal why = JoinRefusal.None)
        {
            // Keep the raw value (255 = refused/spectator) rather than clamping it here: callers
            // need to be able to tell "no slot" apart from "slot 0". Anything outside the table is
            // surfaced through SlotRefused above.
            LocalSlot = slot; LocalRole = role;
            RefusedBecause = why;
            SlotAnswered = true;
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
                // A slot this MODE has no seat for is left OUT of the roster, not published as
                // "Open". Publishing it drew a row with a Claim button that ApplySlotRequest then
                // refused in silence, which is the worst of the three possible behaviours: the
                // player clicks and nothing whatsoever happens. Omission is safe because every
                // consumer already copes with a missing row - RosterSlot returns default (no human,
                // no AI, so no body is spawned) and the drivers iterate what is actually there -
                // and it retires the same defect for the crosser row in set pieces and accuracy,
                // which was already unclaimable before the scrimmage cap added five more of them
                // to a 3-a-side lobby. A slot a human still holds is ALWAYS published, so no
                // config change can make a seated player invisible.
                if (!human && !SlotAllowed(i)) continue;
                bool ai = !human && _slotAi[i];  // a non-human slot the host left AI-on
                string name;
                if (human) name = _slotName[i] ?? "PLAYER";
                else if (ai) name = "Clanker " + (++clanker);
                else name = "Open";              // unfilled, AI toggled off
                // Humans carry their synced look; AI/open slots get default appearance.
                var appr = human ? _slotAppearance[i] : PlayerAppearance.Default;
                list.Add(new LobbySlot { slot = (byte)i, human = human, ai = ai, ready = _slotReady[i],
                                         role = (byte)RoleForSlot(i), name = name, appearance = appr });
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
