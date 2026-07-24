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

    // The host's chosen match configuration, synced to all peers so everyone builds the
    // same arena/mode. Mirrors the relevant SimConfig fields.
    public struct MatchConfig
    {
        public byte mode;        // GameMode as byte
        public byte stadium;     // StadiumStyle index
        public byte perSide;     // scrimmage team size
        public ushort matchSec;  // match length (seconds)
        public bool publicLobby; // visibility (host-only meaning; carried for display)
        public float goalScale;    // set pieces: goal size multiplier (1 = regulation)
        public float keeperAbility; // set pieces: AI keeper strength 0..1
        // Set pieces: host-placed free-kick spot + wall centre (world x/z), and whether the
        // host actually placed them (else the driver uses its centred defaults).
        public bool fkPlaced;
        public float fkBallX, fkBallZ, fkWallX, fkWallZ;
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
    }

    public enum NetRole : byte { Shooter = 0, Keeper = 1, Spectator = 2, Crosser = 3 }

    // One player's per-tick intent, sampled from GameInput and sent to the host.
    public struct InputFrame
    {
        public uint tick;
        public Vector2 move;      // wasd
        public float lookYaw;     // desired facing yaw (camera yaw)
        public float lookPitch;   // camera pitch (deg): set-piece vertical aim comes from this
        public bool jump, legL, legR, sprint, passGround, passLofted, tackle, reset;
        public byte emoteId;      // 255 = none; else Celebration.Emote to start this tick
    }

    // Animation state a body is in, synced so clients play the matching canned local animation on
    // the interpolated puppet (instead of a rigid stance). Discrete state, not streamed poses.
    public enum AnimState : byte { Idle = 0, Run = 1, Jump = 2, Dive = 3, Down = 4, Kick = 5 }

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
    }

    public struct Snapshot
    {
        public uint tick;
        public Vector3 ballPos;
        public Vector3 ballVel;
        public byte homeScore, awayScore;
        public ushort clockSec;   // match seconds remaining (scrimmage); 0 in modes with no clock
        public BodyState[] bodies;
    }

    // Compact big-endian-free binary writer/reader over a MemoryStream.
    public class NetWriter
    {
        readonly MemoryStream _ms = new MemoryStream(64);
        readonly BinaryWriter _bw;
        public NetWriter(MsgType type) { _bw = new BinaryWriter(_ms); _bw.Write((byte)type); }
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
        public byte[] ToArray() { _bw.Flush(); return _ms.ToArray(); }
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
        }
        public static PlayerAppearance ReadAppearance(NetReader r)
        {
            var a = new PlayerAppearance();
            a.Skin = r.Col();
            a.HairStyle = r.U8();   a.HairColor = r.Col();
            a.FacialStyle = r.U8(); a.FacialColor = r.Col();
            a.Accessory = r.U8();   a.AccessoryColor = r.Col();
            return a;
        }

        // Hello now carries the joining player's name AND appearance so the host can store it
        // per slot and broadcast it on the roster (remote players show each other's look).
        public static byte[] Hello(string name, PlayerAppearance appearance)
        {
            var w = new NetWriter(MsgType.Hello); w.Str(name); WriteAppearance(w, appearance); return w.ToArray();
        }

        public static byte[] AssignSlot(byte slot, NetRole role)
        {
            var w = new NetWriter(MsgType.AssignSlot); w.U8(slot); w.U8((byte)role); return w.ToArray();
        }

        public static byte[] Input(in InputFrame f)
        {
            var w = new NetWriter(MsgType.PlayerInput);
            w.U32(f.tick); w.V2(f.move); w.F(f.lookYaw); w.F(f.lookPitch);
            byte bits = 0;
            if (f.jump) bits |= 1; if (f.legL) bits |= 2; if (f.legR) bits |= 4; if (f.sprint) bits |= 8;
            if (f.passGround) bits |= 16; if (f.passLofted) bits |= 32; if (f.tackle) bits |= 64;
            if (f.reset) bits |= 128;
            w.U8(bits);
            w.U8(f.emoteId);   // 255 = none
            return w.ToArray();
        }

        public static InputFrame ReadInput(NetReader r)
        {
            var f = new InputFrame { tick = r.U32(), move = r.V2(), lookYaw = r.F(), lookPitch = r.F() };
            byte bits = r.U8();
            f.jump = (bits & 1) != 0; f.legL = (bits & 2) != 0; f.legR = (bits & 4) != 0; f.sprint = (bits & 8) != 0;
            f.passGround = (bits & 16) != 0; f.passLofted = (bits & 32) != 0; f.tackle = (bits & 64) != 0;
            f.reset = (bits & 128) != 0;
            f.emoteId = r.U8();
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
                foreach (var b in s.bodies) { w.U8(b.slot); w.V3(b.pos); w.F(b.yaw); w.B(b.down); w.U8(b.emoteId); w.U8(b.emotePhase); w.U8(b.anim); w.U32(b.lastInputTick); }
            return w.ToArray();
        }

        public static Snapshot ReadSnap(NetReader r)
        {
            var s = new Snapshot { tick = r.U32(), ballPos = r.V3(), ballVel = r.V3(), homeScore = r.U8(), awayScore = r.U8(), clockSec = (ushort)r.U32() };
            int n = r.U8();
            s.bodies = new BodyState[n];
            for (int i = 0; i < n; i++)
                s.bodies[i] = new BodyState { slot = r.U8(), pos = r.V3(), yaw = r.F(), down = r.B(), emoteId = r.U8(), emotePhase = r.U8(), anim = r.U8(), lastInputTick = r.U32() };
            return s;
        }

        public static byte[] Event(string tag) { var w = new NetWriter(MsgType.MatchEvent); w.Str(tag); return w.ToArray(); }

        // Ball-kick position (host -> clients) for the 3D kick SFX. Unreliable: a dropped one just
        // means one missed thud, cheaper than reliable for a frequent transient.
        public static byte[] BallKick(Vector3 pos) { var w = new NetWriter(MsgType.BallKick); w.V3(pos); return w.ToArray(); }

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
            w.U8((byte)(slots?.Length ?? 0));
            if (slots != null)
                foreach (var s in slots) { w.U8(s.slot); w.B(s.human); w.B(s.ai); w.B(s.ready); w.U8(s.role); w.Str(s.name); WriteAppearance(w, s.appearance); }
            return w.ToArray();
        }

        public static void ReadRoster(NetReader r, out MatchConfig cfg, out LobbySlot[] slots)
        {
            cfg = new MatchConfig { mode = r.U8(), stadium = r.U8(), perSide = r.U8(),
                                    matchSec = (ushort)r.U32(), publicLobby = r.B(),
                                    goalScale = r.F(), keeperAbility = r.F(),
                                    fkPlaced = r.B(),
                                    fkBallX = r.F(), fkBallZ = r.F(), fkWallX = r.F(), fkWallZ = r.F() };
            int n = r.U8();
            slots = new LobbySlot[n];
            for (int i = 0; i < n; i++)
                slots[i] = new LobbySlot { slot = r.U8(), human = r.B(), ai = r.B(), ready = r.B(), role = r.U8(), name = r.Str(), appearance = ReadAppearance(r) };
        }

        public static byte[] Ready(bool ready) { var w = new NetWriter(MsgType.ReadyToggle); w.B(ready); return w.ToArray(); }
        // Client -> host: updated appearance after re-customizing in the lobby.
        public static byte[] Loadout(PlayerAppearance a) { var w = new NetWriter(MsgType.UpdateLoadout); WriteAppearance(w, a); return w.ToArray(); }
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
        public static byte[] ReplayEnd() => new NetWriter(MsgType.ReplayEnd).ToArray();

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
    }
}
