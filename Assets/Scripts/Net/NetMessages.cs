using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Wire message types + a compact binary reader/writer. Host-authoritative flow:
    ///
    ///   client -> host : Hello (name), PlayerInput (per tick)
    ///   host -> client : AssignSlot (your slot + role), Snapshot (all bodies + ball),
    ///                    MatchEvent (goal/score/kickoff/full-time)
    ///
    /// Keep payloads small and versioned by the leading MsgType byte. Ragdoll snapshots
    /// send per-body pelvis pose only (position + yaw); the client interpolates the visible
    /// ragdoll toward it (host owns the true physics).
    /// </summary>
    public enum MsgType : byte
    {
        Hello = 1,        // client -> host
        AssignSlot = 2,   // host -> client
        PlayerInput = 3,  // client -> host
        Snapshot = 4,     // host -> clients
        MatchEvent = 5,   // host -> clients
        RosterSync = 6,   // host -> clients: full lobby roster + match config
        ReadyToggle = 7,  // client -> host: my ready state changed
        StartMatch = 8,   // host -> clients: build the match now
        ReplayStart = 9,  // host -> clients: begin the post-goal replay
        SkipVote = 10,    // client -> host: I clicked to skip the replay
        ReplayEnd = 11,   // host -> clients: end the replay (all skipped or finished)
        RequestSlot = 12, // client -> host: I want to claim this slot (role pick)
        ShootoutState = 13, // host -> clients: set-pieces active shooter + per-slot scores
        UpdateLoadout = 14, // client -> host: my appearance changed (re-customized in the lobby)
        JerseyChunk = 15,   // client<->host: one chunk of a slot's painted-jersey PNG (too big to inline)
        BallKick = 16,      // host -> clients: the ball was struck at a world position (3D kick SFX)
        QuickChat = 17,     // client -> host request, then host -> clients relay: a quickchat message
        PostHit = 18,       // host -> clients: the ball hit the woodwork at a world position + speed
        MatchStats = 19,    // host -> clients, once at full time: the post-match per-player table
        NominateJersey = 20, // client -> host: toggle MY slot's jersey as a candidate for my team
        CastJerseyVote = 21, // client -> host: vote for a candidate slot (255 = clear my vote)
        CrosserSetup = 22,   // client -> host request, then host -> clients relay: the AI crosser panel
        // ---- Trickshot Cup (design 9.4). Host-authoritative; see CupNet / CupDirector.Net ----
        CupState = 23,       // host -> clients, reliable, HOST-ONLY: the whole cup read model (phase, players, results)
        CupRequest = 24,     // client -> host, reliable: an intent (pick, ready, spectate, a round result, a coin call...)
        CupStream = 25,      // client -> host -> the spectators of that slot, unreliable, RELAYED: a spectated round's view
        CupRoundState = 26,  // host -> clients, reliable, HOST-ONLY: the host-simulated round's CupRoundState record
    }

    /// <summary>
    /// The AI crosser's shared setup: where crosses land, where the crosser stands, how he delivers,
    /// the two serve sliders, and the AI's display name. Striker mode's cross map (M) edits this.
    ///
    /// Replicated even though ONLY THE HOST SIMULATES the crosser, because the panel itself has to
    /// tell the truth on every screen: any player may open the map and edit it, so a client showing
    /// its own stale defaults while someone else's edits were live is exactly the confusion this
    /// message exists to remove. The host is still the only peer that acts on the values.
    ///
    /// Positions are x/z only - the panel places on a flat pitch map and the y is derived (ball
    /// radius for a target, 0 for the crosser's feet), so sending it would be two wasted floats and
    /// a chance for the two ends to disagree about a number neither of them chose.
    /// </summary>
    public struct CrosserSetupMsg
    {
        public float targetX, targetZ;   // where crosses land
        public float spotX, spotZ;       // where the AI crosser stands
        public byte delivery;            // Crosser.DeliveryType
        public float ballSpeed;          // -> SimConfig.BallSpeedMul
        public float crossInterval;      // multiplier -> SimConfig.ServeInterval
        public string aiName;            // the AI crosser's name (host-renamable; "Clanker" default)
    }

    /// <summary>
    /// One player's post-match line, as it crosses the wire. Every counter is a BYTE, and the host
    /// clamps each one to that range BEFORE it computes the rating, so the number a client draws and
    /// the rating beside it were derived from the same values - rating off the raw count and displaying
    /// a wrapped byte would have had the two disagree.
    ///
    /// No NAME is sent. A human row carries its slot and the client resolves the roster name it already
    /// has; an AI row (slot 255) is named from its team and shirt, which both peers derive identically.
    /// Strings on a per-player table would be the biggest thing in the message and buy nothing.
    /// </summary>
    public struct StatRow
    {
        public byte slot;      // 255 = AI, no roster entry
        public byte team;      // 0 = Home, 1 = Away
        public byte shirt;     // 0 = keeper, 1.. = outfield
        public byte flags;     // 1 = keeper, 2 = was net-controlled, 4 = man of the match
        public byte goals, assists, shots, passes, passesDone, tackles, saves;
        public byte rat10;     // rating x 10, so 60..100 fits a byte
    }

    // One chunk of a slot's painted-jersey PNG. Jerseys are far too big for the roster row (which
    // is small + resent often), so they ride this dedicated chunked side-channel keyed by slot:
    // client -> host on join / re-customize, host -> all peers on completion. Reassembled by slot.
    public struct JerseyChunkMsg
    {
        public byte slot;        // which player slot this jersey belongs to
        public uint index;       // 0-based chunk index
        public uint total;       // total chunk count for this transfer
        public uint totalBytes;  // full PNG length (for the reassembly buffer + completion check)
        public byte[] chunk;     // this chunk's bytes
    }

    /// <summary>
    /// Extra, non-playing roles a Match host can advertise wanting, so a stranger browsing for
    /// sessions can find a lobby that actually needs what they want to do. A bitmask rather than an
    /// enum value: a host commonly wants more than one at once.
    ///
    /// These are ADVERTISEMENTS ONLY at this point - nothing here seats a player into the role or
    /// changes how a match plays. The roles themselves are not hooked up yet; this is the discovery
    /// half, so that when they are, lobbies are already findable by them.
    /// </summary>
    [System.Flags]
    public enum LookingRole : byte
    {
        None      = 0,
        Sniper    = 1 << 0,
        Referee   = 1 << 1,
        Cameraman = 1 << 2,
    }

    /// <summary>Display/tag names for LookingRole, in bit order - shared by the host setup
    /// checkboxes, the browser filter and ModeLabel's advert tag so the three can never drift.</summary>
    public static class LookingRoles
    {
        public static readonly LookingRole[] All = { LookingRole.Sniper, LookingRole.Referee, LookingRole.Cameraman };
        public static readonly string[] Names = { "Sniper", "Referee", "Cameraman" };
        // One letter per role for the compact ModeLabel tag (see NetSession.ModeLabel).
        public static readonly char[] Tags = { 'S', 'R', 'C' };

        /// <summary>The "[LF:SRC]" advert tag for a mask, or "" when the host wants nobody.
        /// Appended to the mode string the discovery probe already carries.</summary>
        public static string Tag(byte mask)
        {
            if (mask == 0) return "";
            var sb = new System.Text.StringBuilder(" [LF:");
            for (int i = 0; i < All.Length; i++)
                if ((mask & (byte)All[i]) != 0) sb.Append(Tags[i]);
            return sb.Append(']').ToString();
        }

        /// <summary>Read back the mask a Tag() encoded into a browser row's mode string. Unknown
        /// or absent tag = 0, so a row from a peer that never set one simply matches nothing.</summary>
        public static byte Parse(string modeLabel)
        {
            if (string.IsNullOrEmpty(modeLabel)) return 0;
            int i = modeLabel.IndexOf("[LF:", System.StringComparison.Ordinal);
            if (i < 0) return 0;
            int end = modeLabel.IndexOf(']', i);
            if (end < 0) return 0;
            byte mask = 0;
            for (int k = i + 4; k < end; k++)
                for (int r = 0; r < Tags.Length; r++)
                    if (modeLabel[k] == Tags[r]) mask |= (byte)All[r];
            return mask;
        }
    }

    // The host's chosen match configuration, synced to all peers so everyone builds the
    // same arena/mode. Mirrors the relevant SimConfig fields.
    public struct MatchConfig
    {
        public byte mode;        // GameMode as byte
        public byte stadium;     // StadiumStyle index
        public byte perSide;     // match team size
        public ushort matchSec;  // match length (seconds)
        public bool publicLobby; // visibility (host-only meaning; carried for display)
        public float goalScale;    // set pieces: goal size multiplier (1 = regulation)
        public float keeperAbility; // set pieces: AI keeper strength 0..1
        // Set pieces: host-placed free-kick spot + wall centre (world x/z), and whether the
        // host actually placed them (else the driver uses its centred defaults).
        public bool fkPlaced;
        public float fkBallX, fkBallZ, fkWallX, fkWallZ;
        // Accuracy mode: the FORMAT, and nothing else. Accuracy is a three-strikes run at a single
        // patrolling target whose difficulty comes from the round number (see SimConfig.AccuracyTier),
        // so there is no wall, target count, kick count or turn timer left to configure.
        //   false = STRIKES      - each shooter plays their whole run out, then the next takes over.
        //   true  = SUDDEN DEATH - shooters cycle one shot at a time until one is left standing.
        // This is the old accTurnByTime slot reused in place, so the packet layout is byte-for-byte
        // what it was; the four retired slots around it are still written and read as zero.
        public bool accSuddenDeath;
        // Set pieces RANDOM mode: when true, every shooter shoots from a NEW random outside-box spot
        // each of the 10 rounds - the same spot for all shooters in a round, changing 10 times. The
        // seed is host-chosen and carried here so every peer derives the identical 10-spot schedule.
        public bool fkRandom;
        public uint fkSeed;
        // Match only: the extra roles this host is LOOKING FOR, as a bitmask (see LookingRole).
        // A public lobby advertises them through ModeLabel(), which appends a compact tag to the
        // mode string the discovery probe already carries - so the browser can filter on them with
        // no change to the probe/browse wire format at all. 0 = not looking for anyone.
        public byte lookingFor;
        // Goal HEIGHT multiplier, separate from goalScale (which is now the WIDTH). The host's goal
        // editor sizes the two independently. 0 = not set: the reader keeps the goal in proportion.
        public float goalScaleH;
        // Trickshot Cup only: the play STYLE (CupStyle as a byte: 1 = Head to Head, 2 = Co-op;
        // 0 = Solo is never hosted) and the FORMAT (CupFormat: 0 = Penalties, 1 = Free Kicks).
        // The cup's seed is fkSeed, which already rides above. Both are written after goalScaleH
        // and read UNCONDITIONALLY - the slot list follows the config, so `r.More` cannot tell a
        // trailing field from the slot count - which is why they cost the v8 protocol bump.
        public byte cupStyle;
        public byte cupFormat;
    }

    // Host -> clients: the set-pieces shootout tally. activeShooter = slot currently up;
    // scored/taken are indexed by slot. Sent reliably on every change (goal, turn, end).
    public struct ShootoutState
    {
        public byte activeShooter;   // slot index of the shooter up now (255 = none / match over)
        public bool over;            // match finished
        public byte[] scored;        // per-slot goals (length MaxSlots)
        public byte[] taken;         // per-slot attempts (length MaxSlots)
    }

    // ==============================================================================================
    // Trickshot Cup messages (design 9.4). The cup's PURE records (CupBracket, CupRound,
    // CupRoundState) serialise themselves through CupByteWriter; these structs are the thin wire
    // shells the session routes, so the session never has to understand the cup. Encoded and
    // decoded by NetCodec.CupState / CupRequest / CupStream / CupRoundState below; built from and
    // applied to the director by CupNet / CupDirector.Net (Assets/Scripts/Cup).
    // ==============================================================================================

    /// <summary>Bits of <see cref="CupPlayerRow.status"/>.</summary>
    public static class CupPlayerStatus
    {
        public const byte Ready = 1;
        public const byte Out = 2;
        public const byte ReplacedByAi = 4;
        public const byte Left = 8;
        public const byte Loaded = 16;
        public const byte Playing = 32;
        /// <summary>Somebody is spectating this player right now: the owner of a local round must stream it.</summary>
        public const byte Spectated = 64;
    }

    /// <summary>Bits of <see cref="CupPlayerRow.coin"/>: this round's call and its verdict.</summary>
    public static class CupCoinBits
    {
        public const byte HasCall = 1;
        public const byte CallTails = 2;
        public const byte HasVerdict = 4;
        public const byte Right = 8;
    }

    /// <summary>
    /// One human in the cup as the host sees them (CupPlayer's replicated fields). Nation
    /// indices are CupNations table indices (-1 = none). `entrant` is the player's bracket
    /// entrant (-1 before the draw) - it is what lets a client rebuild the SAME draw the host
    /// built, whoever has left since (the draw is a pure function of the seed and the entrant
    /// humans' nations and slots, so only those facts cross the wire; the tree itself never does).
    /// </summary>
    public struct CupPlayerRow
    {
        public byte slot;
        public int nation;          // -1 none (0xFFFF on the wire)
        public int entrant;         // -1 before the draw
        public byte status;         // CupPlayerStatus bits
        public byte spectating;     // the slot being watched, 255 = none
        public int liveOpponent;    // live row: the opponent nation while Playing (-1 none)
        public byte liveFor, liveAgainst, liveKick;
        public byte coin;           // CupCoinBits
        public byte coinMade, coinRight;   // cup tallies (career + the Co-op calls band)
    }

    /// <summary>
    /// One DONE round of the bracket. A round the host SIMULATED (AI vs AI, or a leaver's) is
    /// seed-derivable, so it carries only its identity and the client re-runs CupSim on its own
    /// copy of the draw; a round humans PLAYED carries its real kick line (2 bits a kick).
    /// </summary>
    public struct CupResultRow
    {
        public byte stage;          // CupStage
        public byte index;          // round index within the stage
        public bool simulated;
        public bool suddenDeath;    // played rounds only
        public byte firstKicker;    // 0 = A, 1 = B (played rounds only)
        public byte scoreA, scoreB; // played rounds only
        public byte[] kicks;        // played rounds only: one KickRecord nibble per entry (KickRecord.ToNibble)
    }

    /// <summary>
    /// The whole cup as the host sees it (the read model of CupDirector), sent RELIABLE on every
    /// change, coalesced to at most 10 a second. A client applies it wholesale; nothing in it is
    /// seed-derivable (the draw, the simulated results, the free-kick spots, the coin faces and
    /// the dejection variants are all rebuilt from `seed` on every peer).
    /// </summary>
    public struct CupStateMsg
    {
        public byte version;        // CupNet.StateVersion
        public uint tick;           // the host director's monotonic tick when captured (a stale state is dropped)
        public byte phase;          // CupPhase
        public uint phaseSerial;    // the host's PhaseSerial: a client re-enters a phase whenever it changes
        public float phaseTime;     // seconds the host has spent in the phase
        public byte stage;          // CupStage in progress
        public byte style;          // CupStyle
        public byte format;         // CupFormat
        public uint seed;           // the cup seed; a NEW seed means Play Again (back to the nation pick)
        public byte captainSlot;
        public int teamNation;      // Co-op: the settled nation, -1 until then
        public byte leverPulls;     // Co-op: lever pulls this stage (the permutation stream)
        public bool hasBracket;     // the draw has been made
        public byte currentRound;   // the round the HOST is running: stage << 5 | index; 255 = none. Clients StartRound the same one.
        public CupPlayerRow[] players;
        public byte[] order;        // Co-op shooting order: slot per order index (index 0 = keeper); empty until set
        public CupResultRow[] results;   // every Done round, in stage then index order
        public uint bracketHash;    // FNV-1a over the entrants' (nation, slot) pairs; 0 without a draw. A client checks its rebuilt draw against it.
    }

    /// <summary>A client's intent for the host (CupRequestKind as the kind byte). `arg` is the kind's integer argument; `payload` its blob (a round record, an order, a live row).</summary>
    public struct CupRequestMsg
    {
        public byte kind;
        public int arg;
        public byte[] payload;      // may be null / empty
    }

    /// <summary>Bits of <see cref="CupStreamBody.flags"/>.</summary>
    public static class CupStreamBodyFlags
    {
        public const byte SideB = 1;
        public const byte KeeperBody = 2;   // gloved
        public const byte Referee = 4;
    }

    /// <summary>
    /// One body of a spectated round: the pose subset of BodyState (pelvis ground position, facing
    /// yaw, emote id + quantised phase) plus the facts a spectator needs to BUILD a puppet for it -
    /// which side it is on (its nation kit), whether it wears gloves, whether it is the referee, and
    /// the human slot whose look it wears (255 = an AI body).
    /// </summary>
    public struct CupStreamBody
    {
        public byte vslot;          // the round's virtual body id (CupBody.VirtualSlot)
        public byte slot;           // the human slot whose look it wears; 255 = AI / referee
        public byte flags;          // CupStreamBodyFlags
        public Vector3 pos;
        public float yaw;
        public byte emoteId;        // 255 none
        public byte emotePhase;     // 0..255
    }

    /// <summary>
    /// A spectated player's exact view (design 4, "Spectate"): their camera pose, the ball and
    /// their round's bodies, streamed at 20 Hz UNRELIABLE from the round's owner to the host,
    /// which relays it only to the slots spectating `fromSlot`. The host validates that
    /// `fromSlot` is the sender's own slot before relaying; a client accepts it only from the host.
    /// </summary>
    public struct CupStreamMsg
    {
        public byte fromSlot;
        public uint seq;            // the sender's frame counter; a stale message is dropped
        public Vector3 camPos;
        public Quaternion camRot;
        public float camFov;
        public Vector3 ballPos;
        public int nationA, nationB;   // the round's two nations (kits), -1 unknown
        public CupStreamBody[] bodies; // may be empty (a participant of a HOST-simulated round streams only its camera)
    }

    // One lobby row in the roster (host -> clients each change).
    public struct LobbySlot
    {
        public byte slot;
        public bool human;       // a person holds this slot
        public bool ai;          // an AI ("Clanker") holds this slot (host-toggled; false = open)
        public bool ready;
        public byte role;        // NetRole for this slot (so clients label rows by role)
        public string name;
        public PlayerAppearance appearance;   // this player's look (skin + head cosmetics)
        // Jersey vote (Match only, but carried for every slot so the wire format stays one shape).
        // nominated: this slot's own painted jersey is a candidate for its team. voteFor: which
        // candidate slot this slot voted for (255 = no vote). Every peer - host and clients alike -
        // derives the same winner from these two fields off its own Roster; see
        // NetSession.JerseyWinnerSlot. Appended last so the existing field order stays untouched.
        public bool nominated;
        public byte voteFor;
    }

    // Entrant = every seat in a Trickshot Cup lobby (a cup has no keeper / crosser seats: each
    // human is a nation, and the roles inside a round come from the cup itself). APPEND ONLY - the
    // value rides the wire in AssignSlot and every roster row.
    public enum NetRole : byte { Shooter = 0, Keeper = 1, Spectator = 2, Crosser = 3, Entrant = 4 }

    /// <summary>
    /// Why the host gave a joiner no slot. Rides along on AssignSlot so the joining client can say
    /// what actually happened instead of guessing. Before this, all three refusals rendered as
    /// "no free slot", which is a lie in two of the three cases and sends the player off to hunt
    /// for imaginary lobby space.
    /// </summary>
    public enum JoinRefusal : byte
    {
        None = 0,           // a slot was granted
        NoSlot = 1,         // lobby full, or the host's player cap is reached
        MatchRunning = 2,   // the host is mid-match; join the next lobby
        Version = 3,        // the two builds do not speak the same protocol
    }

    // One player's per-tick intent, sampled from GameInput and sent to the host.
    public struct InputFrame
    {
        public uint tick;
        public Vector2 move;      // wasd
        public float lookYaw;     // desired facing yaw (camera yaw)
        public float lookPitch;   // camera pitch (deg): set-piece vertical aim comes from this
        public bool jump, legL, legR, sprint, passGround, passLofted, tackle, reset;
        public bool closeControl;   // dribble close-control modifier (second bit byte)
        public bool passChip;       // chip pass button (second bit byte, bit 2)
        public bool cross;          // set up a cross, Enter (second bit byte, bit 3)
        public bool thirdLeg;       // adult mode: appendage to attention while held (second bit byte, bit 4)
        public byte emoteId;      // 255 = none; else Celebration.Emote to start this tick
    }

    // Animation state a body is in, synced so clients play the matching canned local animation on
    // the interpolated puppet (instead of a rigid stance). Discrete state, not streamed poses.
    // KickL is the left-footed swing: footedness is authored per player, so a left-footed crosser's
    // puppet has to swing the leg he actually kicks with on every other screen, not the right one.
    public enum AnimState : byte { Idle = 0, Run = 1, Jump = 2, Dive = 3, Down = 4, Kick = 5, Sit = 6, KickL = 7 }

    // One body's state in a snapshot (host -> clients). Compact: pos + yaw + flags.
    public struct BodyState
    {
        public byte slot;         // which player slot (or 255 = ball)
        public Vector3 pos;
        public float yaw;
        public bool down;         // knocked over
        public byte emoteId;      // 255 = none; else the emote this body is currently playing
        public byte emotePhase;   // 0..255 quantized 0..1 progress of that emote
        public byte anim;         // AnimState the body is in (drives the client-side canned anim)
        public uint lastInputTick; // host: the highest input tick applied for this slot (client
                                   // reads its OWN slot's value to reconcile its predicted body)
        public bool erect;        // adult mode: the body's appendage is standing to attention (the
                                  // client mirrors it onto the puppet's AnatomySim; no-op without one)
    }

    public struct Snapshot
    {
        public uint tick;
        public Vector3 ballPos;
        public Vector3 ballVel;
        // Is the ball inside a shot's goal-assist window? Replicated because no ballistic solve can
        // predict a steered ball (ApplyGoalAssist runs up to AssistMaxAccel for AssistDuration), so the
        // match landing telegraph has to HIDE for it rather than point somewhere wrong. Written
        // LAST on the wire, not here - see Snap.
        public bool guided;
        public byte homeScore, awayScore;
        public ushort clockSec;   // match seconds remaining (match mode); 0 in modes with no clock
        public BodyState[] bodies;
    }

    // Compact big-endian-free binary writer/reader over a MemoryStream.
    //
    // The stream + BinaryWriter are SHARED across messages instead of allocated per message: a
    // client encodes an input every rendered frame and the host a snapshot 20 times a second, and
    // two garbage objects per message was a steady trickle of GC at match rate. Encoding is
    // main-thread and never nested (every NetCodec method writes one message and returns it), so
    // one shared pair is enough; a nested/foreign use just falls back to its own private pair.
    // ToArray still hands out a fresh byte[] - the transport keeps reliable packets for resends.
    public class NetWriter
    {
        static readonly MemoryStream s_ms = new MemoryStream(256);
        static readonly BinaryWriter s_bw = new BinaryWriter(s_ms);
        static bool s_busy;

        readonly MemoryStream _ms;
        readonly BinaryWriter _bw;
        readonly bool _shared;
        public NetWriter(MsgType type)
        {
            if (!s_busy) { s_busy = true; _shared = true; _ms = s_ms; _bw = s_bw; _ms.SetLength(0); }
            else { _ms = new MemoryStream(64); _bw = new BinaryWriter(_ms); }
            _bw.Write((byte)type);
        }
        public void U8(byte v) => _bw.Write(v);
        public void U32(uint v) => _bw.Write(v);
        public void F(float v) => _bw.Write(v);
        public void B(bool v) => _bw.Write(v);
        public void Str(string s) => _bw.Write(s ?? "");
        public void V3(Vector3 v) { _bw.Write(v.x); _bw.Write(v.y); _bw.Write(v.z); }
        public void V2(Vector2 v) { _bw.Write(v.x); _bw.Write(v.y); }
        // Length-prefixed raw byte blob (used for jersey PNG chunks). U32 length then the bytes.
        public void Bytes(byte[] v)
        {
            int n = v?.Length ?? 0;
            _bw.Write((uint)n);
            if (n > 0) _bw.Write(v);
        }
        // Colour packed as 3 bytes RGB (alpha is always opaque for appearance/kit colours).
        public void Col(Color c)
        {
            _bw.Write((byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255));
            _bw.Write((byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255));
            _bw.Write((byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255));
        }
        public byte[] ToArray()
        {
            _bw.Flush();
            var arr = _ms.ToArray();
            if (_shared) { _ms.SetLength(0); s_busy = false; }   // release the shared pair
            return arr;
        }
    }

    public class NetReader
    {
        readonly BinaryReader _br;
        public MsgType Type { get; }
        public NetReader(byte[] data)
        {
            _br = new BinaryReader(new MemoryStream(data));
            Type = (MsgType)_br.ReadByte();
        }
        public byte U8() => _br.ReadByte();
        public uint U32() => _br.ReadUInt32();
        public float F() => _br.ReadSingle();
        public bool B() => _br.ReadBoolean();
        public string Str() => _br.ReadString();
        public Vector3 V3() => new Vector3(_br.ReadSingle(), _br.ReadSingle(), _br.ReadSingle());
        public Vector2 V2() => new Vector2(_br.ReadSingle(), _br.ReadSingle());
        public byte[] Bytes() { int n = (int)_br.ReadUInt32(); return n > 0 ? _br.ReadBytes(n) : System.Array.Empty<byte>(); }
        /// <summary>
        /// Is there at least one unread byte left? Lets a handler read a field that an older build
        /// did not send without throwing, which is what makes a version mismatch reportable instead
        /// of just being a malformed packet the session quietly discards.
        /// </summary>
        public bool More => _br.BaseStream.Position < _br.BaseStream.Length;
        public Color Col() { float r = _br.ReadByte() / 255f, g = _br.ReadByte() / 255f, b = _br.ReadByte() / 255f; return new Color(r, g, b, 1f); }
    }

    // Encode/decode helpers so the session code stays readable.
    public static class NetCodec
    {
        // Shared appearance pack/unpack (same field order both ways). 3 ints as bytes (styles
        // are small) + 4 colours as 3 bytes each = ~15 bytes.
        public static void WriteAppearance(NetWriter w, PlayerAppearance a)
        {
            w.Col(a.Skin);
            w.U8((byte)Mathf.Clamp(a.HairStyle, 0, 255));   w.Col(a.HairColor);
            w.U8((byte)Mathf.Clamp(a.FacialStyle, 0, 255)); w.Col(a.FacialColor);
            w.U8((byte)Mathf.Clamp(a.Accessory, 0, 255));   w.Col(a.AccessoryColor);
            w.B(a.Adult);   // appended so the field order stays consistent both ways
            // Third Leg size multipliers (only meaningful when Adult). Appended after Adult.
            w.F(a.MemberLen); w.F(a.MemberGirth); w.F(a.BallSize);
            // Species, appended LAST. The three style indices and their colours above are
            // reinterpreted against this species (a horse's HairStyle is its mane), so a peer must
            // read the species before it can make sense of them. See PlayerAppearance.
            w.U8(a.SpeciesId);
        }
        public static PlayerAppearance ReadAppearance(NetReader r)
        {
            var a = new PlayerAppearance();
            a.Skin = r.Col();
            a.HairStyle = r.U8();   a.HairColor = r.Col();
            a.FacialStyle = r.U8(); a.FacialColor = r.Col();
            a.Accessory = r.U8();   a.AccessoryColor = r.Col();
            a.Adult = r.B();
            a.MemberLen = r.F(); a.MemberGirth = r.F(); a.BallSize = r.F();
            a.SpeciesId = r.U8();
            return a;
        }

        // Hello now carries the joining player's name AND appearance so the host can store it
        // per slot and broadcast it on the roster (remote players show each other's look).
        /// <summary>
        /// Bumped whenever anything on the wire changes shape: a new field, a reordered struct, a
        /// different meaning for an existing byte. Three separately downloaded platform builds make
        /// "one player still has yesterday's copy" the single likeliest reason two people cannot
        /// play, and a mismatch with no check is not a clean failure - it is a client reading a
        /// struct at the wrong offsets, so bodies teleport and the ball is somewhere else. The host
        /// compares this at Hello and refuses with JoinRefusal.Version instead.
        /// </summary>
        public const byte ProtocolVersion = 8;   // 8: GameMode.TrickshotCup + MatchConfig.cupStyle/cupFormat (read unconditionally), NetRole.Entrant
                                                 // 7: BodyState carries the adult-mode erect flag (fixed-stride record grew)
                                                 // 6: PlayerInput carries a bundle of frames; ReliableBulk stream
                                                 // 5: MatchConfig.goalScaleH, InputFrame.cross, player flags

        // Trailing per-player flags on Hello / Loadout (after the version / passing mask). Footedness
        // was never on the wire, so the host animated every REMOTE kicker with the HOST'S foot; a
        // crosser's whole run-up and swing depend on it, so it goes across as one bit.
        public const byte FlagLeftFooted = 1;
        public static byte PlayerFlags() => PlayerProfile.LeftFooted ? FlagLeftFooted : (byte)0;

        public static byte[] Hello(string name, PlayerAppearance appearance)
        {
            var w = new NetWriter(MsgType.Hello); w.Str(name); WriteAppearance(w, appearance);
            // LAST, not first: appending means a Hello from a build that predates the field still
            // parses up to this point, so the host can identify the mismatch (absent = version 0)
            // rather than failing to read the message at all.
            w.U8(ProtocolVersion);
            w.U8(PlayerFlags());   // trailing, after the version, so the version gate is unmoved
            return w.ToArray();
        }

        public static byte[] AssignSlot(byte slot, NetRole role, JoinRefusal why = JoinRefusal.None)
        {
            var w = new NetWriter(MsgType.AssignSlot); w.U8(slot); w.U8((byte)role);
            w.U8((byte)why);   // trailing, for the same reason as Hello's version byte
            return w.ToArray();
        }

        // A client's input packet carries its last few frames (NetSession.InputRedundancy), oldest
        // first: [count u8] then `count` frames in the layout WriteInputFrame writes. A packet lost
        // on the way is covered by the next one, which repeats the frames it held. The host reads
        // them with ReadInput in a loop and keeps only ticks it has not seen.
        public static byte[] InputBundle(InputFrame[] frames, int count)
        {
            var w = new NetWriter(MsgType.PlayerInput);
            w.U8((byte)count);
            for (int i = 0; i < count; i++) WriteInputFrame(w, frames[i]);
            return w.ToArray();
        }

        static void WriteInputFrame(NetWriter w, in InputFrame f)
        {
            w.U32(f.tick); w.V2(f.move); w.F(f.lookYaw); w.F(f.lookPitch);
            byte bits = 0;
            if (f.jump) bits |= 1; if (f.legL) bits |= 2; if (f.legR) bits |= 4; if (f.sprint) bits |= 8;
            if (f.passGround) bits |= 16; if (f.passLofted) bits |= 32; if (f.tackle) bits |= 64;
            if (f.reset) bits |= 128;
            w.U8(bits);
            w.U8(f.emoteId);   // 255 = none
            // Second bit byte, TRAILING. The first eight are full, and putting new bits after
            // emoteId keeps the leading layout byte-for-byte identical, so ReadInput can treat
            // it as optional (see More) instead of mis-parsing an older frame.
            byte bits2 = 0;
            if (f.closeControl) bits2 |= 1;
            if (f.passChip) bits2 |= 2;
            if (f.cross) bits2 |= 4;
            if (f.thirdLeg) bits2 |= 8;
            w.U8(bits2);
        }

        public static InputFrame ReadInput(NetReader r)
        {
            var f = new InputFrame { tick = r.U32(), move = r.V2(), lookYaw = r.F(), lookPitch = r.F() };
            byte bits = r.U8();
            f.jump = (bits & 1) != 0; f.legL = (bits & 2) != 0; f.legR = (bits & 4) != 0; f.sprint = (bits & 8) != 0;
            f.passGround = (bits & 16) != 0; f.passLofted = (bits & 32) != 0; f.tackle = (bits & 64) != 0;
            f.reset = (bits & 128) != 0;
            f.emoteId = r.U8();
            byte bits2 = r.More ? r.U8() : (byte)0;
            f.closeControl = (bits2 & 1) != 0;
            f.passChip = (bits2 & 2) != 0;
            f.cross = (bits2 & 4) != 0;
            f.thirdLeg = (bits2 & 8) != 0;
            return f;
        }

        public static byte[] Snap(in Snapshot s)
        {
            var w = new NetWriter(MsgType.Snapshot);
            w.U32(s.tick); w.V3(s.ballPos); w.V3(s.ballVel);
            w.U8(s.homeScore); w.U8(s.awayScore);
            w.U32(s.clockSec);
            w.U8((byte)(s.bodies?.Length ?? 0));
            if (s.bodies != null)
                // `erect` sits INSIDE the fixed-stride record (it is per body), which is why this
                // change cost a ProtocolVersion bump where `guided` below did not.
                foreach (var b in s.bodies) { w.U8(b.slot); w.V3(b.pos); w.F(b.yaw); w.B(b.down); w.U8(b.emoteId); w.U8(b.emotePhase); w.U8(b.anim); w.U32(b.lastInputTick); w.B(b.erect); }
            // TRAILING, after the body loop, so it costs no protocol break. It belongs to the BALL and
            // is declared beside ballVel in the struct for readability - wire order and field order do
            // not have to agree, and here they deliberately do not. Putting it beside ballVel ON THE
            // WIRE would shift every byte from the scores onward and force a ProtocolVersion bump for
            // one bit. It also cannot go inside the body loop: those records are fixed-stride with no
            // per-record length, so r.More is TRUE mid-loop and a guard there would silently mis-parse
            // every remaining body instead of defaulting.
            w.B(s.guided);
            return w.ToArray();
        }

        public static Snapshot ReadSnap(NetReader r)
        {
            var s = new Snapshot { tick = r.U32(), ballPos = r.V3(), ballVel = r.V3(), homeScore = r.U8(), awayScore = r.U8(), clockSec = (ushort)r.U32() };
            int n = r.U8();
            s.bodies = new BodyState[n];
            for (int i = 0; i < n; i++)
                s.bodies[i] = new BodyState { slot = r.U8(), pos = r.V3(), yaw = r.F(), down = r.B(), emoteId = r.U8(), emotePhase = r.U8(), anim = r.U8(), lastInputTick = r.U32(), erect = r.B() };
            // Trailing (see Snap). A sender without it reads as not guided, which is the permissive
            // direction: the landing telegraph draws. That is correct for an older peer, which has no
            // assist window it could be lying about.
            s.guided = r.More && r.B();
            return s;
        }

        // The post-match table. Sent RELIABLE, exactly once, on the host's full-time edge. 12 bytes a
        // row plus a count, so 22 players is 265 bytes - a one-shot cost, not a per-frame one.
        public static byte[] MatchStats(StatRow[] rows)
        {
            var w = new NetWriter(MsgType.MatchStats);
            int n = rows != null ? Mathf.Min(rows.Length, 255) : 0;
            w.U8((byte)n);
            for (int i = 0; i < n; i++)
            {
                var r = rows[i];
                w.U8(r.slot); w.U8(r.team); w.U8(r.shirt); w.U8(r.flags);
                w.U8(r.goals); w.U8(r.assists); w.U8(r.shots);
                w.U8(r.passes); w.U8(r.passesDone); w.U8(r.tackles); w.U8(r.saves);
                w.U8(r.rat10);
            }
            return w.ToArray();
        }

        public static StatRow[] ReadMatchStats(NetReader r)
        {
            int n = r.U8();
            var rows = new StatRow[n];
            for (int i = 0; i < n; i++)
                rows[i] = new StatRow
                {
                    slot = r.U8(), team = r.U8(), shirt = r.U8(), flags = r.U8(),
                    goals = r.U8(), assists = r.U8(), shots = r.U8(),
                    passes = r.U8(), passesDone = r.U8(), tackles = r.U8(), saves = r.U8(),
                    rat10 = r.U8(),
                };
            return rows;
        }

        public static byte[] Event(string tag) { var w = new NetWriter(MsgType.MatchEvent); w.Str(tag); return w.ToArray(); }

        // Ball-kick position (host -> clients) for the 3D kick SFX. Unreliable: a dropped one just
        // means one missed thud, cheaper than reliable for a frequent transient.
        public static byte[] BallKick(Vector3 pos) { var w = new NetWriter(MsgType.BallKick); w.V3(pos); return w.ToArray(); }

        // Woodwork hit (host -> clients). Same deal, plus the impact speed so the clang is mixed and
        // pitched identically on every peer instead of each one guessing.
        public static byte[] PostHit(Vector3 pos, float speed) { var w = new NetWriter(MsgType.PostHit); w.V3(pos); w.F(speed); return w.ToArray(); }

        // Quickchat. Same wire both directions (client->host request, host->clients relay). slot =
        // sender's player slot (host stamps the authoritative value on relay). presetId 255 = use
        // the custom string; else it's an index into QuickChat.Phrases and custom is ignored/empty.
        public static byte[] QuickChat(byte slot, byte presetId, string custom)
        {
            var w = new NetWriter(MsgType.QuickChat); w.U8(slot); w.U8(presetId); w.Str(custom ?? ""); return w.ToArray();
        }
        public static void ReadQuickChat(NetReader r, out byte slot, out byte presetId, out string custom)
        {
            slot = r.U8(); presetId = r.U8(); custom = r.Str();
        }

        // Roster + config (host -> clients).
        public static byte[] Roster(MatchConfig cfg, LobbySlot[] slots)
        {
            var w = new NetWriter(MsgType.RosterSync);
            w.U8(cfg.mode); w.U8(cfg.stadium); w.U8(cfg.perSide);
            w.U32(cfg.matchSec); w.B(cfg.publicLobby);
            w.F(cfg.goalScale); w.F(cfg.keeperAbility);
            w.B(cfg.fkPlaced);
            w.F(cfg.fkBallX); w.F(cfg.fkBallZ); w.F(cfg.fkWallX); w.F(cfg.fkWallZ);
            w.B(cfg.fkRandom); w.U32(cfg.fkSeed);
            // Accuracy fields appended last so the existing field order stays untouched. Only the
            // format survives; the four retired slots are written as zero to hold the layout.
            w.U8(0); w.U8(0);
            w.B(cfg.accSuddenDeath); w.U8(0); w.U32(0);
            w.U8(cfg.lookingFor);    // appended last for the same reason
            w.F(cfg.goalScaleH);     // ...and this after it
            w.U8(cfg.cupStyle);      // v8: the cup's style + format, read unconditionally (the
            w.U8(cfg.cupFormat);     //     slot list follows, so a trailing r.More test is useless)
            w.U8((byte)(slots?.Length ?? 0));
            if (slots != null)
                foreach (var s in slots) { w.U8(s.slot); w.B(s.human); w.B(s.ai); w.B(s.ready); w.U8(s.role); w.Str(s.name); WriteAppearance(w, s.appearance); w.B(s.nominated); w.U8(s.voteFor); }
            return w.ToArray();
        }

        public static void ReadRoster(NetReader r, out MatchConfig cfg, out LobbySlot[] slots)
        {
            cfg = new MatchConfig { mode = r.U8(), stadium = r.U8(), perSide = r.U8(),
                                    matchSec = (ushort)r.U32(), publicLobby = r.B(),
                                    goalScale = r.F(), keeperAbility = r.F(),
                                    fkPlaced = r.B(),
                                    fkBallX = r.F(), fkBallZ = r.F(), fkWallX = r.F(), fkWallZ = r.F(),
                                    fkRandom = r.B(), fkSeed = r.U32() };
            // Accuracy block, in the order it was written. The four retired slots (wall count,
            // target count, kick count, turn seconds) are consumed and dropped: an initializer has
            // no member to discard them into, and the reads still have to happen to stay in step
            // with the writer.
            r.U8(); r.U8();
            cfg.accSuddenDeath = r.B();
            r.U8(); r.U32();
            cfg.lookingFor = r.U8();
            cfg.goalScaleH = r.F();
            cfg.cupStyle = r.U8();    // v8 (see Roster)
            cfg.cupFormat = r.U8();
            int n = r.U8();
            slots = new LobbySlot[n];
            for (int i = 0; i < n; i++)
                slots[i] = new LobbySlot { slot = r.U8(), human = r.B(), ai = r.B(), ready = r.B(), role = r.U8(), name = r.Str(), appearance = ReadAppearance(r),
                                           nominated = r.B(), voteFor = r.U8() };
        }

        public static byte[] Ready(bool ready) { var w = new NetWriter(MsgType.ReadyToggle); w.B(ready); return w.ToArray(); }
        // Client -> host: updated appearance after re-customizing in the lobby.
        // Appearance plus the sender's PASSING build as a node mask (SkillTree.PackPassing). The mask
        // is a genuine TRAILING append - Loadout wrote nothing after the appearance - so a peer that
        // does not send it still parses, and the reader defaults it. It must NOT go inside
        // PlayerAppearance: Hello writes the appearance and then ProtocolVersion AFTER it, so growing
        // the appearance moves the version byte and breaks the version gate in both directions.
        public static byte[] Loadout(PlayerAppearance a, byte passMask)
        { var w = new NetWriter(MsgType.UpdateLoadout); WriteAppearance(w, a); w.U8(passMask); w.U8(PlayerFlags()); return w.ToArray(); }
        // One jersey PNG chunk (client<->host). Field order must match ReadJerseyChunk.
        public static byte[] JerseyChunk(byte slot, uint index, uint total, uint totalBytes, byte[] chunk)
        {
            var w = new NetWriter(MsgType.JerseyChunk);
            w.U8(slot); w.U32(index); w.U32(total); w.U32(totalBytes); w.Bytes(chunk);
            return w.ToArray();
        }
        public static JerseyChunkMsg ReadJerseyChunk(NetReader r)
            => new JerseyChunkMsg { slot = r.U8(), index = r.U32(), total = r.U32(), totalBytes = r.U32(), chunk = r.Bytes() };
        public static byte[] RequestSlot(byte slot) { var w = new NetWriter(MsgType.RequestSlot); w.U8(slot); return w.ToArray(); }
        public static byte[] Start() => new NetWriter(MsgType.StartMatch).ToArray();
        public static byte[] ReplayStart() => new NetWriter(MsgType.ReplayStart).ToArray();
        public static byte[] SkipVote() => new NetWriter(MsgType.SkipVote).ToArray();

        public static byte[] NominateJersey() => new NetWriter(MsgType.NominateJersey).ToArray();
        public static byte[] CastJerseyVote(byte candidateSlot) { var w = new NetWriter(MsgType.CastJerseyVote); w.U8(candidateSlot); return w.ToArray(); }
        public static byte[] ReplayEnd() => new NetWriter(MsgType.ReplayEnd).ToArray();

        // The AI crosser panel (client -> host as a request; host -> everyone as the truth).
        public static byte[] CrosserSetup(in CrosserSetupMsg c)
        {
            var w = new NetWriter(MsgType.CrosserSetup);
            w.F(c.targetX); w.F(c.targetZ); w.F(c.spotX); w.F(c.spotZ);
            w.U8(c.delivery); w.F(c.ballSpeed); w.F(c.crossInterval);
            w.Str(c.aiName ?? "");
            return w.ToArray();
        }

        public static CrosserSetupMsg ReadCrosserSetup(NetReader r)
            => new CrosserSetupMsg { targetX = r.F(), targetZ = r.F(), spotX = r.F(), spotZ = r.F(),
                                     delivery = r.U8(), ballSpeed = r.F(), crossInterval = r.F(),
                                     aiName = r.Str() };

        // Set-pieces shootout tally (host -> clients). Writes activeShooter + over flag + the
        // per-slot scored/taken arrays (fixed length, sender passes MaxSlots-sized arrays).
        public static byte[] Shootout(in ShootoutState s)
        {
            var w = new NetWriter(MsgType.ShootoutState);
            w.U8(s.activeShooter); w.B(s.over);
            byte n = (byte)(s.scored?.Length ?? 0);
            w.U8(n);
            for (int i = 0; i < n; i++) { w.U8(s.scored[i]); w.U8(s.taken[i]); }
            return w.ToArray();
        }

        public static ShootoutState ReadShootout(NetReader r)
        {
            var s = new ShootoutState { activeShooter = r.U8(), over = r.B() };
            int n = r.U8();
            s.scored = new byte[n]; s.taken = new byte[n];
            for (int i = 0; i < n; i++) { s.scored[i] = r.U8(); s.taken[i] = r.U8(); }
            return s;
        }

        // ==========================================================================================
        // Trickshot Cup (design 9.4). Layouts, byte for byte:
        //
        //   CupState      u8 version | u32 tick | u8 phase | u32 phaseSerial | f32 phaseTime | u8 stage
        //                 | u8 style | u8 format | u32 seed | u8 captain | u16 teamNation | u8 leverPulls
        //                 | u8 flags (1 = hasBracket) | u8 currentRound (stage<<5 | index, 255 none)
        //                 | u8 nPlayers | nPlayers x PLAYER | u8 nOrder | nOrder x u8
        //                 | u8 nResults | nResults x RESULT | u32 bracketHash
        //     PLAYER      u8 slot | u16 nation | u8 entrant | u8 status | u8 spectating | u16 liveOpponent
        //                 | u8 liveFor | u8 liveAgainst | u8 liveKick | u8 coin | u8 coinMade | u8 coinRight   (15 B)
        //     RESULT      u8 stage<<5 | index  (stage in the top 3 bits, index 0..15 below)
        //                 | u8 flags (1 = simulated, 2 = suddenDeath, 4 = firstKickerIsB)
        //                 | [played only] u8 scoreA | u8 scoreB | u8 nKicks | ceil(nKicks/2) x u8 (two nibbles a byte)
        //   CupRequest    u8 kind | u32 arg | u32 payloadLen | payload
        //   CupStream     u8 fromSlot | u32 seq | v3 camPos | f32 x4 camRot | f32 camFov | v3 ballPos
        //                 | u16 nationA | u16 nationB | u8 nBodies | nBodies x BODY
        //     BODY        u8 vslot | u8 slot | u8 flags | v3 pos | f32 yaw | u8 emoteId | u8 emotePhase   (21 B)
        //   CupRoundState u32 len | len x u8   (CupRoundState.ToBytes: its own versioned record)
        //
        // u16 = two bytes, low first (NetWriter has no U16). A nation / entrant of -1 is 0xFFFF / 255.
        // ==========================================================================================

        /// <summary>The largest CupRequest payload the host will read (a round record is ~30 B, an order 9 B).</summary>
        public const int CupRequestMaxPayload = 1024;
        /// <summary>The largest CupRoundState blob a client will read (~45 B + a byte per two kicks).</summary>
        public const int CupRoundStateMaxBytes = 512;
        /// <summary>Bodies per CupStream (a Co-op round has at most 17).</summary>
        public const int CupStreamMaxBodies = 32;

        static void U16(NetWriter w, int v)
        {
            if (v < 0 || v > 0xFFFE) v = 0xFFFF;
            w.U8((byte)(v & 0xFF));
            w.U8((byte)((v >> 8) & 0xFF));
        }

        static int U16(NetReader r)
        {
            int v = r.U8() | (r.U8() << 8);
            return v == 0xFFFF ? -1 : v;
        }

        static byte Slot(int v) => v < 0 || v > 254 ? (byte)255 : (byte)v;
        static int Slot(byte v) => v == 255 ? -1 : v;

        public static byte[] CupState(in CupStateMsg m)
        {
            var w = new NetWriter(MsgType.CupState);
            w.U8(m.version); w.U32(m.tick);
            w.U8(m.phase); w.U32(m.phaseSerial); w.F(m.phaseTime);
            w.U8(m.stage); w.U8(m.style); w.U8(m.format);
            w.U32(m.seed);
            w.U8(m.captainSlot);
            U16(w, m.teamNation);
            w.U8(m.leverPulls);
            w.U8((byte)(m.hasBracket ? 1 : 0));
            w.U8(m.currentRound);
            int np = m.players != null ? Mathf.Min(m.players.Length, NetSession.MaxSlots) : 0;
            w.U8((byte)np);
            for (int i = 0; i < np; i++)
            {
                var p = m.players[i];
                w.U8(p.slot); U16(w, p.nation); w.U8(Slot(p.entrant));
                w.U8(p.status); w.U8(p.spectating);
                U16(w, p.liveOpponent);
                w.U8(p.liveFor); w.U8(p.liveAgainst); w.U8(p.liveKick);
                w.U8(p.coin); w.U8(p.coinMade); w.U8(p.coinRight);
            }
            int no = m.order != null ? Mathf.Min(m.order.Length, NetSession.MaxSlots) : 0;
            w.U8((byte)no);
            for (int i = 0; i < no; i++) w.U8(m.order[i]);
            int nr = m.results != null ? Mathf.Min(m.results.Length, 255) : 0;
            w.U8((byte)nr);
            for (int i = 0; i < nr; i++)
            {
                var res = m.results[i];
                w.U8((byte)(((res.stage & 7) << 5) | (res.index & 31)));
                byte flags = 0;
                if (res.simulated) flags |= 1;
                if (res.suddenDeath) flags |= 2;
                if (res.firstKicker != 0) flags |= 4;
                w.U8(flags);
                if (res.simulated) continue;
                w.U8(res.scoreA); w.U8(res.scoreB);
                int nk = res.kicks != null ? Mathf.Min(res.kicks.Length, 255) : 0;
                w.U8((byte)nk);
                for (int k = 0; k < nk; k += 2)
                {
                    int lo = res.kicks[k] & 15;
                    int hi = k + 1 < nk ? res.kicks[k + 1] & 15 : 0;
                    w.U8((byte)(lo | (hi << 4)));
                }
            }
            w.U32(m.bracketHash);
            return w.ToArray();
        }

        public static CupStateMsg ReadCupState(NetReader r)
        {
            var m = new CupStateMsg();
            m.version = r.U8(); m.tick = r.U32();
            m.phase = r.U8(); m.phaseSerial = r.U32(); m.phaseTime = r.F();
            m.stage = r.U8(); m.style = r.U8(); m.format = r.U8();
            m.seed = r.U32();
            m.captainSlot = r.U8();
            m.teamNation = U16(r);
            m.leverPulls = r.U8();
            m.hasBracket = (r.U8() & 1) != 0;
            m.currentRound = r.U8();
            int np = r.U8();
            if (np > NetSession.MaxSlots) throw new System.IO.InvalidDataException("CupState: " + np + " players");
            m.players = new CupPlayerRow[np];
            for (int i = 0; i < np; i++)
            {
                var p = new CupPlayerRow();
                p.slot = r.U8(); p.nation = U16(r); p.entrant = Slot(r.U8());
                p.status = r.U8(); p.spectating = r.U8();
                p.liveOpponent = U16(r);
                p.liveFor = r.U8(); p.liveAgainst = r.U8(); p.liveKick = r.U8();
                p.coin = r.U8(); p.coinMade = r.U8(); p.coinRight = r.U8();
                m.players[i] = p;
            }
            int no = r.U8();
            if (no > NetSession.MaxSlots) throw new System.IO.InvalidDataException("CupState: " + no + " order entries");
            m.order = new byte[no];
            for (int i = 0; i < no; i++) m.order[i] = r.U8();
            int nr = r.U8();
            m.results = new CupResultRow[nr];
            for (int i = 0; i < nr; i++)
            {
                var res = new CupResultRow();
                byte id = r.U8();
                res.stage = (byte)(id >> 5);
                res.index = (byte)(id & 31);
                byte flags = r.U8();
                res.simulated = (flags & 1) != 0;
                res.suddenDeath = (flags & 2) != 0;
                res.firstKicker = (byte)((flags & 4) != 0 ? 1 : 0);
                if (!res.simulated)
                {
                    res.scoreA = r.U8(); res.scoreB = r.U8();
                    int nk = r.U8();
                    res.kicks = new byte[nk];
                    for (int k = 0; k < nk; k += 2)
                    {
                        byte b = r.U8();
                        res.kicks[k] = (byte)(b & 15);
                        if (k + 1 < nk) res.kicks[k + 1] = (byte)((b >> 4) & 15);
                    }
                }
                else res.kicks = System.Array.Empty<byte>();
                m.results[i] = res;
            }
            m.bracketHash = r.U32();
            return m;
        }

        public static byte[] CupRequest(in CupRequestMsg m)
        {
            var w = new NetWriter(MsgType.CupRequest);
            w.U8(m.kind);
            w.U32(unchecked((uint)m.arg));
            w.Bytes(m.payload);
            return w.ToArray();
        }

        public static CupRequestMsg ReadCupRequest(NetReader r)
        {
            var m = new CupRequestMsg { kind = r.U8(), arg = unchecked((int)r.U32()) };
            m.payload = BoundedBytes(r, CupRequestMaxPayload, "CupRequest");
            return m;
        }

        public static byte[] CupStream(in CupStreamMsg m)
        {
            var w = new NetWriter(MsgType.CupStream);
            w.U8(m.fromSlot); w.U32(m.seq);
            w.V3(m.camPos);
            w.F(m.camRot.x); w.F(m.camRot.y); w.F(m.camRot.z); w.F(m.camRot.w);
            w.F(m.camFov);
            w.V3(m.ballPos);
            U16(w, m.nationA); U16(w, m.nationB);
            int n = m.bodies != null ? Mathf.Min(m.bodies.Length, CupStreamMaxBodies) : 0;
            w.U8((byte)n);
            for (int i = 0; i < n; i++)
            {
                var b = m.bodies[i];
                w.U8(b.vslot); w.U8(b.slot); w.U8(b.flags);
                w.V3(b.pos); w.F(b.yaw);
                w.U8(b.emoteId); w.U8(b.emotePhase);
            }
            return w.ToArray();
        }

        public static CupStreamMsg ReadCupStream(NetReader r)
        {
            var m = new CupStreamMsg { fromSlot = r.U8(), seq = r.U32() };
            m.camPos = r.V3();
            m.camRot = new Quaternion(r.F(), r.F(), r.F(), r.F());
            m.camFov = r.F();
            m.ballPos = r.V3();
            m.nationA = U16(r); m.nationB = U16(r);
            int n = r.U8();
            if (n > CupStreamMaxBodies) throw new System.IO.InvalidDataException("CupStream: " + n + " bodies");
            m.bodies = new CupStreamBody[n];
            for (int i = 0; i < n; i++)
                m.bodies[i] = new CupStreamBody { vslot = r.U8(), slot = r.U8(), flags = r.U8(), pos = r.V3(), yaw = r.F(), emoteId = r.U8(), emotePhase = r.U8() };
            return m;
        }

        /// <summary>The host-simulated round's record (CupRoundState.ToBytes), as an opaque blob the cup layer decodes.</summary>
        public static byte[] CupRoundState(byte[] record)
        {
            var w = new NetWriter(MsgType.CupRoundState);
            w.Bytes(record);
            return w.ToArray();
        }

        public static byte[] ReadCupRoundState(NetReader r) => BoundedBytes(r, CupRoundStateMaxBytes, "CupRoundState");

        // A length-prefixed blob with a ceiling: the length is an untrusted wire uint, and an
        // unbounded read would let one corrupt packet ask for gigabytes (the jersey path guards the
        // same way). Over the ceiling is a malformed packet - thrown, so RouteMessage's guard drops it.
        static byte[] BoundedBytes(NetReader r, int max, string what)
        {
            uint n = r.U32();
            if (n > (uint)max) throw new System.IO.InvalidDataException(what + ": " + n + "-byte payload (max " + max + ")");
            if (n == 0) return System.Array.Empty<byte>();
            var buf = new byte[n];
            for (int i = 0; i < n; i++) buf[i] = r.U8();
            return buf;
        }
    }
}
