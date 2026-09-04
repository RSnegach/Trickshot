using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>What a body is doing in the CURRENT kick (a body's role changes kick by kick).</summary>
    public enum CupBodyRole : byte
    {
        /// <summary>On the ball this kick.</summary>
        Taker = 0,
        /// <summary>On the line this kick.</summary>
        Keeper = 1,
        /// <summary>Standing in a lineup (or parked hidden, when it is a human's other body).</summary>
        Lineup = 2,
        /// <summary>The referee: never plays, never in a lineup.</summary>
        Referee = 3,
    }

    /// <summary>
    /// One body in a cup round, as the driver, the HUD, the choreography and the coin toss see it.
    /// A HUMAN who both takes and keeps (Solo, Head to Head) owns TWO of these with the same
    /// <see cref="Slot"/> - a shooter body and a gloved keeper body, because gloves and keeper
    /// hitboxes are baked at Build (design 7.3) - and only one is <see cref="Active"/> per kick;
    /// the other is parked hidden behind the goal. Co-op humans, AI bodies and the referee are one
    /// body each. Plain fields: the driver writes them, everyone else reads.
    /// </summary>
    public sealed class CupBody
    {
        public CupSide Side;
        /// <summary>The human's net slot (Solo: 0); -1 = an AI body (or the referee).</summary>
        public int Slot = -1;
        /// <summary>
        /// The wire body id (CupRoundState.TakerBodyId / KeeperBodyId, the snapshot's slot byte):
        /// a human's PRIMARY body is its slot (0..7); a human's second body, every AI body and the
        /// referee take ids from CupRoundState.AiBodyIdBase up, in spawn order.
        /// </summary>
        public int VirtualSlot;
        public bool IsHuman => Slot >= 0;
        /// <summary>What the body does in the current kick.</summary>
        public CupBodyRole Role = CupBodyRole.Lineup;
        /// <summary>Built with gloves + keeper hitboxes (fixed at Build; a shooter body can never keep).</summary>
        public bool IsKeeperBody;
        public ActiveRagdoll Ragdoll;
        /// <summary>Shooter bodies of humans: locomotion + kick detectors for the free windows.</summary>
        public Striker Striker;
        /// <summary>A human keeper body's controller.</summary>
        public KeeperController Keeper;
        /// <summary>An AI keeper body's brain.</summary>
        public Goalkeeper Ai;
        /// <summary>An AI shooter body's taker input.</summary>
        public CupBotTaker Bot;
        public Celebration Celeb;
        /// <summary>Host authority: a REMOTE human's wire input (shared by both of that human's bodies).</summary>
        public NetInputSource NetInput;
        /// <summary>Position in its side's lineup (a human's two bodies share one).</summary>
        public int LineupIndex;
        public Vector3 LineupMark;
        public Quaternion LineupFacing = Quaternion.identity;
        /// <summary>Live and visible this kick (false = the human's other body is out instead).</summary>
        public bool Active = true;
        /// <summary>Parked hidden behind the goal right now (a display body, renderers off).</summary>
        public bool Parked;
        /// <summary>Nation index (CupNations), -1 when unresolved (the referee).</summary>
        public int Nation = -1;
        public string Name = "";
        public GameObject Go;
        /// <summary>The body may run and emote right now (a free window); the driver ticks its Striker.</summary>
        public bool Freed;
        /// <summary>
        /// The human who owned this body left mid-round (CupRoundDriver.HumanLeft): retired for
        /// good - parked hidden at the next placement, never placed, never counted as a voter,
        /// never streamed. A leaving Co-op keeper's gloved body is re-slotted instead of retired.
        /// </summary>
        public bool Gone;

        public Transform Pelvis => Ragdoll != null && Ragdoll.Pelvis != null ? Ragdoll.Pelvis.transform : null;
        /// <summary>Feet-level position (the pelvis projected to the turf), or the lineup mark when there is no body.</summary>
        public Vector3 GroundPos
        {
            get
            {
                var p = Pelvis;
                if (p == null) return LineupMark;
                return new Vector3(p.position.x, 0f, p.position.z);
            }
        }
        public bool Alive => Ragdoll != null && Ragdoll.Pelvis != null;

        public override string ToString()
        {
            return (IsHuman ? "slot " + Slot : "AI") + " " + Name + " side " + CupSides.Name(Side) + " " + Role
                   + (IsKeeperBody ? " [gloves]" : "") + (Active ? "" : " (parked)") + " v" + VirtualSlot;
        }
    }

    /// <summary>
    /// The local device behind a gate: every button reads idle while the pause menu is up (design
    /// 6.10 / 12.1 answer 20: in a networked cup the overlay pause cuts the LOCAL input but the sim
    /// and the kick clock keep running), the emote wheel is open (its click must not read as a leg
    /// raise or a keeper lunge - the same guard MatchGame keeps) or a replay is playing.
    ///
    /// Space is LATCHED rather than cut: SetPieceTaker commits a shot on the RELEASE edge, so a
    /// charge that reads "up" the instant Esc is pressed would fire the kick into the pause menu.
    /// While gated, JumpHeld keeps reporting whatever it last read; when the gate lifts a genuine
    /// release then commits at the value the meter froze on.
    /// </summary>
    public sealed class CupLocalInput : IStrikerInput
    {
        readonly IStrikerInput _inner;
        readonly Func<bool> _gated;
        bool _jumpLatch;

        public CupLocalInput(IStrikerInput inner, Func<bool> gated)
        {
            _inner = inner;
            _gated = gated ?? (() => false);
        }

        public IStrikerInput Inner => _inner;
        bool Gated => _inner == null || _gated();

        public Vector2 Move => Gated ? Vector2.zero : _inner.Move;
        public float Scroll => Gated ? 0f : _inner.Scroll;
        public bool SprintHeld => !Gated && _inner.SprintHeld;
        public bool CloseControlHeld => !Gated && _inner.CloseControlHeld;
        public bool JumpPressed => !Gated && _inner.JumpPressed;
        public bool JumpHeld
        {
            get
            {
                if (Gated) return _jumpLatch;
                _jumpLatch = _inner.JumpHeld;
                return _jumpLatch;
            }
        }
        public bool JumpReleased => !Gated && _inner.JumpReleased;
        public bool LeftLegHeld => !Gated && _inner.LeftLegHeld;
        public bool RightLegHeld => !Gated && _inner.RightLegHeld;
        public bool ResetPressed => false;   // R never resets a cup round
        public bool LeftClickPressed => !Gated && _inner.LeftClickPressed;
        public bool RightClickPressed => !Gated && _inner.RightClickPressed;
        public bool PassGroundPressed => !Gated && _inner.PassGroundPressed;
        public bool PassLoftedPressed => !Gated && _inner.PassLoftedPressed;
        public bool PassGroundHeld => !Gated && _inner.PassGroundHeld;
        public bool PassLoftedHeld => !Gated && _inner.PassLoftedHeld;
        public bool PassGroundReleased => !Gated && _inner.PassGroundReleased;
        public bool PassLoftedReleased => !Gated && _inner.PassLoftedReleased;
        public bool PassChipPressed => !Gated && _inner.PassChipPressed;
        public bool PassChipHeld => !Gated && _inner.PassChipHeld;
        public bool PassChipReleased => !Gated && _inner.PassChipReleased;
        public bool Fresh => true;
        public int EmoteId => Gated ? 255 : _inner.EmoteId;
        public bool CrossPressed => false;
        public bool ThirdLegHeld => !Gated && _inner.ThirdLegHeld;
    }

    /// <summary>
    /// The materials and textures ONE round paints: a nation kit per nation (shared by every body
    /// wearing it), the referee's stripes, and a per-body limb material (Build tints a human's limb
    /// material to their skin, so each human body needs its own). Nothing here is freed by Unity
    /// when the GameObjects die - Make.* hands back fresh objects - so the driver calls Free() from
    /// OnDestroy, the way every other builder in the project does (see the build notes).
    /// </summary>
    public sealed class CupKitCache
    {
        readonly List<Material> _mats = new List<Material>();
        readonly List<Texture2D> _texs = new List<Texture2D>();
        readonly Dictionary<int, Material> _nation = new Dictionary<int, Material>();
        Material _referee;

        /// <summary>
        /// The torso material of a nation: its jersey design painted on a full atlas over its
        /// primary kit colour (so the atlas's plain side band matches the design), cached per
        /// nation for the round. `fallback` (the director's torso material) when the nation is
        /// unresolved - never null.
        /// </summary>
        public Material Nation(int nationIndex, Material fallback)
        {
            if (nationIndex < 0 || !CupNations.IsValid(nationIndex)) return fallback;
            Material m;
            if (_nation.TryGetValue(nationIndex, out m) && m != null) return m;
            var design = CupNations.Design(nationIndex);
            if (design == null) return fallback;
            var tex = Paint(design, CupNations.PrimaryColor(nationIndex));
            m = Make.MatTex(tex);
            _texs.Add(tex);
            _mats.Add(m);
            _nation[nationIndex] = m;
            return m;
        }

        /// <summary>The referee's black-and-white stripes (JerseyDesigns.RefereeName), or a plain black torso if the design is missing.</summary>
        public Material Referee()
        {
            if (_referee != null) return _referee;
            var design = JerseyDesigns.Find(JerseyDesigns.RefereeName);
            if (design != null)
            {
                var tex = Paint(design, Color.black);
                _texs.Add(tex);
                _referee = Make.MatTex(tex);
            }
            else
            {
                CupLog.Warn("CupKitCache: no '" + JerseyDesigns.RefereeName + "' jersey design - the referee wears plain black");
                _referee = Make.Mat(new Color(0.08f, 0.08f, 0.09f));
            }
            _mats.Add(_referee);
            return _referee;
        }

        /// <summary>A fresh limb material in a colour (tracked). One per human body: Build tints it to the skin.</summary>
        public Material Limb(Color c)
        {
            var m = Make.Mat(c);
            _mats.Add(m);
            return m;
        }

        /// <summary>
        /// Paint a jersey design onto a full torso atlas (JerseyDesigns.AtlasW x AtlasH) over a
        /// base colour, exactly as CustomizeUI.BuildCanvas does for the player's own kit: the base
        /// fills every row (the plain side band included), then the design fills the front and back
        /// regions. No name or number - a nation kit is the nation's, not the player's.
        /// </summary>
        public static Texture2D Paint(Design design, Color32 baseColour)
        {
            int w = JerseyDesigns.AtlasW, h = JerseyDesigns.AtlasH;
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = baseColour;
            if (design != null && design.Apply != null) design.Apply(px);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        public void Free()
        {
            for (int i = 0; i < _mats.Count; i++) if (_mats[i] != null) UnityEngine.Object.Destroy(_mats[i]);
            for (int i = 0; i < _texs.Count; i++) if (_texs[i] != null) UnityEngine.Object.Destroy(_texs[i]);
            _mats.Clear();
            _texs.Clear();
            _nation.Clear();
            _referee = null;
        }
    }

    /// <summary>
    /// The body builders of a cup round (design 7.3), each one the idiom the existing modes use:
    /// GameBootstrap.BuildStrikerPlayer / BuildAiKeeper and NetSetPieceMatch.SpawnBody. Every
    /// body hangs under the round root, so one Destroy of the root cascades; only the materials
    /// need explicit freeing (<see cref="CupKitCache"/>).
    /// </summary>
    public static class CupBodies
    {
        /// <summary>The limb colour AI bodies wear when a nation has no secondary colour to offer.</summary>
        public static readonly Color AiLimbFallback = new Color(0.15f, 0.32f, 0.6f);
        /// <summary>The referee's limbs: black shorts, socks and sleeves (the torso atlas cannot draw cuffs).</summary>
        public static readonly Color RefereeLimb = new Color(0.08f, 0.08f, 0.09f);

        /// <summary>
        /// A human's body: their own look (skin, hair, face, species) under the NATION kit (design
        /// 2.4: the custom jersey is replaced by the nation kit in every style). The local player is
        /// built to their own height / girth / mass exactly like BuildStrikerPlayer; a remote human
        /// is built at scale 1 because the sliders are not on the wire (NetSetPieceMatch does the
        /// same). Gloves make it a KEEPER body.
        /// </summary>
        public static ActiveRagdoll BuildHuman(GameObject go, Vector3 feet, Quaternion facing, Material torso, Material limb,
                                               bool gloves, PlayerAppearance look, bool localProfileScale)
        {
            var rag = go.AddComponent<ActiveRagdoll>();
            if (localProfileScale)
                rag.BuildScaled(feet, facing, torso, limb, PlayerProfile.HeightScale, PlayerProfile.GirthScale,
                                PlayerProfile.EffectiveMassMul, withGloves: gloves, appearance: look);
            else
                rag.Build(feet, facing, torso, limb, withGloves: gloves, appearance: look);
            return rag;
        }

        /// <summary>A plain AI body (no cosmetics, scale 1) in a torso + limb material pair.</summary>
        public static ActiveRagdoll BuildAi(GameObject go, Vector3 feet, Quaternion facing, Material torso, Material limb, bool gloves)
        {
            var rag = go.AddComponent<ActiveRagdoll>();
            rag.Build(feet, facing, torso, limb, withGloves: gloves, appearance: null);
            return rag;
        }

        /// <summary>
        /// The AttachKick idiom (NetSetPieceMatch): a KickDetector on every strike bone of the
        /// layout, so a shooter body's bicycle contact classifies as a trick during a free window.
        /// </summary>
        public static void AttachKick(ActiveRagdoll ragdoll, Striker striker, BallController ball)
        {
            if (ragdoll == null || striker == null || ball == null) return;
            var strike = ragdoll.StrikeBones;
            for (int i = 0; i < strike.Length; i++)
            {
                var rb = ragdoll.Rb(strike[i]);
                if (rb == null) continue;
                rb.gameObject.AddComponent<KickDetector>().Init(striker, ragdoll, ball);
            }
        }

        /// <summary>The look a human body wears: the local profile's, or a roster slot's on the host (Default when the session cannot say).</summary>
        public static PlayerAppearance LookFor(int slot, int localSlot)
        {
            if (slot == localSlot) return PlayerProfile.Appearance;
            var s = Multiplayer.Session;
            if (s != null && slot >= 0 && slot < NetSession.MaxSlots)
            {
                var r = s.RosterSlot(slot);
                if (r.human) return r.appearance;
            }
            return PlayerAppearance.Default;
        }

        /// <summary>A human's display name: the cup player's, else the roster's, else "Player n".</summary>
        public static string NameFor(CupDirector director, int slot)
        {
            var p = director != null ? director.PlayerAt(slot) : null;
            if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
            var s = Multiplayer.Session;
            if (s != null && slot >= 0 && slot < NetSession.MaxSlots)
            {
                var r = s.RosterSlot(slot);
                if (r.human && !string.IsNullOrEmpty(r.name)) return r.name;
            }
            return slot == 0 && (s == null || !s.Active) ? PlayerProfile.PlayerName : "Player " + slot;
        }

        /// <summary>
        /// Park a body out of the kick: hidden behind the goal as a kinematic display body, its
        /// renderers off and the ball told to ignore it (Goalkeeper.Park's recipe). Cheap enough
        /// to leave the component enabled - one or two bodies per round.
        /// </summary>
        public static void Park(CupBody b, Vector3 hideSpot, BallController ball)
        {
            if (b == null || !b.Alive) return;
            if (b.Keeper != null) { b.Keeper.InputLocked = false; b.Keeper.ForceRecover(); }
            if (b.Striker != null) { b.Striker.ControlEnabled = false; b.Striker.ForceRecover(); }
            if (b.Celeb != null) b.Celeb.Cancel();
            b.Ragdoll.ResetTo(hideSpot, Quaternion.identity);
            b.Ragdoll.BecomeDisplayBody();
            Goalkeeper.SetVisible(b.Ragdoll, false);
            if (ball != null) ball.IgnoreBody(b.Ragdoll, true);
            b.Parked = true;
            b.Active = false;
            b.Freed = false;
        }

        /// <summary>The inverse of Park: live again, visible, standing at a spot with a facing.</summary>
        public static void Unpark(CupBody b, Vector3 spot, Quaternion facing, BallController ball)
        {
            if (b == null || !b.Alive) return;
            if (b.Parked)
            {
                Goalkeeper.SetVisible(b.Ragdoll, true);
                b.Ragdoll.BecomeLiveBody();
                b.Parked = false;
            }
            Stand(b, spot, facing, ball);
            b.Active = true;
        }

        /// <summary>
        /// Stand a live body at a spot: the controller is recovered first (a dive or a trick in
        /// progress must not survive a teleport), then the ragdoll is snapped, then the ball is
        /// told to collide with it again (the caller re-ignores the taker / the referee).
        /// </summary>
        public static void Stand(CupBody b, Vector3 spot, Quaternion facing, BallController ball)
        {
            if (b == null || !b.Alive) return;
            if (b.Celeb != null) b.Celeb.Cancel();
            if (b.Keeper != null) { b.Keeper.InputLocked = false; b.Keeper.ForceRecover(); }
            if (b.Striker != null) { b.Striker.ControlEnabled = false; b.Striker.ForceRecover(); }
            // An AI keeper's own ResetTo clears his dive / hold state and moves his home, but it
            // also forces his line facing (-Z); the body is re-snapped with the facing ASKED for,
            // so he faces the goal when he stands in the lineup on his own side's kicks. On the
            // line the caller passes KeeperFacing and the two agree.
            if (b.Ai != null) b.Ai.ResetTo(spot);
            b.Ragdoll.ResetTo(spot, facing);
            if (ball != null) ball.IgnoreBody(b.Ragdoll, false);
            b.Freed = false;
        }

        /// <summary>
        /// Open a free window for a body: its Striker takes input again and the ragdoll may
        /// locomote (the scorer's 5 s, the winners' beat). AI bodies have no Striker; the
        /// choreography moves them.
        /// </summary>
        public static void Free(CupBody b)
        {
            if (b == null || !b.Alive) return;
            b.Freed = true;
            b.Ragdoll.LocomotionEnabled = true;
            b.Ragdoll.UprightLock = true;
            b.Ragdoll.BalanceEnabled = true;
            if (b.Striker != null) b.Striker.ControlEnabled = true;
        }

        /// <summary>Close a free window: control off, the body left standing where it is.</summary>
        public static void Hold(CupBody b)
        {
            if (b == null || !b.Alive) return;
            b.Freed = false;
            if (b.Striker != null)
            {
                b.Striker.ControlEnabled = false;
                b.Striker.ForceRecover();
            }
            b.Ragdoll.MoveInput = Vector3.zero;
        }
    }
}
