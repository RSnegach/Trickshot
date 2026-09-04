using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Spectate = the spectated player's EXACT view (design 4). The owner of a round streams its
    /// camera pose, its ball and its bodies at 20 Hz (CupStream); the host relays it to whoever
    /// pressed Spectate on that row; this view renders it on the spectator's machine:
    ///
    ///   - the camera through <see cref="CupCameraRig.MirrorView"/> (eased between samples);
    ///   - when this machine has NO round of its own standing (a lobby watcher of a Head to Head
    ///     parallel round), display puppets built from the stream's body list - the nation kits
    ///     from the stream's two nations, a human's look from the roster, gloves and the referee's
    ///     stripes from the body flags - interpolated between the last two samples, and the shared
    ///     ball mirrored kinematic; a small scoreboard from the spectated player's live row;
    ///   - when this machine DOES have a Client-authority driver for the same host-simulated
    ///     round (a participant of, or a watcher seated in, a human-vs-human round), the driver's
    ///     puppets already follow the host's snapshots, so only the camera is mirrored - and the
    ///     driver's own camera cuts are held off through <see cref="CupRoundDriver.CamMirrored"/>.
    ///
    /// No control beyond Esc back to the lobby (director.StopSpectating). Owned by
    /// CupDirector.Net (created on the local player's SpectatingSlot, closed when it clears).
    /// </summary>
    public sealed class CupSpectatorView : MonoBehaviour
    {
        public const string LegendText = "Esc  back to lobby";
        /// <summary>No stream for this long = the round is over or the owner stopped sending; the last pose holds.</summary>
        public const float StaleSeconds = 3f;
        public const int GuiDepth = 5;

        public static CupSpectatorView Instance { get; private set; }
        /// <summary>A view is up and watching a slot.</summary>
        public static bool Active => Instance != null && Instance._target >= 0 && !Instance._closed;
        /// <summary>PauseMenu polls Escape before IMGUI sees it: while a view is up (and one frame after it closes) Esc is the view's.</summary>
        public static bool EscapeOwned => Active || Time.frameCount <= _escFrame;
        static int _escFrame = -1;

        /// <summary>The slot being watched, -1 none.</summary>
        public int Target => _target;
        /// <summary>A stream arrived recently.</summary>
        public bool Receiving => _hasCur && Time.unscaledTime - _curAt < StaleSeconds;
        public int PuppetCount => _puppets.Count;

        CupDirector _d;
        CupCameraRig _rig;
        int _target = -1;
        CupKitCache _kits;
        Transform _root;
        bool _closed;
        readonly Dictionary<int, Puppet> _puppets = new Dictionary<int, Puppet>();
        readonly List<int> _seen = new List<int>(24);
        CupStreamMsg _prev, _cur;
        float _prevAt, _curAt;
        bool _hasPrev, _hasCur;
        uint _lastSeq;
        bool _ballMirrored;

        sealed class Puppet
        {
            public int VSlot;
            public byte Slot, Flags;
            public int Nation;
            public ActiveRagdoll Rag;
            public GameObject Go;
            public bool Visible = true;
        }

        public static CupSpectatorView Create(CupDirector d)
        {
            var go = new GameObject("CupSpectatorView");
            if (d != null) go.transform.SetParent(d.transform, false);
            var v = go.AddComponent<CupSpectatorView>();
            v._d = d;
            v._rig = d != null ? d.Rig : null;
            v._kits = new CupKitCache();
            var root = new GameObject("Puppets");
            root.transform.SetParent(go.transform, false);
            v._root = root.transform;
            Instance = v;
            return v;
        }

        /// <summary>Watch a slot (a change forgets the samples and the puppets: another round, other nations).</summary>
        public void SetTarget(int slot)
        {
            if (slot == _target) return;
            _target = slot;
            _hasPrev = _hasCur = false;
            _lastSeq = 0;
            ClearPuppets();
        }

        /// <summary>A relayed stream (the director filters nothing: this checks the sender).</summary>
        public void OnStream(in CupStreamMsg m)
        {
            if (_closed || m.fromSlot != _target) return;
            if (_hasCur && m.seq <= _lastSeq) return;   // reordered / stale (UDP)
            _lastSeq = m.seq;
            if (_hasCur) { _prev = _cur; _prevAt = _curAt; _hasPrev = true; }
            _cur = m;
            _curAt = Time.unscaledTime;
            _hasCur = true;
            if (_rig != null) _rig.MirrorView(m.camPos, m.camRot, m.camFov);
            var drv = _d != null ? _d.Driver : null;
            if (drv != null) drv.CamMirrored = true;
        }

        void Update()
        {
            if (_closed) return;
            if (_d == null) { Close(); return; }
            var drv = _d.Driver;
            if (drv != null) drv.CamMirrored = true;

            // Esc back to the lobby (design 4): the only control a spectator has.
            if (_target >= 0 && !PauseMenu.Paused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _escFrame = Time.frameCount + 1;
                _d.StopSpectating();
                return;
            }
            if (!_hasCur) return;

            // A standing round of our own follows the host's snapshots itself; puppets here would
            // double it. Show ours only when nothing else draws the round.
            bool own = drv == null;
            if (!own)
            {
                HidePuppets();
                ReleaseBall();
                return;
            }
            float t = 1f;
            if (_hasPrev && _curAt > _prevAt)
            {
                float render = Time.unscaledTime - SimConfig.NetInterpDelay;
                t = Mathf.Clamp01((render - _prevAt) / (_curAt - _prevAt));
            }
            PoseBodies(t);
            MirrorBall(t);
        }

        void PoseBodies(float t)
        {
            var bodies = _cur.bodies;
            _seen.Clear();
            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    var cb = bodies[i];
                    var pup = Ensure(in cb);
                    if (pup == null || pup.Rag == null) continue;
                    Vector3 pos = cb.pos;
                    float yaw = cb.yaw;
                    CupStreamBody pb;
                    if (_hasPrev && FindBody(in _prev, cb.vslot, out pb))
                    {
                        pos = Vector3.Lerp(pb.pos, cb.pos, t);
                        yaw = Mathf.LerpAngle(pb.yaw, cb.yaw, t);
                    }
                    var facing = Quaternion.Euler(0f, yaw, 0f);
                    if (!pup.Visible) { Goalkeeper.SetVisible(pup.Rag, true); pup.Visible = true; }
                    if (cb.emoteId != 255) pup.Rag.DisplayEmote(pos, facing, cb.emoteId, cb.emotePhase / 255f);
                    else pup.Rag.DisplaySnap(pos, facing);
                    _seen.Add(cb.vslot);
                }
            }
            // A body the owner stopped streaming (a parked twin) hides rather than freezing in shot.
            foreach (var kv in _puppets)
            {
                if (_seen.Contains(kv.Key) || !kv.Value.Visible) continue;
                Goalkeeper.SetVisible(kv.Value.Rag, false);
                kv.Value.Visible = false;
            }
        }

        static bool FindBody(in CupStreamMsg m, int vslot, out CupStreamBody b)
        {
            if (m.bodies != null)
                for (int i = 0; i < m.bodies.Length; i++)
                    if (m.bodies[i].vslot == vslot) { b = m.bodies[i]; return true; }
            b = default;
            return false;
        }

        /// <summary>The puppet for a streamed body, built on first sight: its nation's kit from the side flag, a human's look from the roster, gloves and stripes from the flags.</summary>
        Puppet Ensure(in CupStreamBody b)
        {
            Puppet p;
            if (_puppets.TryGetValue(b.vslot, out p) && p.Rag != null) return p;
            if (_d == null) return null;
            bool sideB = (b.flags & CupStreamBodyFlags.SideB) != 0;
            bool gloves = (b.flags & CupStreamBodyFlags.KeeperBody) != 0;
            bool referee = (b.flags & CupStreamBodyFlags.Referee) != 0;
            int nation = sideB ? _cur.nationB : _cur.nationA;
            var go = new GameObject("Puppet v" + b.vslot + (referee ? " referee" : gloves ? " keeper" : ""));
            go.transform.SetParent(_root, true);
            var facing = Quaternion.Euler(0f, b.yaw, 0f);
            ActiveRagdoll rag;
            if (referee)
            {
                rag = CupBodies.BuildAi(go, b.pos, facing, _kits.Referee(), _kits.Limb(CupBodies.RefereeLimb), false);
            }
            else if (b.slot != 255)
            {
                var look = CupBodies.LookFor(b.slot, _d.LocalSlot);
                rag = CupBodies.BuildHuman(go, b.pos, facing, _kits.Nation(nation, _d.Torso), _kits.Limb(look.Skin), gloves, look, false);
            }
            else
            {
                Color c = nation >= 0 && CupNations.IsValid(nation) ? CupNations.SecondaryColor(nation) : CupBodies.AiLimbFallback;
                rag = CupBodies.BuildAi(go, b.pos, facing, _kits.Nation(nation, _d.Torso), _kits.Limb(c), gloves);
            }
            rag.BecomeDisplayBody();
            p = new Puppet { VSlot = b.vslot, Slot = b.slot, Flags = b.flags, Nation = nation, Rag = rag, Go = go, Visible = true };
            _puppets[b.vslot] = p;
            return p;
        }

        void HidePuppets()
        {
            foreach (var kv in _puppets)
            {
                if (!kv.Value.Visible || kv.Value.Rag == null) continue;
                Goalkeeper.SetVisible(kv.Value.Rag, false);
                kv.Value.Visible = false;
            }
        }

        void ClearPuppets()
        {
            foreach (var kv in _puppets) if (kv.Value.Go != null) Destroy(kv.Value.Go);
            _puppets.Clear();
        }

        void MirrorBall(float t)
        {
            var ball = _d != null ? _d.Ball : null;
            if (ball == null || ball.Rb == null) return;
            if (!_ballMirrored) { ball.Rb.isKinematic = true; _ballMirrored = true; }
            Vector3 from = _hasPrev ? _prev.ballPos : _cur.ballPos;
            ball.Rb.position = Vector3.Lerp(from, _cur.ballPos, t);
        }

        void ReleaseBall()
        {
            if (!_ballMirrored) return;
            _ballMirrored = false;
            var ball = _d != null ? _d.Ball : null;
            if (ball != null && ball.Rb != null) ball.Rb.isKinematic = false;
        }

        // ---- the spectator's HUD: only when no round HUD of our own is bound --------------------

        void OnGUI()
        {
            if (_closed || _d == null || _target < 0 || PauseMenu.Paused || _d.Driver != null) return;
            GUI.depth = GuiDepth;
            Hud.Begin();
            var p = _d.PlayerAt(_target);
            if (p != null)
            {
                string home = p.Nation >= 0 && CupNations.IsValid(p.Nation) ? CupNations.Code(p.Nation) : "---";
                string away = p.LiveOpponentNation >= 0 && CupNations.IsValid(p.LiveOpponentNation) ? CupNations.Code(p.LiveOpponentNation) : "---";
                Color homeCol = p.Nation >= 0 && CupNations.IsValid(p.Nation) ? CupNations.PrimaryColor(p.Nation) : Color.white;
                Color awayCol = p.LiveOpponentNation >= 0 && CupNations.IsValid(p.LiveOpponentNation) ? CupNations.PrimaryColor(p.LiveOpponentNation) : Color.white;
                string sub = p.Playing ? "KICK " + Mathf.Max(1, p.LiveKick) : "";
                Hud.Scoreboard(home, homeCol, p.LiveScoreFor, p.LiveScoreAgainst, away, awayCol, -1f, false, sub);
                var panel = Hud.PanelStart(CupText.Title, 2);
                Hud.Stat(ref panel, "You", CupText.Watching(p.DisplayName));
                Hud.Stat(ref panel, "Nation", p.Nation >= 0 && CupNations.IsValid(p.Nation) ? CupNations.Name(p.Nation) : "-");
            }
            Hud.Legend(LegendText);
            Hud.End();
        }

        // ---- teardown ----------------------------------------------------------------------------

        /// <summary>Stop watching: puppets, kits and the mirrored ball go; the rig is handed back if it is still mirroring.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _escFrame = Time.frameCount + 1;
            ReleaseBall();
            ClearPuppets();
            if (_kits != null) { _kits.Free(); _kits = null; }
            var drv = _d != null ? _d.Driver : null;
            if (drv != null) drv.CamMirrored = false;
            if (_rig != null && _rig.Current == CupCameraRig.View.Mirror)
            {
                _rig.Release();
                // Back on a menu screen the rig shows the empty stadium; a round takes it itself.
                if (_d != null && _d.Phase != CupPhase.Round) _d.MenuBackdrop();
            }
            if (Instance == this) Instance = null;
            _target = -1;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (!_closed)
            {
                _closed = true;
                ReleaseBall();
                foreach (var kv in _puppets) if (kv.Value.Go != null) Destroy(kv.Value.Go);
                _puppets.Clear();
                if (_kits != null) { _kits.Free(); _kits = null; }
                var drv = _d != null ? _d.Driver : null;
                if (drv != null) drv.CamMirrored = false;
            }
            if (Instance == this) Instance = null;
        }
    }
}
