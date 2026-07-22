using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Networked SCRIMMAGE (host-authoritative), capped to fit the 8-slot model:
    ///   slots 0-3 = HOME (0 = keeper, 1-3 = outfield), slots 4-7 = AWAY (4 = keeper, 5-7 = outfield).
    /// Team is a pure function of slot (slot &lt; 4 = Home), so nothing on the wire carries a team bit.
    /// That is up to 4-a-side incl. keepers (3v3 + keepers); larger scrimmages stay single-player.
    ///
    /// The HOST reuses a real ScrimmageGame to run the whole sim (ball, AI, possession, passing,
    /// tackles, goals, clock, kickoff) - it just marks the networked human slots so ScrimmageGame
    /// leaves them to net-driven control instead of AI, and it does not own the camera/HUD/local
    /// input (this driver does). The host then streams a snapshot per body + ball + score + clock.
    /// Clients build the same slot bodies as kinematic puppets and render them from the snapshot
    /// interpolation buffer; the local player's own body is client-predicted + reconciled. Mirrors
    /// NetStrikerMatch's structure and reuses its interpolation / anim-state / reconciliation.
    /// </summary>
    public class NetScrimmageMatch : MonoBehaviour
    {
        class Body
        {
            public ActiveRagdoll ragdoll;
            public Footballer footballer;   // the AI/team body (host + client both build one)
            public Striker striker;         // outfield control (human slots)
            public KeeperController keeper;  // human keeper control
            public Celebration celeb;        // emote driver (host sim + local owner)
            public NetInputSource netInput;  // host: remote slots' input adapter
            public bool isKeeper;
            public int team;                 // 0 = Home, 1 = Away
            public bool wasHuman;
            // client anim/interp
            public float animPhase;
            public Vector3 lastInterpPos;
            public bool hasLastInterp;
        }

        GameInput _input;
        GameCamera _cam;
        BallController _ball;
        Transform _root;
        NetSession _s;
        ScrimmageGame _game;   // host only: the real sim
        ScrimmageArena.Refs _arena;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;
        bool _localIsKeeper;
        uint _tick; float _snapAccum;
        string _flash = ""; float _flashTime;

        // Slot conventions for the capped two-team board.
        const int HomeKeeper = 0, AwayKeeper = 4, TeamSplit = 4;
        static int TeamOfSlot(int slot) => slot < TeamSplit ? 0 : 1;
        static bool KeeperSlot(int slot) => slot == HomeKeeper || slot == AwayKeeper;

        int _perSide = 4;   // players per side incl keeper (capped 2..4 to fit the 8-slot board)

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                              Material homeTorso, Material homeLimb, Material awayTorso, Material awayLimb,
                              Material glove, Transform root, ScrimmageArena.Refs arena, int perSide)
        {
            _input = input; _cam = gameCam; _ball = ball; _root = root; _arena = arena;
            _perSide = Mathf.Clamp(perSide, 2, 4);
            _s = Multiplayer.Session;
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _s.MatchEvent += OnMatchEvent;
            _s.JerseyUpdated += OnJerseyUpdated;
            _s.RosterChanged += OnRosterChanged;

            // HOST: create the ScrimmageGame component FIRST so the Footballers built in SpawnBody
            // can take a valid game ref (Footballer reads _game.HomeGoal/PossessionTeam in AiTick).
            // It is Configured below, after all bodies exist.
            if (_s.IsHost)
            {
                var gmGo = new GameObject("NetScrimmageSim");
                gmGo.transform.SetParent(_root, true);
                _game = gmGo.AddComponent<ScrimmageGame>();
                _game.ConfigureNetHost();
            }

            // Build a body per active slot (host + client both). Team + role are derived from slot.
            for (int slot = 0; slot < NetSession.MaxSlots; slot++)
                SpawnBody(slot, homeTorso, homeLimb, awayTorso, awayLimb, glove);

            // Camera follows the local body.
            var me = _bodies[_localSlot];
            _localIsKeeper = me != null && me.isKeeper;
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                if (_localIsKeeper)
                    _cam.SetKeeperFollow(me.ragdoll.Pelvis.transform,
                                         () => Quaternion.LookRotation(KeeperFace(_localSlot), Vector3.up),
                                         () => _input.Look);
                else
                {
                    _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look);
                    if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw);
                }
            }

            // HOST: Configure the (already-created) ScrimmageGame over the spawned bodies to run
            // the full sim, marking the networked human slots so it leaves them to net control.
            if (_s.IsHost)
            {
                var home = new List<Footballer>();
                var away = new List<Footballer>();
                Footballer homeKeeper = null, awayKeeper = null;
                for (int slot = 0; slot < NetSession.MaxSlots; slot++)
                {
                    var b = _bodies[slot];
                    if (b == null || b.footballer == null) continue;
                    if (b.isKeeper) { if (b.team == 0) homeKeeper = b.footballer; else awayKeeper = b.footballer; }
                    else (b.team == 0 ? home : away).Add(b.footballer);
                    if (b.striker != null || b.keeper != null) _game.MarkNetControlled(b.footballer);   // net-driven, not AI
                }
                // Outfield role is nominal (the net host owns control per slot).
                _game.Configure(_input, _ball, _cam, _arena, SimConfig.ScrimRole.Outfield,
                                home, away, homeKeeper, awayKeeper, null, null, null, null);
            }

            LockCursor();
        }

        // Facing a keeper slot defends: Home keeper (slot 0) defends the -Z (away) goal, Away keeper
        // the +Z. This matches ScrimmageGame.KeeperSpot orientation.
        Vector3 KeeperFace(int slot) => TeamOfSlot(slot) == 0 ? new Vector3(0f, 0f, -1f) : new Vector3(0f, 0f, 1f);

        void SpawnBody(int slot, Material homeTorso, Material homeLimb, Material awayTorso, Material awayLimb, Material glove)
        {
            var rosterSlot = _s.RosterSlot(slot);
            bool human = rosterSlot.human;
            bool ai = rosterSlot.ai;
            bool isLocal = slot == _localSlot;
            if (!human && !ai && !isLocal) return;   // empty slot: no body
            // Respect the per-side cap: a slot whose in-team index is beyond perSide only spawns if
            // a human actually holds it (so AI-default fill doesn't put 4v4 on a 3v3-sized pitch).
            int teamIndex = slot < TeamSplit ? slot : slot - TeamSplit;   // 0 = keeper, 1.. = outfield
            if (teamIndex >= _perSide && !human && !isLocal) return;

            int team = TeamOfSlot(slot);
            bool keeper = KeeperSlot(slot);
            bool hostSim = _s.IsHost;
            float attackZ = team == 0 ? 1f : -1f;
            var facing = Quaternion.LookRotation(new Vector3(0f, 0f, attackZ), Vector3.up);
            Vector3 start = new Vector3((slot % 4 - 1.5f) * 2f, 0f, attackZ * -_arena.halfLength * 0.3f);

            var go = new GameObject((team == 0 ? "Home" : "Away") + (keeper ? "GK" : "P" + slot));
            go.transform.SetParent(_root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();

            // Kit: Home slots wear the (painted) home kit + per-slot networked jersey when present;
            // Away slots wear the away kit. Human slots get a skin-tinted own-copy limb + cosmetics.
            Material teamTorso = team == 0 ? homeTorso : awayTorso;
            Material teamLimb  = team == 0 ? homeLimb  : awayLimb;
            Material slotLimb = human ? Make.Mat(rosterSlot.appearance.Skin) : teamLimb;
            PlayerAppearance? appr = human ? rosterSlot.appearance : (PlayerAppearance?)null;
            Texture2D jt = human ? _s.JerseyForSlot(slot) : null;
            Material slotTorso = (team == 0 && jt != null) ? Make.MatTex(jt) : teamTorso;
            ragdoll.Build(start, facing, slotTorso, slotLimb, withGloves: keeper && glove != null, appearance: appr);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, team = team, wasHuman = human };

            // Every body gets a Footballer (the host ScrimmageGame drives AI ones; a Footballer is
            // harmless on a client puppet - it is never ticked there). Attach a Striker/Dribble too
            // so a human slot can be controlled, exactly like BuildFootballer does.
            var striker = go.AddComponent<Striker>();
            striker.Init(_input, ragdoll);   // input is only read when ControlEnabled; puppets never tick
            striker.ControlEnabled = false;
            var dribble = go.AddComponent<Dribble>();
            dribble.Init(_input, striker, ragdoll, _ball);
            dribble.Enabled = false;
            striker.SetDribble(dribble);
            var celeb = go.AddComponent<Celebration>(); celeb.Init(ragdoll); b.celeb = celeb;
            go.AddComponent<Knockdown>().Init(ragdoll);

            if (hostSim)
            {
                AttachKickDetectors(ragdoll, striker);
                var f = go.AddComponent<Footballer>();
                f.Init(_game, _ball, ragdoll, team, keeper, attackZ, Vector3.zero);   // _game exists (created before spawn)
                b.footballer = f;

                if (human)
                {
                    // Networked human slot: drive its controller from local device or the wire.
                    if (keeper)
                    {
                        var kc = go.AddComponent<KeeperController>();
                        if (isLocal) kc.Init(_input, ragdoll);
                        else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll); }
                        kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.KeeperLookYaw)
                                                    : (() => b.netInput != null ? b.netInput.LookYaw : 0f));
                        b.keeper = kc;
                    }
                    else
                    {
                        if (isLocal) { striker.SetInput(_input); }
                        else { b.netInput = new NetInputSource(); striker.SetInput(b.netInput); }
                        striker.ControlEnabled = true;
                        b.striker = striker;
                    }
                }
            }
            else
            {
                // Client: only the local body is a live predicted ragdoll; everyone else is a puppet.
                if (isLocal)
                {
                    var f = go.AddComponent<Footballer>();
                    f.Init(null, _ball, ragdoll, team, keeper, attackZ, Vector3.zero);
                    b.footballer = f;
                    if (keeper)
                    {
                        var kc = go.AddComponent<KeeperController>(); kc.Init(_input, ragdoll);
                        kc.SetLookYawSource(() => _cam.KeeperLookYaw);
                        b.keeper = kc;
                    }
                    else { striker.SetInput(_input); striker.ControlEnabled = true; b.striker = striker; }
                }
                else ragdoll.BecomeDisplayBody();
            }

            _bodies[slot] = b;
        }

        void AttachKickDetectors(ActiveRagdoll ragdoll, Striker striker)
        {
            AddDet(ragdoll.Rb(Bone.FootR), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.CalfR), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.FootL), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.CalfL), striker, ragdoll);
        }
        void AddDet(Rigidbody rb, Striker striker, ActiveRagdoll ragdoll)
        {
            if (rb == null) return;
            rb.gameObject.AddComponent<KickDetector>().Init(striker, ragdoll, _ball);
        }

        void OnMatchEvent(string tag) { Flash(tag); }
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        void Update()
        {
            if (_s == null || PauseMenu.Paused) return;

            // Local emote + control for the local player's own body (client + host predict locally).
            var me = _bodies[_localSlot];
            if (me != null)
            {
                if (me.celeb != null && !me.celeb.Playing)
                {
                    int eid = _input.EmoteId;
                    if (eid >= 0 && eid != 255) me.celeb.Play((Celebration.Emote)eid);
                }
                bool emoting = me.celeb != null && me.celeb.Playing;
                if (!emoting)
                {
                    if (me.striker != null && me.striker.ControlEnabled) me.striker.Tick();
                    if (me.keeper != null) me.keeper.Tick();
                }
            }

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void HostUpdate()
        {
            // Feed remote human slots' networked input into their controllers (ticked by the local
            // control path for the host-local slot, and here for remote slots).
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || i == _localSlot) continue;
                if (b.netInput != null) b.netInput.Feed(_s.InputForSlot(i));
                // Start a remote player's emote from their wire pick so the host sims it and streams
                // id+phase to everyone (matches NetStrikerMatch). One-shot: EmoteId != 255 only on
                // the tick it changes.
                if (b.netInput != null && b.celeb != null && !b.celeb.Playing)
                {
                    int reid = b.netInput.EmoteId;
                    if (reid != 255) b.celeb.Play((Celebration.Emote)reid);
                }
                bool remoteEmoting = b.celeb != null && b.celeb.Playing;
                if (!remoteEmoting)
                {
                    if (b.striker != null && b.striker.ControlEnabled) b.striker.Tick();
                    if (b.keeper != null) b.keeper.Tick();
                }
            }
            // ScrimmageGame (its own Update) runs the ball/AI/possession/goals/clock this frame.

            PublishSnapshotIfDue();
        }

        void PublishSnapshotIfDue()
        {
            _snapAccum += Time.deltaTime;
            if (_snapAccum < SimConfig.NetSnapshotInterval) return;
            _snapAccum = 0f;
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw));
            BroadcastSnapshot();
            _tick++;
        }

        void BroadcastSnapshot()
        {
            var list = new List<BodyState>();
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || b.ragdoll == null || b.ragdoll.Pelvis == null) continue;
                Vector3 p = b.ragdoll.Pelvis.position; p.y = 0f;
                byte eid = 255, eph = 0;
                if (b.celeb != null && b.celeb.Playing) { eid = (byte)b.celeb.CurrentEmote; eph = (byte)Mathf.Clamp(Mathf.RoundToInt(b.celeb.Progress01 * 255f), 0, 255); }
                bool down = b.footballer != null && b.footballer.IsDown;
                list.Add(new BodyState
                {
                    slot = (byte)i, pos = p, yaw = b.ragdoll.FacingRotation.eulerAngles.y,
                    down = down, emoteId = eid, emotePhase = eph, anim = (byte)AnimStateOf(b),
                    lastInputTick = _s.InputTickForSlot(i),
                });
            }
            int home = _game != null ? _game.HomeScore : 0;
            int away = _game != null ? _game.AwayScore : 0;
            ushort clock = (ushort)(_game != null ? Mathf.Max(0, Mathf.RoundToInt(_game.ClockRemaining)) : 0);
            _s.BroadcastSnapshot(new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
                homeScore = (byte)Mathf.Min(255, home), awayScore = (byte)Mathf.Min(255, away),
                clockSec = clock, bodies = list.ToArray(),
            });
        }

        static AnimState AnimStateOf(Body b)
        {
            if (b.ragdoll == null) return AnimState.Idle;
            if (b.footballer != null && b.footballer.IsDown) return AnimState.Down;
            if (b.keeper != null && b.keeper.IsCommitting) return AnimState.Dive;
            if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            if (b.ragdoll.MoveInput.sqrMagnitude > 0.6f) return AnimState.Run;
            return AnimState.Idle;
        }

        int _homeScore, _awayScore, _clockSec;

        void ClientUpdate()
        {
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw));

            ReconcileLocalBody();

            if (!_s.SampleInterpolated(SimConfig.NetInterpDelay, out var a, out var bSnap, out float f))
                return;
            _homeScore = bSnap.homeScore; _awayScore = bSnap.awayScore; _clockSec = bSnap.clockSec;

            for (int i = 0; i < _bodies.Length; i++)
            {
                var body = _bodies[i];
                if (body == null || i == _localSlot) continue;
                if (!FindBody(a, i, out var sa)) continue;
                if (!FindBody(bSnap, i, out var sb)) sb = sa;
                Vector3 pos = Vector3.Lerp(sa.pos, sb.pos, f);
                float yaw = Mathf.LerpAngle(sa.yaw, sb.yaw, f);
                var facing = Quaternion.Euler(0f, yaw, 0f);
                byte emoteId = sb.emoteId != 255 ? sb.emoteId : sa.emoteId;
                if (emoteId != 255)
                {
                    float ephase = Mathf.Lerp(sa.emotePhase / 255f, sb.emotePhase / 255f, f);
                    body.ragdoll.DisplayEmote(pos, facing, emoteId, ephase);
                    continue;
                }
                float speed = 0f;
                if (body.hasLastInterp) { Vector3 d = pos - body.lastInterpPos; d.y = 0f; speed = d.magnitude / Mathf.Max(1e-4f, Time.deltaTime); }
                body.lastInterpPos = pos; body.hasLastInterp = true;
                float moveAmount = Mathf.Clamp01(speed / SimConfig.StrikerMoveSpeed);
                body.animPhase += Time.deltaTime * SimConfig.StrideRateMax * moveAmount / (2f * Mathf.PI);
                AnimState st = sb.down ? AnimState.Down : (AnimState)sb.anim;
                body.ragdoll.DisplayAnim(pos, facing, st, body.animPhase, moveAmount);
            }

            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(a.ballPos, bSnap.ballPos, f);
        }

        void ReconcileLocalBody()
        {
            var me = _bodies[_localSlot];
            if (me == null || me.ragdoll == null || me.ragdoll.Pelvis == null) return;
            if (!me.ragdoll.IsGrounded) return;
            if (!_s.HasSnapshot) return;
            if (!FindBody(_s.LatestSnapshot, _localSlot, out var auth)) return;
            Vector3 pred = me.ragdoll.Pelvis.position; pred.y = 0f;
            Vector3 target = auth.pos; target.y = 0f;
            Vector3 err = target - pred;
            float d = err.magnitude;
            if (d < SimConfig.ReconcileDeadzone) return;
            if (d > SimConfig.ReconcileSnap) { me.ragdoll.ShiftAll(err); return; }
            me.ragdoll.ShiftAll(err * Mathf.Clamp01(SimConfig.ReconcileRate * Time.deltaTime));
        }

        static bool FindBody(in Snapshot s, int slot, out BodyState bs)
        {
            if (s.bodies != null)
                for (int i = 0; i < s.bodies.Length; i++)
                    if (s.bodies[i].slot == slot) { bs = s.bodies[i]; return true; }
            bs = default; return false;
        }

        void OnJerseyUpdated(int slot)
        {
            if (slot < 0 || slot >= _bodies.Length) return;
            var b = _bodies[slot];
            if (b == null || b.ragdoll == null || b.team != 0) return;   // only Home wears painted kits
            var tex = _s.JerseyForSlot(slot);
            if (tex != null) b.ragdoll.SetTorsoMaterial(Make.MatTex(tex));
        }

        void OnRosterChanged()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.wasHuman) continue;
                if (_s.RosterSlot(i).human) continue;   // still human
                // Human left: hand the body back to AI (host) / keep puppeting (client). Removing
                // it from the ScrimmageGame's net-controlled set lets the AI loop resume driving it.
                b.wasHuman = false; b.netInput = null;
                if (b.striker != null) { b.striker.ControlEnabled = false; b.striker = null; }
                b.keeper = null;
                if (_game != null && b.footballer != null) _game.UnmarkNetControlled(b.footballer);
            }
        }

        void OnDestroy()
        {
            if (_s != null) { _s.MatchEvent -= OnMatchEvent; _s.JerseyUpdated -= OnJerseyUpdated; _s.RosterChanged -= OnRosterChanged; }
            if (_ball != null && _ball.Rb != null) _ball.Rb.isKinematic = false;
        }

        static void LockCursor() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        void OnGUI()
        {
            if (_s == null) return;
            Hud.Begin();
            int home = _s.IsHost && _game != null ? _game.HomeScore : _homeScore;
            int away = _s.IsHost && _game != null ? _game.AwayScore : _awayScore;
            float clock = _s.IsHost && _game != null ? _game.ClockRemaining : _clockSec;
            var score = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, 12f, Screen.width, 40f), $"HOME {home} - {away} AWAY", score);
            int cs = Mathf.Max(0, Mathf.RoundToInt(clock));
            var clk = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1f, 0.9f, 0.4f) } };
            GUI.Label(new Rect(0, 50f, Screen.width, 28f), $"{cs / 60}:{cs % 60:00}", clk);
            Hud.Legend(_localIsKeeper ? "WASD move   Mouse aim   LMB/RMB dive   Space jump"
                                      : "WASD move   Mouse aim   LMB/RMB legs   C tackle   B emote   V ball cam");
            Hud.Flash(_flash, _flashTime / 1.6f);
        }
    }
}
