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
    }

    // One lobby row in the roster (host -> clients each change).
    public struct LobbySlot
    {
        public byte slot;
        public bool human;       // a person holds this slot (else AI)
        public bool ready;
        public bool isLocalHint;  // set per-recipient by nothing; clients compare to LocalSlot
        public string name;
    }

    public enum NetRole : byte { Shooter = 0, Keeper = 1, Spectator = 2 }

    // One player's per-tick intent, sampled from GameInput and sent to the host.
    public struct InputFrame
    {
        public uint tick;
        public Vector2 move;      // wasd
        public float lookYaw;     // desired facing yaw (camera yaw)
        public bool jump, legL, legR, sprint, passGround, passLofted, tackle;
    }

    // One body's state in a snapshot (host -> clients). Compact: pos + yaw + flags.
    public struct BodyState
    {
        public byte slot;         // which player slot (or 255 = ball)
        public Vector3 pos;
        public float yaw;
        public bool down;         // knocked over
    }

    public struct Snapshot
    {
        public uint tick;
        public Vector3 ballPos;
        public Vector3 ballVel;
        public byte homeScore, awayScore;
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
    }

    // Encode/decode helpers so the session code stays readable.
    public static class NetCodec
    {
        public static byte[] Hello(string name)
        {
            var w = new NetWriter(MsgType.Hello); w.Str(name); return w.ToArray();
        }

        public static byte[] AssignSlot(byte slot, NetRole role)
        {
            var w = new NetWriter(MsgType.AssignSlot); w.U8(slot); w.U8((byte)role); return w.ToArray();
        }

        public static byte[] Input(in InputFrame f)
        {
            var w = new NetWriter(MsgType.PlayerInput);
            w.U32(f.tick); w.V2(f.move); w.F(f.lookYaw);
            byte bits = 0;
            if (f.jump) bits |= 1; if (f.legL) bits |= 2; if (f.legR) bits |= 4; if (f.sprint) bits |= 8;
            if (f.passGround) bits |= 16; if (f.passLofted) bits |= 32; if (f.tackle) bits |= 64;
            w.U8(bits);
            return w.ToArray();
        }

        public static InputFrame ReadInput(NetReader r)
        {
            var f = new InputFrame { tick = r.U32(), move = r.V2(), lookYaw = r.F() };
            byte bits = r.U8();
            f.jump = (bits & 1) != 0; f.legL = (bits & 2) != 0; f.legR = (bits & 4) != 0; f.sprint = (bits & 8) != 0;
            f.passGround = (bits & 16) != 0; f.passLofted = (bits & 32) != 0; f.tackle = (bits & 64) != 0;
            return f;
        }

        public static byte[] Snap(in Snapshot s)
        {
            var w = new NetWriter(MsgType.Snapshot);
            w.U32(s.tick); w.V3(s.ballPos); w.V3(s.ballVel);
            w.U8(s.homeScore); w.U8(s.awayScore);
            w.U8((byte)(s.bodies?.Length ?? 0));
            if (s.bodies != null)
                foreach (var b in s.bodies) { w.U8(b.slot); w.V3(b.pos); w.F(b.yaw); w.B(b.down); }
            return w.ToArray();
        }

        public static Snapshot ReadSnap(NetReader r)
        {
            var s = new Snapshot { tick = r.U32(), ballPos = r.V3(), ballVel = r.V3(), homeScore = r.U8(), awayScore = r.U8() };
            int n = r.U8();
            s.bodies = new BodyState[n];
            for (int i = 0; i < n; i++)
                s.bodies[i] = new BodyState { slot = r.U8(), pos = r.V3(), yaw = r.F(), down = r.B() };
            return s;
        }

        public static byte[] Event(string tag) { var w = new NetWriter(MsgType.MatchEvent); w.Str(tag); return w.ToArray(); }

        // Roster + config (host -> clients).
        public static byte[] Roster(MatchConfig cfg, LobbySlot[] slots)
        {
            var w = new NetWriter(MsgType.RosterSync);
            w.U8(cfg.mode); w.U8(cfg.stadium); w.U8(cfg.perSide);
            w.U32(cfg.matchSec); w.B(cfg.publicLobby);
            w.U8((byte)(slots?.Length ?? 0));
            if (slots != null)
                foreach (var s in slots) { w.U8(s.slot); w.B(s.human); w.B(s.ready); w.Str(s.name); }
            return w.ToArray();
        }

        public static void ReadRoster(NetReader r, out MatchConfig cfg, out LobbySlot[] slots)
        {
            cfg = new MatchConfig { mode = r.U8(), stadium = r.U8(), perSide = r.U8(),
                                    matchSec = (ushort)r.U32(), publicLobby = r.B() };
            int n = r.U8();
            slots = new LobbySlot[n];
            for (int i = 0; i < n; i++)
                slots[i] = new LobbySlot { slot = r.U8(), human = r.B(), ready = r.B(), name = r.Str() };
        }

        public static byte[] Ready(bool ready) { var w = new NetWriter(MsgType.ReadyToggle); w.B(ready); return w.ToArray(); }
        public static byte[] Start() => new NetWriter(MsgType.StartMatch).ToArray();
    }
}
