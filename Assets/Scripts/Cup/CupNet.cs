using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The cup's side of the wire (design 9.3 / 9.4): how the director's read model becomes a
    /// <see cref="CupStateMsg"/> and how the cup's pure records ride a <see cref="CupRequestMsg"/>
    /// payload, plus the spectate stream builder. Pure functions over public surfaces - nothing
    /// here touches the session, so the host's coalescing and the client's application live
    /// beside the state they mutate (CupDirector.Net.cs / CupRoundDriver.Net.cs).
    ///
    /// The one rule every layout follows: NOTHING SEED-DERIVABLE CROSSES THE WIRE. The draw is
    /// rebuilt on every peer from the seed and the entrants' picks; a simulated round is re-run
    /// by CupSim on the client from the same forked stream; free-kick spots, coin faces and the
    /// dejection variants never leave their machine. Only what humans DID goes across: picks,
    /// readiness, the played rounds' kick lines, the coin calls, the live rows.
    /// </summary>
    public static class CupNet
    {
        /// <summary>Bumped when the CupState layout changes; a client refuses other versions (logged once).</summary>
        public const byte StateVersion = 1;
        /// <summary>The host coalesces CupState broadcasts to at most one every this many seconds (10/s), phase changes excepted (sent at once).</summary>
        public const float StateCoalesceSeconds = 0.1f;
        /// <summary>A spectated round's view streams at 20 Hz.</summary>
        public const float StreamInterval = 0.05f;
        /// <summary>The owner of a local round reports its live row at most this often.</summary>
        public const float LiveRowInterval = 0.25f;
        /// <summary>A host-simulated round's CupRoundState goes out at least this often (plus on every phase / kick edge).</summary>
        public const float RoundStateInterval = 0.5f;
        /// <summary>How long a client keeps its OWN live row over the host's echo after reporting a change (the echo lags a round trip).</summary>
        public const float LiveRowEchoGrace = 1.5f;

        // ==========================================================================================
        // CupState
        // ==========================================================================================

        /// <summary>The host's whole read model as a message (the players' Spectated flags are derived here).</summary>
        public static CupStateMsg BuildState(CupDirector d)
        {
            var m = new CupStateMsg();
            m.version = StateVersion;
            m.tick = d.Tick;
            m.phase = (byte)d.Phase;
            m.phaseSerial = (uint)Math.Max(0, d.PhaseSerial);
            m.phaseTime = d.PhaseTime;
            m.stage = (byte)d.Stage;
            m.style = (byte)d.Style;
            m.format = (byte)d.Format;
            m.seed = d.Seed;
            m.captainSlot = (byte)Mathf.Clamp(d.CaptainSlot, 0, 255);
            m.teamNation = d.TeamNation;
            m.leverPulls = (byte)Mathf.Clamp(d.LeverPulls, 0, 255);
            m.hasBracket = d.Bracket != null;
            m.currentRound = PackRoundId(d.CurrentRound);

            var players = d.Players;
            int n = Mathf.Min(players.Count, NetSession.MaxSlots);
            m.players = new CupPlayerRow[n];
            for (int i = 0; i < n; i++)
            {
                var p = players[i];
                var row = new CupPlayerRow();
                row.slot = (byte)Mathf.Clamp(p.Slot, 0, 255);
                row.nation = p.Nation;
                row.entrant = p.Entrant;
                row.status = StatusOf(p, d.AnySpectating(p.Slot));
                row.spectating = p.SpectatingSlot < 0 ? (byte)255 : (byte)p.SpectatingSlot;
                row.liveOpponent = p.LiveOpponentNation;
                row.liveFor = (byte)Mathf.Clamp(p.LiveScoreFor, 0, 255);
                row.liveAgainst = (byte)Mathf.Clamp(p.LiveScoreAgainst, 0, 255);
                row.liveKick = (byte)Mathf.Clamp(p.LiveKick, 0, 255);
                row.coin = CoinBitsOf(p);
                row.coinMade = (byte)Mathf.Clamp(p.CoinCallsMade, 0, 255);
                row.coinRight = (byte)Mathf.Clamp(p.CoinCallsRight, 0, 255);
                m.players[i] = row;
            }

            var order = d.CoopOrder;
            int no = order != null ? Mathf.Min(order.Length, NetSession.MaxSlots) : 0;
            m.order = new byte[no];
            for (int i = 0; i < no; i++) m.order[i] = OrderSlotByte(order[i]);

            m.results = d.Bracket != null ? BuildResults(d.Bracket) : Array.Empty<CupResultRow>();
            m.bracketHash = d.Bracket != null ? BracketHash(d.Bracket) : 0u;
            return m;
        }

        /// <summary>A round's identity in one byte (stage in the top 3 bits, index below); 255 for none.</summary>
        public static byte PackRoundId(CupRound r)
            => r == null ? (byte)255 : (byte)((((int)r.Stage & 7) << 5) | (r.Index & 31));

        /// <summary>The bracket round a packed id names, null for 255 / an id the bracket has no round for.</summary>
        public static CupRound UnpackRoundId(CupBracket b, byte id)
        {
            if (b == null || id == 255) return null;
            var stage = (CupStage)(id >> 5);
            int index = id & 31;
            if (!CupStages.IsValid(stage) || index >= CupStages.RoundsIn(stage)) return null;
            return b.Round(stage, index);
        }

        /// <summary>A player's status bits (Spectated is host-derived, so it is passed in).</summary>
        public static byte StatusOf(CupPlayer p, bool spectated)
        {
            byte s = 0;
            if (p.Ready) s |= CupPlayerStatus.Ready;
            if (p.Out) s |= CupPlayerStatus.Out;
            if (p.ReplacedByAi) s |= CupPlayerStatus.ReplacedByAi;
            if (p.Left) s |= CupPlayerStatus.Left;
            if (p.Loaded) s |= CupPlayerStatus.Loaded;
            if (p.Playing) s |= CupPlayerStatus.Playing;
            if (spectated) s |= CupPlayerStatus.Spectated;
            return s;
        }

        /// <summary>A player's coin bits for this round.</summary>
        public static byte CoinBitsOf(CupPlayer p)
        {
            byte c = 0;
            if (p.CoinCall.HasValue)
            {
                c |= CupCoinBits.HasCall;
                if (p.CoinCall.Value == CoinFace.Tails) c |= CupCoinBits.CallTails;
            }
            if (p.CoinCallRight.HasValue)
            {
                c |= CupCoinBits.HasVerdict;
                if (p.CoinCallRight.Value) c |= CupCoinBits.Right;
            }
            return c;
        }

        /// <summary>Every Done round of the bracket, stage then index order; simulated ones carry only their identity.</summary>
        public static CupResultRow[] BuildResults(CupBracket b)
        {
            var list = new List<CupResultRow>(32);
            for (int s = 0; s < b.Stages.Length; s++)
            {
                var rounds = b.Stages[s];
                for (int i = 0; i < rounds.Length; i++)
                {
                    var r = rounds[i];
                    if (r == null || !r.Done) continue;
                    var row = new CupResultRow();
                    row.stage = (byte)r.Stage;
                    row.index = (byte)r.Index;
                    row.simulated = r.Simulated;
                    row.suddenDeath = r.SuddenDeath;
                    row.firstKicker = (byte)(r.FirstKicker.HasValue && r.FirstKicker.Value == CupSide.B ? 1 : 0);
                    row.scoreA = (byte)Mathf.Clamp(r.ScoreA, 0, 255);
                    row.scoreB = (byte)Mathf.Clamp(r.ScoreB, 0, 255);
                    row.kicks = r.Simulated ? Array.Empty<byte>() : PackKicks(r.Kicks);
                    list.Add(row);
                }
            }
            return list.ToArray();
        }

        /// <summary>One KickRecord nibble per entry (KickRecord.ToNibble); the codec packs two to a byte.</summary>
        public static byte[] PackKicks(IList<KickRecord> kicks)
        {
            int n = kicks != null ? Mathf.Min(kicks.Count, 255) : 0;
            var k = new byte[n];
            for (int i = 0; i < n; i++) k[i] = (byte)(kicks[i].ToNibble() & 15);
            return k;
        }

        public static List<KickRecord> UnpackKicks(byte[] nibbles)
        {
            var list = new List<KickRecord>(nibbles != null ? nibbles.Length : 0);
            if (nibbles != null) for (int i = 0; i < nibbles.Length; i++) list.Add(KickRecord.FromNibble(nibbles[i] & 15));
            return list;
        }

        /// <summary>
        /// FNV-1a over the entrants' (nation, human slot) pairs: what a client's rebuilt draw must
        /// match. Names and results are excluded on purpose - only the SHAPE of the draw is checked.
        /// </summary>
        public static uint BracketHash(CupBracket b)
        {
            if (b == null) return 0u;
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < b.Entrants.Count; i++)
                {
                    var e = b.Entrants[i];
                    h = (h ^ (uint)(e.NationIndex & 0xFFFF)) * 16777619u;
                    h = (h ^ (uint)((e.HumanSlot + 1) & 0xFF)) * 16777619u;
                }
                h = (h ^ b.Seed) * 16777619u;
                return h == 0u ? 1u : h;
            }
        }

        /// <summary>The wire size of a CupState in bytes (the MsgType byte included), for diagnostics.</summary>
        public static int SizeOf(in CupStateMsg m)
        {
            int size = 1 + 1 + 4 + 1 + 4 + 4 + 1 + 1 + 1 + 4 + 1 + 2 + 1 + 1 + 1 + 1;
            size += (m.players != null ? m.players.Length : 0) * 15;
            size += 1 + (m.order != null ? m.order.Length : 0);
            size += 1;
            if (m.results != null)
                for (int i = 0; i < m.results.Length; i++)
                {
                    var r = m.results[i];
                    size += 2;
                    if (r.simulated) continue;
                    int nk = r.kicks != null ? r.kicks.Length : 0;
                    size += 3 + (nk + 1) / 2;
                }
            size += 4;
            return size;
        }

        // ==========================================================================================
        // CupRequest payloads
        // ==========================================================================================

        public static CupRequestMsg Request(CupRequestKind kind, int arg, byte[] payload)
            => new CupRequestMsg { kind = (byte)kind, arg = arg, payload = payload };

        /// <summary>A finished round's record (CupRound.WriteTo) for CupRequest.RoundResult.</summary>
        public static byte[] PackRound(CupRound r)
        {
            if (r == null) return Array.Empty<byte>();
            var w = new CupByteWriter(64);
            r.WriteTo(w);
            return w.ToArray();
        }

        /// <summary>The inverse of <see cref="PackRound"/>; null (logged) on a malformed record.</summary>
        public static CupRound UnpackRound(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return null;
            try
            {
                return CupRound.ReadFrom(new CupByteReader(payload));
            }
            catch (Exception e)
            {
                CupLog.Warn("CupNet: bad round record (" + e.Message + ")");
                return null;
            }
        }

        /// <summary>
        /// An order entry on the wire: a slot 0..7, or <see cref="EmptyOrderSlot"/> for an EMPTY
        /// slot (-1 in CupDirector.CoopOrder). The order screen fills the order one drag at a time
        /// and every peer watches it fill, so a partial order must survive the trip: a plain clamp
        /// would turn an empty slot into slot 0 - a player standing in a slot nobody put them in.
        /// </summary>
        public const byte EmptyOrderSlot = 255;

        public static byte OrderSlotByte(int slot) => slot < 0 ? EmptyOrderSlot : (byte)Mathf.Clamp(slot, 0, 254);

        public static int OrderSlotFromByte(byte b) => b == EmptyOrderSlot ? -1 : b;

        /// <summary>A Co-op shooting order (slot per order index, -1 = empty) for CupRequest.SetOrder.</summary>
        public static byte[] PackOrder(int[] order)
        {
            int n = order != null ? Mathf.Min(order.Length, NetSession.MaxSlots) : 0;
            var b = new byte[1 + n];
            b[0] = (byte)n;
            for (int i = 0; i < n; i++) b[1 + i] = OrderSlotByte(order[i]);
            return b;
        }

        public static int[] UnpackOrder(byte[] payload)
        {
            if (payload == null || payload.Length < 1) return Array.Empty<int>();
            int n = Mathf.Min(payload[0], payload.Length - 1);
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = OrderSlotFromByte(payload[1 + i]);
            return order;
        }

        /// <summary>A player's live row (opponent, score, kick, playing) for CupRequest.LiveRow.</summary>
        public static byte[] PackLiveRow(CupPlayer p)
        {
            var b = new byte[6];
            int opp = p.LiveOpponentNation < 0 || p.LiveOpponentNation > 0xFFFE ? 0xFFFF : p.LiveOpponentNation;
            b[0] = (byte)(opp & 0xFF);
            b[1] = (byte)((opp >> 8) & 0xFF);
            b[2] = (byte)Mathf.Clamp(p.LiveScoreFor, 0, 255);
            b[3] = (byte)Mathf.Clamp(p.LiveScoreAgainst, 0, 255);
            b[4] = (byte)Mathf.Clamp(p.LiveKick, 0, 255);
            b[5] = (byte)(p.Playing ? 1 : 0);
            return b;
        }

        public static bool UnpackLiveRow(byte[] payload, out int opponent, out int scoreFor, out int scoreAgainst, out int kick, out bool playing)
        {
            opponent = -1; scoreFor = scoreAgainst = kick = 0; playing = false;
            if (payload == null || payload.Length < 6) return false;
            int opp = payload[0] | (payload[1] << 8);
            opponent = opp == 0xFFFF ? -1 : opp;
            scoreFor = payload[2];
            scoreAgainst = payload[3];
            kick = payload[4];
            playing = payload[5] != 0;
            return true;
        }

        // ==========================================================================================
        // CupStream
        // ==========================================================================================

        /// <summary>
        /// The spectated view of a round on this machine: the rig's camera, the ball and (when
        /// asked) every visible body with its wire emote. `fromSlot` is stamped by the session.
        /// Works under every authority: a Local round's live bodies, or a Client round's puppets
        /// as they stand (the spectator then sees exactly what the participant sees).
        /// </summary>
        public static CupStreamMsg BuildStream(CupRoundDriver drv, CupCameraRig rig, BallController ball, uint seq, bool withBodies)
        {
            var m = new CupStreamMsg();
            m.seq = seq;
            if (rig != null) { m.camPos = rig.CamPos; m.camRot = rig.CamRot; m.camFov = rig.CamFov; }
            else if (Camera.main != null) { m.camPos = Camera.main.transform.position; m.camRot = Camera.main.transform.rotation; m.camFov = Camera.main.fieldOfView; }
            m.ballPos = ball != null ? ball.transform.position : Vector3.zero;
            m.nationA = drv != null ? drv.NationOf(CupSide.A) : -1;
            m.nationB = drv != null ? drv.NationOf(CupSide.B) : -1;
            if (!withBodies || drv == null || !drv.SceneBuilt)
            {
                m.bodies = Array.Empty<CupStreamBody>();
                return m;
            }
            var bodies = drv.Bodies;
            var list = new List<CupStreamBody>(bodies.Count);
            for (int i = 0; i < bodies.Count && list.Count < NetCodec.CupStreamMaxBodies; i++)
            {
                var b = bodies[i];
                if (b == null || !b.Alive || b.Parked || b.VirtualSlot < 0 || b.VirtualSlot > 254) continue;
                Vector3 p = b.Pelvis.position;
                p.y = 0f;
                int emoteId; float phase;
                drv.TryGetWireEmote(b, out emoteId, out phase);
                byte flags = 0;
                if (b.Side == CupSide.B) flags |= CupStreamBodyFlags.SideB;
                if (b.IsKeeperBody) flags |= CupStreamBodyFlags.KeeperBody;
                if (b.Role == CupBodyRole.Referee) flags |= CupStreamBodyFlags.Referee;
                list.Add(new CupStreamBody
                {
                    vslot = (byte)b.VirtualSlot,
                    slot = b.IsHuman && b.Slot <= 254 ? (byte)b.Slot : (byte)255,
                    flags = flags,
                    pos = p,
                    yaw = b.Ragdoll.FacingRotation.eulerAngles.y,
                    emoteId = emoteId < 0 || emoteId > 254 ? (byte)255 : (byte)emoteId,
                    emotePhase = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(phase) * 255f), 0, 255),
                });
            }
            m.bodies = list.ToArray();
            return m;
        }
    }
}
