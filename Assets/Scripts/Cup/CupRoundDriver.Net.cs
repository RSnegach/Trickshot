using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The round driver's WIRE (design 9.3): what a host-simulated round publishes and what a
    /// client mirrors. Bound lazily on the first Host / Client tick, unbound with the component.
    ///
    /// HOST (RoundAuthority.Host): the same simulation as Local (Kick.cs), the remote humans fed
    /// from their wire input first (Scene.cs FeedRemoteInputs), then out go: a Snapshot of every
    /// visible body at SimConfig.NetSnapshotInterval - the slot byte is the body's
    /// CupBody.VirtualSlot (humans 0..7, twins / AI / the referee from AiBodyIdBase up), emote id
    /// and phase from its Celebration - and the CupRoundState record on every phase or kick edge
    /// and every CupNet.RoundStateInterval. The replay rides the session's existing plumbing:
    /// ReplayPlaying rising / falling = BeginReplay / EndReplayHost, and every skip vote reaches
    /// VoteSkipReplayBy through SkipVoteReceived (the session's own "every human" tally is off:
    /// the cup's voters are the humans WITH A BODY in the round). Publishing continues past Over,
    /// so the Co-op trophy lift's bodies keep moving on every peer for its whole cinematic.
    ///
    /// CLIENT (RoundAuthority.Client): the local human's input goes out every frame - the keeper's
    /// cone yaw (GameCamera.KeeperLookYaw) when keeping, the camera yaw otherwise, the frame tick
    /// the director's monotonic Tick - gated like the local device is (the pause overlay, the wheel,
    /// a replay: movement and buttons read idle, Space latched so a release lands, the one-shot
    /// emote pick never dropped); the host's snapshots are sampled at NetInterpDelay through the
    /// session's interpolation buffer and posed onto the puppets (ApplyBodyPose), the ball mirrored
    /// kinematic; CupRoundState -> ApplyState; ReplayStarted / Ended -> ClientSetReplay.
    ///
    /// There is NO local prediction of a client's keeper: the cup's bodies are twins that swap
    /// under a cut, hold their line, dive on the host's ball - a predicted keeper reacting to a
    /// 100 ms-old ball buys nothing a plain puppet does not, so the local body is a puppet like
    /// every other and the host's keeper answers the client's input a round trip later.
    /// </summary>
    public partial class CupRoundDriver
    {
        NetSession _ns;
        bool _nsBound, _nsHost, _nsClient;
        float _snapAccum, _stateAccum;
        bool _stateNow;
        bool _replayWasPlaying;
        bool _ballMirrored;
        bool _wireJumpLatch;
        readonly List<BodyState> _snapBodies = new List<BodyState>(24);
        // Client: the emote each puppet was last posed with (a participant of a host round streams
        // its puppets to its spectators, and a puppet has no playing Celebration to read).
        readonly Dictionary<int, int> _posedEmote = new Dictionary<int, int>();
        readonly Dictionary<int, float> _posedPhase = new Dictionary<int, float>();

        /// <summary>
        /// A spectator view is mirroring a remote camera on this machine: every camera call of the
        /// driver (Scene.cs Cam*) stands down so the mirrored pose is never fought by a phase cut.
        /// Set and cleared by <see cref="CupSpectatorView"/>.
        /// </summary>
        public bool CamMirrored { get; set; }

        /// <summary>
        /// The emote a body should show on the wire: a simulated body's playing Celebration
        /// (Local / Host), or the emote its puppet was last posed with (Client). 255 = none.
        /// </summary>
        public bool TryGetWireEmote(CupBody b, out int emoteId, out float phase)
        {
            emoteId = 255;
            phase = 0f;
            if (b == null) return false;
            if (Authority == RoundAuthority.Client)
            {
                int id;
                if (_posedEmote.TryGetValue(b.VirtualSlot, out id) && id >= 0 && id != 255)
                {
                    emoteId = id;
                    float ph;
                    phase = _posedPhase.TryGetValue(b.VirtualSlot, out ph) ? ph : 0f;
                    return true;
                }
                return false;
            }
            if (b.Celeb != null && b.Celeb.Playing)
            {
                emoteId = (int)b.Celeb.CurrentEmote;
                phase = b.Celeb.Progress01;
                return true;
            }
            return false;
        }

        // ==========================================================================================
        // Binding
        // ==========================================================================================

        void NetEnsureBound()
        {
            if (_nsBound) return;
            if (Authority != RoundAuthority.Host && Authority != RoundAuthority.Client) return;
            var s = Multiplayer.Session;
            if (s == null || !s.Active) return;
            _ns = s;
            _nsBound = true;
            _nsHost = Authority == RoundAuthority.Host;
            _nsClient = Authority == RoundAuthority.Client;
            _replayWasPlaying = false;
            _snapAccum = _stateAccum = 0f;
            if (_nsHost)
            {
                _ns.ReplayVotesExternal = true;
                _ns.SkipVoteReceived += NetOnSkipVote;
                PhaseChanged += NetMarkStatePhase;
                KickResolved += NetMarkStateKick;
                RoundDecided += NetMarkStateDecided;
                _stateNow = true;   // the first record goes out on this tick
            }
            else
            {
                _ns.CupRoundStateReceived += NetOnRoundState;
                _ns.ReplayStarted += NetOnReplayStarted;
                _ns.ReplayEnded += NetOnReplayEnded;
                _posedEmote.Clear();
                _posedPhase.Clear();
            }
        }

        void NetUnbind()
        {
            if (!_nsBound) return;
            if (_ns != null)
            {
                if (_nsHost)
                {
                    _ns.SkipVoteReceived -= NetOnSkipVote;
                    _ns.ReplayVotesExternal = false;
                    // A round torn down mid-replay (Abort, End Match) must not leave clients in one.
                    if (_replayWasPlaying && _ns.IsHost && _ns.Active) _ns.EndReplayHost();
                }
                else
                {
                    _ns.CupRoundStateReceived -= NetOnRoundState;
                    _ns.ReplayStarted -= NetOnReplayStarted;
                    _ns.ReplayEnded -= NetOnReplayEnded;
                }
            }
            PhaseChanged -= NetMarkStatePhase;
            KickResolved -= NetMarkStateKick;
            RoundDecided -= NetMarkStateDecided;
            // The mirrored ball goes back to physics for whatever plays next (a Local round on
            // this peer, the next mode); the shared ball outlives every round.
            if (_ballMirrored && Setup != null && Setup.Ball != null && Setup.Ball.Rb != null) Setup.Ball.Rb.isKinematic = false;
            _ballMirrored = false;
            _replayWasPlaying = false;
            CamMirrored = false;
            _nsBound = _nsHost = _nsClient = false;
            _ns = null;
        }

        // Unity calls OnDisable before OnDestroy when the round root goes; the subscriptions must
        // not outlive the driver (OnTornDown is the Scene partial's and cannot be shared).
        void OnDisable() => NetUnbind();

        // ==========================================================================================
        // Host
        // ==========================================================================================

        /// <summary>After SimTick on the host: the replay bridge, the snapshot, the round record.</summary>
        void NetHostTick(float dt)
        {
            NetEnsureBound();
            if (!_nsBound || _ns == null || !_ns.Active) return;

            if (ReplayPlaying != _replayWasPlaying)
            {
                _replayWasPlaying = ReplayPlaying;
                if (ReplayPlaying) _ns.BeginReplay(); else _ns.EndReplayHost();
                _stateNow = true;   // SkipVotes / SkipVoters changed with it
            }

            _snapAccum += dt;
            if (_snapAccum >= SimConfig.NetSnapshotInterval)
            {
                _snapAccum = Mathf.Min(_snapAccum - SimConfig.NetSnapshotInterval, SimConfig.NetSnapshotInterval);
                NetBroadcastSnapshot();
            }

            _stateAccum += dt;
            if (_stateNow || _stateAccum >= CupNet.RoundStateInterval)
            {
                _stateAccum = 0f;
                _stateNow = false;
                _ns.BroadcastCupRoundState(BuildState().ToBytes());
            }
        }

        void NetMarkStatePhase(RoundPhase phase) => _stateNow = true;
        void NetMarkStateKick(KickOutcome outcome, CupSide side, int scorerSlot) => _stateNow = true;
        void NetMarkStateDecided(CupSide winner) => _stateNow = true;

        /// <summary>A human's replay-skip click (a client's through the session, the host's own directly through VoteSkipReplay).</summary>
        void NetOnSkipVote(int slot) => VoteSkipReplayBy(slot);

        /// <summary>Every visible body's pose + emote, keyed by virtual slot, and the ball.</summary>
        void NetBroadcastSnapshot()
        {
            if (!_sceneBuilt || Setup == null || Setup.Ball == null) return;
            _snapBodies.Clear();
            for (int i = 0; i < _bodies.Count && _snapBodies.Count < 255; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.Alive || b.Parked || b.VirtualSlot < 0 || b.VirtualSlot > 254) continue;
                Vector3 p = b.Pelvis.position;
                p.y = 0f;
                int emoteId; float phase;
                bool emoting = TryGetWireEmote(b, out emoteId, out phase);
                _snapBodies.Add(new BodyState
                {
                    slot = (byte)b.VirtualSlot,
                    pos = p,
                    yaw = b.Ragdoll.FacingRotation.eulerAngles.y,
                    down = false,
                    emoteId = emoting ? (byte)Mathf.Clamp(emoteId, 0, 255) : (byte)255,
                    emotePhase = emoting ? (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(phase) * 255f), 0, 255) : (byte)0,
                    anim = (byte)NetAnimOf(b),
                    lastInputTick = b.IsHuman ? _ns.InputTickForSlot(b.Slot) : 0u,
                    erect = b.Ragdoll.Anatomy != null && b.Ragdoll.Anatomy.Erect,
                });
            }
            var ball = Setup.Ball;
            _ns.BroadcastSnapshot(new Snapshot
            {
                tick = Director != null ? Director.Tick : 0u,
                ballPos = ball.transform.position,
                ballVel = ball.Rb != null ? ball.Rb.linearVelocity : Vector3.zero,
                guided = ball.Guided,
                homeScore = (byte)Mathf.Clamp(ScoreA, 0, 255),
                awayScore = (byte)Mathf.Clamp(ScoreB, 0, 255),
                clockSec = 0,
                bodies = _snapBodies.ToArray(),
            });
        }

        /// <summary>The canned-animation hint the other drivers stream (a puppet consumer may use it; ApplyBodyPose today does not).</summary>
        static AnimState NetAnimOf(CupBody b)
        {
            if (b.Ragdoll == null) return AnimState.Idle;
            if (b.Keeper != null && b.Keeper.IsCommitting) return AnimState.Dive;
            if (b.Ai != null && b.Ai.WasDivingSave) return AnimState.Dive;
            if (b.Striker != null && (b.Striker.IsDiving || b.Striker.IsTumbling)) return AnimState.Down;
            if (b.Ragdoll.MoveInput.sqrMagnitude > 0.6f) return AnimState.Run;
            return AnimState.Idle;
        }

        // ==========================================================================================
        // Client
        // ==========================================================================================

        /// <summary>The client's frame: input out, the host's world in.</summary>
        void NetClientTick(float dt)
        {
            NetEnsureBound();
            if (!_nsBound || _ns == null || !_ns.Active) return;
            NetSendLocalInput();
            NetApplySnapshots();
        }

        /// <summary>
        /// The local human's frame for the host, sampled from the device the way every net driver
        /// does (GameInput.SampleFrame) and then gated exactly as the local CupLocalInput gates
        /// the device: while suspended, movement and buttons read idle, Space keeps its last held
        /// state so a release commits at the value the meter froze on when the gate lifts, and the
        /// one-shot emote pick is kept (the wheel that made it may still read as open this frame).
        /// </summary>
        void NetSendLocalInput()
        {
            var s = Setup;
            if (s == null || !s.LocalHasBody || s.Input == null) return;
            var cam = s.GameCam;
            float yaw = cam != null ? (LocalIsKeeper ? cam.KeeperLookYaw : cam.Yaw) : 0f;
            float pitch = cam != null ? cam.Pitch : 0f;
            var f = s.Input.SampleFrame(Director != null ? Director.Tick : 0u, yaw, pitch);
            f.reset = false;    // R never resets a cup round (CupLocalInput says the same)
            f.tackle = false;
            if (LocalInputSuspended)
            {
                f.move = Vector2.zero;
                f.jump = _wireJumpLatch;
                f.legL = f.legR = f.sprint = f.passGround = f.passLofted = f.passChip = false;
                f.closeControl = f.cross = f.thirdLeg = false;
            }
            else _wireJumpLatch = f.jump;
            _ns.SetLocalInput(f);
        }

        /// <summary>The host's bodies at (now - NetInterpDelay), onto the puppets; the ball kinematic on the interpolated position.</summary>
        void NetApplySnapshots()
        {
            if (!_sceneBuilt) return;
            Snapshot a, b;
            float f;
            if (!_ns.SampleInterpolated(SimConfig.NetInterpDelay, out a, out b, out f)) return;
            if (b.bodies != null)
            {
                for (int i = 0; i < b.bodies.Length; i++)
                {
                    var sb = b.bodies[i];
                    BodyState sa;
                    if (!NetFindBody(in a, sb.slot, out sa)) sa = sb;
                    Vector3 pos = Vector3.Lerp(sa.pos, sb.pos, f);
                    float yaw = Mathf.LerpAngle(sa.yaw, sb.yaw, f);
                    int eid = sb.emoteId == 255 ? -1 : sb.emoteId;
                    float phase = sb.emotePhase / 255f;
                    _posedEmote[sb.slot] = eid;
                    _posedPhase[sb.slot] = phase;
                    ApplyBodyPose(sb.slot, pos, yaw, eid, phase);
                    var cb = BodyByVirtualSlot(sb.slot);
                    if (cb != null && cb.Ragdoll != null && cb.Ragdoll.Anatomy != null) cb.Ragdoll.Anatomy.Erect = sb.erect;
                }
            }
            var ball = Setup != null ? Setup.Ball : null;
            if (ball != null && ball.Rb != null)
            {
                if (!_ballMirrored) { ball.Rb.isKinematic = true; _ballMirrored = true; }
                ball.Rb.position = Vector3.Lerp(a.ballPos, b.ballPos, f);
            }
        }

        static bool NetFindBody(in Snapshot s, int slot, out BodyState bs)
        {
            if (s.bodies != null)
                for (int i = 0; i < s.bodies.Length; i++)
                    if (s.bodies[i].slot == slot) { bs = s.bodies[i]; return true; }
            bs = default;
            return false;
        }

        /// <summary>The host's record for THIS round (another round's, or a malformed one, is dropped).</summary>
        void NetOnRoundState(byte[] record)
        {
            if (!_nsClient || !Configured || Data == null || record == null) return;
            CupRoundState s;
            try { s = CupRoundState.FromBytes(record); }
            catch (Exception e) { CupLog.Warn("CupRoundDriver: bad round state from the host (" + e.Message + ")"); return; }
            if (s.Stage != Data.Stage || s.RoundIndex != Data.Index) return;
            ApplyState(s);
        }

        void NetOnReplayStarted() => ClientSetReplay(true);
        void NetOnReplayEnded() => ClientSetReplay(false);
    }
}
