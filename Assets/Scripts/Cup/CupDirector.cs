using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The cup's top-level phases, shared by the three styles (not every style visits every
    /// phase - see the flow partials). Rides the wire as a byte in CupState: append, never reorder.
    /// </summary>
    public enum CupPhase : byte
    {
        /// <summary>CHOOSE YOUR NATION (design 6.1): pick / vote; Play Again returns here.</summary>
        NationPick = 0,
        /// <summary>The bracket screen (6.2): 5 s, no button; "THE DRAW" first, then the stage header.</summary>
        Bracket = 1,
        /// <summary>The loading card (6.4) over the round build; MP: the "everyone loaded" barrier.</summary>
        Loading = 2,
        /// <summary>The coin toss ceremony and the HEADS / TAILS calls (6.11, 7.1).</summary>
        CoinToss = 3,
        /// <summary>A round is being played by <see cref="CupRoundDriver"/> (or watched).</summary>
        Round = 4,
        /// <summary>Solo: the stage results ("Simulating the rest of the stage") before the cup lobby / bracket.</summary>
        StageComplete = 5,
        /// <summary>The cup lobby (6.3): rows, Spectate, View Bracket, Customize, Ready, Quit.</summary>
        Lobby = 6,
        /// <summary>Co-op: the shooting order screen (6.8) before every stage.</summary>
        OrderPick = 7,
        /// <summary>Head to Head: "HEAD TO HEAD - up next" for the two participants of a human round.</summary>
        Interstitial = 8,
        /// <summary>Solo / Head to Head: the podium (8.1).</summary>
        Podium = 9,
        /// <summary>Co-op: the trophy lift cinematic and free window (8.2).</summary>
        TrophyLift = 10,
        /// <summary>Solo: the KNOCKED OUT card (6.7); Co-op: GAME OVER (6.6, lost).</summary>
        GameOver = 11,
        /// <summary>The results / CUP SUMMARY / CHAMPIONS table (6.6).</summary>
        Results = 12,
        /// <summary>The cup is over and the director is being torn down; nothing ticks.</summary>
        Ended = 13,
    }

    /// <summary>
    /// The client -> host requests of the cup (design 9.4, the CupRequest message). The director
    /// raises them through <see cref="CupDirector.RequestRaised"/>; the net layer serialises them.
    /// Rides the wire as a byte: append, never reorder.
    /// </summary>
    public enum CupRequestKind : byte
    {
        /// <summary>arg = nation index.</summary>
        PickNation = 0,
        /// <summary>arg = 1 ready / 0 not.</summary>
        Ready = 1,
        /// <summary>arg = the slot to watch.</summary>
        Spectate = 2,
        Unspectate = 3,
        /// <summary>payload = CupRound.WriteTo bytes of a finished locally-simulated round (Head to Head parallel phase).</summary>
        RoundResult = 4,
        /// <summary>The loading barrier ack.</summary>
        Loaded = 5,
        /// <summary>Reserved: the Captain is the host, so order changes never cross the wire as a request (they ride CupState).</summary>
        SetOrder = 6,
        /// <summary>arg = (int)CoinFace.</summary>
        CallCoin = 7,
        /// <summary>The scorer's / winning keeper's click to skip the open window.</summary>
        SkipCelebration = 8,
        /// <summary>Co-op, the Captain: the slot-machine lever (host-local today; a request for a non-host Captain).</summary>
        PullLever = 9,
        /// <summary>Co-op, the Captain: everyone picked without a majority, the Captain's pick decides.</summary>
        CaptainDecides = 10,
        /// <summary>Continue from the podium / trophy lift / results (host-only: refused from a client).</summary>
        Continue = 11,
        /// <summary>Play Again (host-only: refused from a client, who only sees "waiting for the host").</summary>
        PlayAgain = 12,
        /// <summary>A client is leaving on purpose: the host applies the leave at once instead of waiting for the peer timeout.</summary>
        Quit = 13,
        /// <summary>payload = CupNet.PackLiveRow: the owner of a LOCAL round reports its live row (opponent, score, kick, playing) for the lobby.</summary>
        LiveRow = 14,
    }

    /// <summary>
    /// One human in the cup, as every screen and the HUD see them. Host-authoritative in
    /// multiplayer (the host's copy is the truth and rides CupState), local in Solo. Plain
    /// fields: only the director writes them (through its Apply* methods); screens read.
    /// </summary>
    public sealed class CupPlayer
    {
        /// <summary>Net slot (Solo: 0). The key everything else uses.</summary>
        public int Slot;
        public string Name;
        /// <summary>Index into CupBracket.Entrants, -1 before the draw (Co-op: every player shares the team's entrant).</summary>
        public int Entrant = -1;
        /// <summary>The nation picked (Head to Head / Solo) or voted for (Co-op); -1 = none yet.</summary>
        public int Nation = -1;
        /// <summary>The lobby / order-screen ready flag. Auto-true once Out.</summary>
        public bool Ready;
        /// <summary>Eliminated from the bracket.</summary>
        public bool Out;
        /// <summary>Head to Head: left mid-cup; an AI plays the nation from here ("Alice (AI)").</summary>
        public bool ReplacedByAi;
        /// <summary>Left the session (Co-op: dropped from the order; every style: no longer counted).</summary>
        public bool Left;
        /// <summary>The loading barrier ack for the current round (cleared by ClearLoaded).</summary>
        public bool Loaded;
        /// <summary>The slot this player is watching, -1 when not spectating.</summary>
        public int SpectatingSlot = -1;
        /// <summary>
        /// Somebody is watching THIS player right now. Host-derived (any active player's
        /// SpectatingSlot == Slot) and echoed in CupState, so the owner of a locally simulated
        /// round knows to stream its view (design 4: the owner streams only while spectated).
        /// </summary>
        public bool Spectated;

        // Live status for the lobby row while Playing ("Playing vs GHA - 2-1 - kick 4").
        public int LiveOpponentNation = -1;
        public int LiveScoreFor;
        public int LiveScoreAgainst;
        /// <summary>The 1-based number of the kick in progress.</summary>
        public int LiveKick;
        /// <summary>A round of theirs is in progress right now.</summary>
        public bool Playing;

        // The coin: this round's call, whether it was right, and the cup tallies (career stats).
        public CoinFace? CoinCall;
        public bool? CoinCallRight;
        public int CoinCallsMade;
        public int CoinCallsRight;

        public CupPlayer() { }

        public CupPlayer(int slot, string name)
        {
            Slot = slot;
            Name = name;
        }

        public bool HasPicked => Nation >= 0;
        public bool InCup => Entrant >= 0;
        /// <summary>Still a human in this cup: has not left and has not been handed to the AI.</summary>
        public bool Active => !Left && !ReplacedByAi;
        /// <summary>In the draw and not knocked out.</summary>
        public bool Alive => InCup && !Out;
        public bool IsSpectating => SpectatingSlot >= 0;
        /// <summary>"Alice", or "Alice (AI)" once replaced.</summary>
        public string DisplayName => ReplacedByAi ? CupText.AiName(Name) : Name;

        /// <summary>Clear the live-row fields (a round of theirs ended).</summary>
        public void ClearLive()
        {
            Playing = false;
            LiveOpponentNation = -1;
            LiveScoreFor = LiveScoreAgainst = 0;
            LiveKick = 0;
        }

        /// <summary>Set the live-row fields (a round of theirs is on).</summary>
        public void SetLive(int opponentNation, int scoreFor, int scoreAgainst, int kickNumber)
        {
            Playing = true;
            LiveOpponentNation = opponentNation;
            LiveScoreFor = scoreFor;
            LiveScoreAgainst = scoreAgainst;
            LiveKick = kickNumber;
        }

        /// <summary>Play Again: back to an unpicked, un-drawn player (Left / ReplacedByAi and the coin tallies are kept).</summary>
        public void ResetForNewCup()
        {
            Entrant = -1;
            Nation = -1;
            Ready = false;
            Out = false;
            Loaded = false;
            SpectatingSlot = -1;
            Spectated = false;
            ClearLive();
            CoinCall = null;
            CoinCallRight = null;
        }

        public CupPlayer Clone()
        {
            var c = new CupPlayer();
            c.CopyFrom(this);
            return c;
        }

        public void CopyFrom(CupPlayer o)
        {
            if (o == null) return;
            Slot = o.Slot;
            Name = o.Name;
            Entrant = o.Entrant;
            Nation = o.Nation;
            Ready = o.Ready;
            Out = o.Out;
            ReplacedByAi = o.ReplacedByAi;
            Left = o.Left;
            Loaded = o.Loaded;
            SpectatingSlot = o.SpectatingSlot;
            Spectated = o.Spectated;
            LiveOpponentNation = o.LiveOpponentNation;
            LiveScoreFor = o.LiveScoreFor;
            LiveScoreAgainst = o.LiveScoreAgainst;
            LiveKick = o.LiveKick;
            Playing = o.Playing;
            CoinCall = o.CoinCall;
            CoinCallRight = o.CoinCallRight;
            CoinCallsMade = o.CoinCallsMade;
            CoinCallsRight = o.CoinCallsRight;
        }

        public override string ToString()
        {
            return "slot " + Slot + " " + DisplayName + (Nation >= 0 ? " " + CupNationTable.CodeOf(Nation) : "") +
                   (Entrant >= 0 ? " e" + Entrant : "") + (Out ? " OUT" : "") + (Ready ? " ready" : "") +
                   (Playing ? " playing" : "") + (Left ? " LEFT" : "");
        }
    }

    /// <summary>
    /// The cup's phase machine: owns the bracket, the players, the current round, the per-round
    /// root, the tick counter, the borrowed statics and the seed (design 9.1). One instance lives
    /// under the match root for the whole cup - the arena, crowd and camera persist across rounds;
    /// only <see cref="RoundRoot"/> is rebuilt per round.
    ///
    /// This file is the SHELL: the read model, the intents, the authority-side Apply* methods
    /// that the intents (and the host's request handler) call, and the bracket / round plumbing
    /// every style needs. The per-style phase flows are the partials CupDirector.Solo.cs,
    /// CupDirector.HeadToHead.cs and CupDirector.Coop.cs (SoloTick / HeadToHeadTick / CoopTick,
    /// dispatched from Update by Style). Screens are separate components that register a draw
    /// callback in <see cref="OnGuiHook"/> and call the intents.
    ///
    /// Authority: in Solo (and whenever no session is active) everything is local. In multiplayer
    /// the host is the authority - an intent on the host applies at once, on a client it becomes a
    /// <see cref="RequestRaised"/> event for the net layer to send (marked "// MP:" below), and the
    /// host's echo arrives through the net layer calling the same Apply* methods / SetPhase and
    /// the read model. <see cref="StateChanged"/> fires after every change so the host can
    /// broadcast CupState.
    /// </summary>
    public partial class CupDirector : MonoBehaviour
    {
        /// <summary>The live director, or null outside a cup (like PauseMenu.Paused: a global the HUD may read).</summary>
        public static CupDirector Instance { get; private set; }

        /// <summary>The host's default seat (NetSession.Host seats itself at slot 1). A client's best guess for the Captain until CupState says otherwise.</summary>
        public const int HostSlotDefault = 1;

        // ---- identity ----------------------------------------------------------------------------
        public CupStyle Style { get; private set; }
        public CupFormat Format { get; private set; }
        /// <summary>The cup seed: every stream is new SeededRng(Seed).Fork(CupSalts.X).</summary>
        public uint Seed { get; private set; }
        /// <summary>The draw; null until BuildBracket (after the nation pick).</summary>
        public CupBracket Bracket { get; private set; }
        /// <summary>The stage in progress.</summary>
        public CupStage Stage { get; private set; }
        public CupPhase Phase { get; private set; }
        /// <summary>Seconds in the current phase (Time.deltaTime: a Solo pause freezes it, MP keeps running).</summary>
        public float PhaseTime { get; private set; }
        /// <summary>The round being played, null outside a round.</summary>
        public CupRound CurrentRound { get; private set; }
        /// <summary>The live round driver, null outside a round.</summary>
        public CupRoundDriver Driver { get; private set; }
        /// <summary>The local player's net slot (Solo: 0; -1 if the session gave none).</summary>
        public int LocalSlot { get; private set; } = -1;
        /// <summary>The Captain's slot (Co-op: the host; Solo: 0). Host-authoritative in MP.</summary>
        public int CaptainSlot { get; private set; }
        /// <summary>The local player's entrant index, -1 before the draw (Co-op: the team's entrant).</summary>
        public int LocalEntrant { get; private set; } = -1;
        public bool LocalIsCaptain => LocalSlot >= 0 && LocalSlot == CaptainSlot;
        /// <summary>Every human who was in the session at launch, by slot (leavers stay, flagged Left).</summary>
        public IReadOnlyList<CupPlayer> Players => _players;
        /// <summary>Co-op: the stage's shooting order, slot per order index, index 0 = the keeper. Empty until set.</summary>
        public int[] CoopOrder { get; private set; } = new int[0];
        /// <summary>Co-op: votes per nation index (length CupNations.Count; recounted on every pick).</summary>
        public int[] NationVotes { get; private set; } = new int[0];
        /// <summary>Co-op: the nation the team settled on (majority or CAPTAIN DECIDES), -1 until then.</summary>
        public int TeamNation { get; private set; } = -1;
        /// <summary>Co-op: how many times the lever was pulled this stage (varies the permutation stream).</summary>
        public int LeverPulls { get; private set; }
        /// <summary>Monotonic frame counter for the whole cup (starts at 1, never reset between rounds - the input reorder guard relies on it).</summary>
        public uint Tick { get; private set; }

        // ---- scene references (from GameBootstrap through Launch) ---------------------------------
        public Transform MatchRoot { get; private set; }
        /// <summary>The per-round root (bodies, referee, wall, coin, wall materials); null between rounds.</summary>
        public Transform RoundRoot { get; private set; }
        public GameInput Input { get; private set; }
        public Camera Cam { get; private set; }
        public GameCamera GameCam { get; private set; }
        public BallController Ball { get; private set; }
        /// <summary>The goal centre transform (Arena.Refs.goalCenter).</summary>
        public Transform Goal { get; private set; }
        public Material Torso { get; private set; }
        public Material Limb { get; private set; }
        public Material Glove { get; private set; }
        /// <summary>Tears the match down and shows the main menu (GameBootstrap.ReturnToMainMenu). Destroys this director.</summary>
        public Action OnMainMenu { get; set; }
        /// <summary>MP client: leave the session cleanly (GameBootstrap.LeaveNetworkedMatch). Falls back to OnMainMenu when null.</summary>
        public Action OnLeave { get; set; }
        /// <summary>
        /// Solo only: Back / Esc on CHOOSE YOUR NATION returns to the fork screen (design 6.1 -
        /// "Solo only: back to the fork"). GameBootstrap sets it to its ReturnToMatchSetup path
        /// (tear the match down, reopen the PENALTIES / FREE KICKS cards). Falls back to
        /// OnMainMenu when null. Destroys this director.
        /// </summary>
        public Action OnBackToSetup { get; set; }

        // ---- per-cup presentation services (created once at Launch, live under MatchRoot) ---------
        /// <summary>Every camera the cup uses (design 7.8 / 7.9); the round driver and the choreography ask it for views.</summary>
        public CupCameraRig Rig { get; private set; }
        /// <summary>The in-round HUD (design 6.5); bound to each round's driver by StartRound, unbound by EndRound.</summary>
        public CupHud Hud { get; private set; }
        /// <summary>The loading card (design 6.4): the flows Show it before StartRound and Hide it once the scene is built.</summary>
        public CupLoadingUI Loading { get; private set; }
        /// <summary>The coin toss ceremony in progress (design 7.1), null outside one. Started by BeginCoinToss.</summary>
        public CupCoinToss Toss { get; private set; }
        /// <summary>
        /// Bumps on every SetPhase, even to the same phase (Play Again lands on NationPick from
        /// NationPick on a client that never left it). The flow partials compare it to their own
        /// copy to run a phase's ENTRY actions exactly once per entry, from their tick rather than
        /// from inside SetPhase - so screens are created from Update, never inside a GUI pass.
        /// </summary>
        public int PhaseSerial { get; private set; }

        /// <summary>A networked cup (a session is active and the style is not Solo).</summary>
        public bool IsNetworked => Style != CupStyle.Solo && Multiplayer.IsActive;
        /// <summary>
        /// This machine decides (Solo, no session, or the host). A CLIENT whose session died under
        /// it (the host gone, CupDirector.Net's NetTick noticed) stays a non-authority for the
        /// frame or two until the pump's HostConnectionLost tears the match down: with the session
        /// null, IsNetworked reads false and this would otherwise flip to true, and one tick of a
        /// client flow acting as the host could build a draw, start a round or bank career stats
        /// for a cup that no longer exists.
        /// </summary>
        public bool IsAuthority => Multiplayer.IsHost || (!IsNetworked && !_netLostAsClient);

        // ---- events --------------------------------------------------------------------------------
        public event Action<CupPhase> PhaseChanged;
        /// <summary>Anything in the read model changed (players, bracket, order, votes, phase). The host broadcasts CupState on it.</summary>
        public event Action StateChanged;
        /// <summary>A client-side intent that must reach the host: (kind, int arg, optional payload). // MP: the net layer subscribes and sends CupRequest.</summary>
        public event Action<CupRequestKind, int, byte[]> RequestRaised;

        /// <summary>
        /// Draw callbacks, invoked in list order from this component's OnGUI. Screens register
        /// with <see cref="AddGuiHook"/> / <see cref="RemoveGuiHook"/>; changes made DURING a GUI
        /// pass are deferred to the next Update, so a control list never changes between IMGUI's
        /// Layout and Repaint passes (which would shift every control id after it).
        /// </summary>
        public List<Action> OnGuiHook { get; } = new List<Action>();

        readonly List<CupPlayer> _players = new List<CupPlayer>();
        readonly List<Action> _guiAdd = new List<Action>();
        readonly List<Action> _guiRemove = new List<Action>();
        bool _inGui;
        bool _wasPaused;

        // ==========================================================================================
        // Lifecycle
        // ==========================================================================================

        /// <summary>
        /// Create the director as a child of the match root and store everything a round will
        /// need. Applies the cup statics (regulation goal, format, shooting overrides - see
        /// <see cref="ApplyCupStatics(CupFormat, CupStage)"/>; call that static overload ABOVE
        /// Arena.Build too, exactly like the accuracy challenge, so the goal frame is built at
        /// regulation; this call then only rebuilds it if something still differs). Starts in
        /// <see cref="CupPhase.NationPick"/> with the players seeded from the session roster (MP)
        /// or the profile name (Solo).
        /// </summary>
        public static CupDirector Launch(Transform matchRoot, CupStyle style, CupFormat format, uint seed,
                                         GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                                         Transform goal, Material torso, Material limb, Material glove,
                                         Action onMainMenu)
        {
            var go = new GameObject("CupDirector");
            if (matchRoot != null) go.transform.SetParent(matchRoot, false);
            var d = go.AddComponent<CupDirector>();
            d.Init(matchRoot, style, format, seed, input, cam, gameCam, ball, goal, torso, limb, glove, onMainMenu);
            return d;
        }

        void Init(Transform matchRoot, CupStyle style, CupFormat format, uint seed,
                  GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                  Transform goal, Material torso, Material limb, Material glove, Action onMainMenu)
        {
            Instance = this;
            MatchRoot = matchRoot;
            Style = style;
            Format = format;
            Seed = seed;
            Input = input;
            Cam = cam;
            GameCam = gameCam;
            Ball = ball;
            Goal = goal;
            Torso = torso;
            Limb = limb;
            Glove = glove;
            OnMainMenu = onMainMenu;

            _players.Clear();
            var s = Multiplayer.Session;
            bool net = style != CupStyle.Solo && s != null && s.Active;
            if (net)
            {
                var roster = s.Roster;
                if (roster != null)
                {
                    for (int i = 0; i < roster.Length; i++)
                    {
                        var r = roster[i];
                        if (!r.human) continue;
                        if (PlayerAt(r.slot) != null) continue;
                        _players.Add(new CupPlayer(r.slot, string.IsNullOrEmpty(r.name) ? "Player " + r.slot : r.name));
                    }
                }
                LocalSlot = s.LocalSlot >= 0 && s.LocalSlot < NetSession.MaxSlots ? s.LocalSlot : -1;
                if (LocalSlot >= 0 && PlayerAt(LocalSlot) == null)
                    _players.Add(new CupPlayer(LocalSlot, PlayerProfile.PlayerName));   // a roster that has not caught up yet
                // The Captain is the host. On the host that is our own seat; a client assumes the
                // default host seat until the host's CupState says otherwise (SetCaptainSlot).
                CaptainSlot = s.IsHost ? LocalSlot : HostSlotDefault;
            }
            else
            {
                if (style != CupStyle.Solo)
                    CupLog.Warn("CupDirector: " + CupText.StyleName(style) + " launched with no session - running it locally");
                _players.Add(new CupPlayer(0, PlayerProfile.PlayerName));
                LocalSlot = 0;
                CaptainSlot = 0;
            }
            _players.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            NationVotes = new int[CupNations.Count];
            RecountNationVotes();
            Stage = CupStage.RoundOf32;
            Tick = 1u;
            Bracket = null;
            LocalEntrant = -1;
            TeamNation = -1;
            CoopOrder = new int[0];
            LeverPulls = 0;

            ApplyCupStatics();

            // The camera rig, the HUD and the loading card live for the whole cup (one each,
            // under the match root): rounds bind and release them, the screens between rounds
            // lean on the rig for a backdrop. Created here, once, so no flow can forget them.
            Rig = CupCameraRig.Create(matchRoot, cam, gameCam, input);
            Hud = CupHud.Create(matchRoot, this, input);
            Loading = CupLoadingUI.Create(matchRoot, this);

            Phase = CupPhase.NationPick;
            PhaseTime = 0f;
            PhaseSerial = 1;   // a flow's entry latch starts at -1, so the launch phase is entered too
            _wasPaused = PauseMenu.Paused;
            // The wire (CupDirector.Net.cs): the host broadcasts CupState on every change and
            // answers requests; a client applies CupState and raises requests. Bound last, once
            // the whole read model above exists, because a client may already have a CupState
            // waiting in the session and applies it right here.
            NetBind();
            CupLog.Info("Launch " + CupText.Label(style, format) + " seed " + seed + ", " + _players.Count + " player(s), local slot " + LocalSlot + ", captain " + CaptainSlot);
        }

        void Update()
        {
            Tick++;
            PhaseTime += Time.deltaTime;
            FlushGuiHookChanges();
            ReassertCursorAfterUnpause();
            // The shared multiplayer plumbing runs BEFORE the style tick: a client's phase edge
            // arrives here (from a deferred CupState) and the style's entry latch then sees it
            // in the same frame; the host's coalesced broadcast goes out after the style ticked.
            NetTick();
            switch (Style)
            {
                case CupStyle.Solo: SoloTick(); break;
                case CupStyle.HeadToHead: HeadToHeadTick(); break;
                case CupStyle.Coop: CoopTick(); break;
            }
        }

        /// <summary>
        /// PauseMenu.Resume() re-captures the cursor unconditionally (right mid-round, wrong on
        /// every menu screen and during the coin call, whose HEADS / TAILS buttons need a
        /// pointer). The cup's own screens re-assert a free cursor on the unpause edge already;
        /// this covers the phases that have no screen component of their own (the coin toss, the
        /// loading card) with the same edge, so the rule holds for every phase in one place.
        /// </summary>
        void ReassertCursorAfterUnpause()
        {
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused && CursorShouldBeFree) GameInput.CaptureCursor(false);
            _wasPaused = paused;
        }

        /// <summary>
        /// Does the current phase want a FREE cursor? Every menu phase does. In a round the driver
        /// owns it (captured while the local player has a body and the round runs; freed at Over
        /// and for a bodiless spectator). The podium and the trophy lift own their own cursor.
        /// </summary>
        public bool CursorShouldBeFree
        {
            get
            {
                switch (Phase)
                {
                    case CupPhase.Round:
                        return Driver == null || Driver.Phase == RoundPhase.Over || Driver.Setup == null || !Driver.Setup.LocalHasBody;
                    case CupPhase.Podium:
                    case CupPhase.TrophyLift:
                        return false;
                    default:
                        return true;
                }
            }
        }

        void OnGUI()
        {
            if (OnGuiHook.Count == 0) return;
            _inGui = true;
            try
            {
                for (int i = 0; i < OnGuiHook.Count; i++) OnGuiHook[i]?.Invoke();
            }
            finally
            {
                _inGui = false;
            }
        }

        void OnDestroy()
        {
            NetUnbind();   // first: nothing below may broadcast or apply a state on a dying director
            if (Toss != null) { Toss.Cancel(); Toss = null; }
            EndPodium();
            EndTrophyLift();
            if (Driver != null && Driver.Running) Driver.Abort();
            UnbindRoundStats();
            if (Hud != null) Hud.Unbind();
            Driver = null;
            CurrentRound = null;
            // The results ledger is a static that holds this director: let go of it, or the next
            // cup's first screen finds a dead director attached and a stale table behind it.
            // The KNOCKED OUT card's career-best latch is per cup too (re-latched at every draw).
            CupStatsLedger.Detach();
            CupKnockedOutUI.BestStageBefore = null;
            // The borrowed statics go back ONLY when this director is the live one. A director
            // superseded in the same frame (Back to the fork and straight into a new cup before
            // the old match root is destroyed at end of frame) must leave them alone: the new
            // cup is already running on them, and its own OnDestroy restores the shared
            // snapshot - ApplyCupStatics never re-snapshots while one is valid, so the values
            // put back are still the ones from before the FIRST cup.
            if (Instance == this)
            {
                RestoreCupStatics();
                Instance = null;
            }
        }

        /// <summary>Register a screen's draw callback (appended = drawn last = on top). Safe to call from inside a GUI pass (deferred).</summary>
        public void AddGuiHook(Action draw)
        {
            if (draw == null) return;
            if (_inGui) { _guiAdd.Add(draw); return; }
            if (!OnGuiHook.Contains(draw)) OnGuiHook.Add(draw);
        }

        public void RemoveGuiHook(Action draw)
        {
            if (draw == null) return;
            if (_inGui) { _guiRemove.Add(draw); return; }
            OnGuiHook.Remove(draw);
        }

        void FlushGuiHookChanges()
        {
            if (_guiRemove.Count > 0)
            {
                for (int i = 0; i < _guiRemove.Count; i++) OnGuiHook.Remove(_guiRemove[i]);
                _guiRemove.Clear();
            }
            if (_guiAdd.Count > 0)
            {
                for (int i = 0; i < _guiAdd.Count; i++) if (!OnGuiHook.Contains(_guiAdd[i])) OnGuiHook.Add(_guiAdd[i]);
                _guiAdd.Clear();
            }
        }

        /// <summary>
        /// Move to a phase: PhaseTime resets (or takes the host's value on a client), PhaseChanged
        /// then StateChanged fire. Idempotent on the same phase only in that it still resets the
        /// timer and fires (a flow re-entering NationPick on Play Again wants exactly that).
        /// </summary>
        public void SetPhase(CupPhase phase, float phaseTime = 0f)
        {
            Phase = phase;
            PhaseTime = phaseTime < 0f ? 0f : phaseTime;
            PhaseSerial++;
            PhaseChanged?.Invoke(phase);
            Notify();
        }

        /// <summary>Fire StateChanged (every Apply* does; call it after a direct edit of the read model).</summary>
        public void Notify()
        {
            StateChanged?.Invoke();
        }

        /// <summary>A fresh cup seed (Solo fork screen, Play Again). Not deterministic - the host rolls it once and ships it.</summary>
        public static uint RollSeed()
        {
            unchecked
            {
                uint a = (uint)Environment.TickCount * 2654435761u;
                uint b = (uint)DateTime.UtcNow.Ticks;
                uint c = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                uint s = a ^ (b << 7) ^ (b >> 3) ^ c;
                return s == 0u ? 0x9E3779B9u : s;
            }
        }

        // ==========================================================================================
        // Borrowed statics (design 9.6) - snapshot once, restore in OnDestroy
        // ==========================================================================================

        struct StaticsSnapshot
        {
            public float goalW, goalH, ballSpeed, moveSpeed, wallDist, keeperAbility, humanKeeperMul;
            public int wallCount;
            public bool penaltyMode, accNoKeeper, placed, randomSpots, maxShooting, uniformBody;
            public Vector3 attackGoal, ballSpot, wallCenter;
        }

        static StaticsSnapshot s_saved;
        static bool s_savedValid;

        /// <summary>
        /// Write every static the cup borrows, saving the previous values the FIRST time (a second
        /// call never overwrites the snapshot with cup values). Regulation goal (rebuilt in place
        /// when an arena is live and the size changed), BallSpeedMul 1, StrikerMoveSpeed base, the
        /// wall from CupTuning, PenaltyMode per format, no accuracy flags, no placed spot, the
        /// attack goal at the real goal, the stage's AI keeper ability, no human keeper handicap,
        /// and the two standardised-shooting overrides. Call it ABOVE Arena.Build as well
        /// (GameBootstrap), so the goal frame is built at regulation in the first place.
        /// </summary>
        public static void ApplyCupStatics(CupFormat format, CupStage stage)
        {
            if (!s_savedValid)
            {
                s_saved.goalW = SimConfig.GoalWidth;
                s_saved.goalH = SimConfig.GoalHeight;
                s_saved.ballSpeed = SimConfig.BallSpeedMul;
                s_saved.moveSpeed = SimConfig.StrikerMoveSpeed;
                s_saved.wallDist = SimConfig.WallDistance;
                s_saved.wallCount = SimConfig.WallCount;
                s_saved.keeperAbility = SimConfig.KeeperAbility;
                s_saved.humanKeeperMul = SimConfig.HumanKeeperSpeedMul;
                s_saved.penaltyMode = SimConfig.PenaltyMode;
                s_saved.accNoKeeper = SimConfig.AccuracyNoKeeper;
                s_saved.placed = SimConfig.SetPiecePlaced;
                s_saved.randomSpots = SimConfig.SetPieceRandomSpots;
                s_saved.ballSpot = SimConfig.SetPieceBallSpot;
                s_saved.wallCenter = SimConfig.SetPieceWallCenter;
                s_saved.attackGoal = SimConfig.AttackGoalCenter;
                s_saved.maxShooting = SkillTree.MaxShootingOverride;
                s_saved.uniformBody = PlayerProfile.UniformBodyOverride;
                s_savedValid = true;
            }

            bool resized = !Mathf.Approximately(SimConfig.GoalWidth, SimConfig.GoalWidthBase)
                        || !Mathf.Approximately(SimConfig.GoalHeight, SimConfig.GoalHeightBase);
            SimConfig.GoalWidth = SimConfig.GoalWidthBase;
            SimConfig.GoalHeight = SimConfig.GoalHeightBase;
            SimConfig.BallSpeedMul = 1f;
            SimConfig.StrikerMoveSpeed = SimConfig.StrikerMoveSpeedBase;
            SimConfig.WallCount = CupTuning.WallCount;
            SimConfig.WallDistance = CupTuning.WallDistance;
            SimConfig.PenaltyMode = format == CupFormat.Penalties;
            SimConfig.AccuracyNoKeeper = false;
            SimConfig.SetPiecePlaced = false;
            SimConfig.SetPieceRandomSpots = false;
            SimConfig.AttackGoalCenter = SimConfig.GoalCenter;
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(stage);
            SimConfig.HumanKeeperSpeedMul = SimConfig.HumanKeeperSpeedBase;   // never handicapped in the cup (design 2.3)
            SkillTree.MaxShootingOverride = true;
            PlayerProfile.UniformBodyOverride = true;
            if (resized) Arena.RebuildGoal();   // no-op when no single-goal arena is live
        }

        /// <summary>The instance form: this cup's format and the current stage's ramp.</summary>
        public void ApplyCupStatics() => ApplyCupStatics(Format, Stage);

        /// <summary>
        /// Put every borrowed static back as it was before the first ApplyCupStatics. The goal is
        /// NOT rebuilt here: this runs during the match teardown, and the next mode's setup writes
        /// the goal size before its arena is built anyway.
        /// </summary>
        public static void RestoreCupStatics()
        {
            if (!s_savedValid) return;
            SimConfig.GoalWidth = s_saved.goalW;
            SimConfig.GoalHeight = s_saved.goalH;
            SimConfig.BallSpeedMul = s_saved.ballSpeed;
            SimConfig.StrikerMoveSpeed = s_saved.moveSpeed;
            SimConfig.WallDistance = s_saved.wallDist;
            SimConfig.WallCount = s_saved.wallCount;
            SimConfig.KeeperAbility = s_saved.keeperAbility;
            SimConfig.HumanKeeperSpeedMul = s_saved.humanKeeperMul;
            SimConfig.PenaltyMode = s_saved.penaltyMode;
            SimConfig.AccuracyNoKeeper = s_saved.accNoKeeper;
            SimConfig.SetPiecePlaced = s_saved.placed;
            SimConfig.SetPieceRandomSpots = s_saved.randomSpots;
            SimConfig.SetPieceBallSpot = s_saved.ballSpot;
            SimConfig.SetPieceWallCenter = s_saved.wallCenter;
            SimConfig.AttackGoalCenter = s_saved.attackGoal;
            SkillTree.MaxShootingOverride = s_saved.maxShooting;
            PlayerProfile.UniformBodyOverride = s_saved.uniformBody;
            s_savedValid = false;
        }

        // ==========================================================================================
        // Players
        // ==========================================================================================

        public CupPlayer PlayerAt(int slot)
        {
            for (int i = 0; i < _players.Count; i++) if (_players[i].Slot == slot) return _players[i];
            return null;
        }

        public CupPlayer LocalPlayer => PlayerAt(LocalSlot);
        public CupPlayer Captain => PlayerAt(CaptainSlot);

        /// <summary>Players still human in this cup (not Left, not ReplacedByAi).</summary>
        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _players.Count; i++) if (_players[i].Active) n++;
                return n;
            }
        }

        /// <summary>Slots of every active player, ascending (Co-op: the team).</summary>
        public List<int> ActiveSlots()
        {
            var list = new List<int>();
            for (int i = 0; i < _players.Count; i++) if (_players[i].Active) list.Add(_players[i].Slot);
            return list;
        }

        /// <summary>Slots of every active player still alive in the bracket (or every active player before the draw).</summary>
        public List<int> AliveHumanSlots()
        {
            var list = new List<int>();
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.Active) continue;
                if (Bracket != null && !p.Alive) continue;
                list.Add(p.Slot);
            }
            return list;
        }

        /// <summary>Every active player has picked / voted (false with nobody active).</summary>
        public bool AllPicked
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (!p.Active) continue;
                    if (!p.HasPicked) return false;
                    n++;
                }
                return n > 0;
            }
        }

        /// <summary>Every active player is ready (the eliminated are auto-ready).</summary>
        public bool AllReady
        {
            get
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p.Active && !p.Ready && !p.Out) return false;
                }
                return true;
            }
        }

        /// <summary>Every active player sent the loading ack for this round.</summary>
        public bool AllLoaded
        {
            get
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p.Active && !p.Loaded) return false;
                }
                return true;
            }
        }

        /// <summary>Head to Head: is a nation held by another active player?</summary>
        public bool NationTaken(int nationIndex, out int bySlot)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Active && p.Nation == nationIndex) { bySlot = p.Slot; return true; }
            }
            bySlot = -1;
            return false;
        }

        /// <summary>Set the Captain (a client applying the host's CupState).</summary>
        public void SetCaptainSlot(int slot)
        {
            if (CaptainSlot == slot) return;
            CaptainSlot = slot;
            Notify();
        }

        // ---- Co-op votes -----------------------------------------------------------------------
        /// <summary>Recount NationVotes from the active players' picks (every style keeps it current; only Co-op shows it).</summary>
        public void RecountNationVotes()
        {
            if (NationVotes == null || NationVotes.Length != CupNations.Count) NationVotes = new int[CupNations.Count];
            else Array.Clear(NationVotes, 0, NationVotes.Length);
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Active && p.Nation >= 0 && p.Nation < NationVotes.Length) NationVotes[p.Nation]++;
            }
        }

        /// <summary>The nation with the most votes (lowest index on a draw), -1 with no votes.</summary>
        public int LeadingNation(out int votes)
        {
            int best = -1;
            votes = 0;
            for (int i = 0; i < NationVotes.Length; i++)
            {
                if (NationVotes[i] > votes) { votes = NationVotes[i]; best = i; }
            }
            return best;
        }

        /// <summary>The nation holding more than half the active players' votes, or -1.</summary>
        public int MajorityNation(out int votes)
        {
            int best = LeadingNation(out votes);
            return best >= 0 && votes * 2 > ActiveCount ? best : -1;
        }

        /// <summary>Co-op: a majority exists (the gate that lets the pick screen proceed once everyone has picked).</summary>
        public bool MajorityReached
        {
            get
            {
                int v;
                return MajorityNation(out v) >= 0;
            }
        }

        // ==========================================================================================
        // Intents (what screens call). Local in Solo; a request to the host in MP.
        // ==========================================================================================

        /// <summary>Pick (Solo / Head to Head, picking = ready) or vote for (Co-op) a nation.</summary>
        public void PickNation(int nationIndex)
        {
            if (Phase != CupPhase.NationPick) return;
            if (IsAuthority) { ApplyPick(LocalSlot, nationIndex); return; }
            // MP: client -> host CupRequest.PickNation(nation); the host runs ApplyPick(slot, nation)
            //     (first request wins a race; a refused pick simply never echoes, so the card snaps back)
            //     and the echo arrives in CupState.
            RaiseRequest(CupRequestKind.PickNation, nationIndex, null);
        }

        /// <summary>The lobby / order-screen Ready toggle.</summary>
        public void SetReady(bool ready)
        {
            if (IsAuthority) { ApplyReady(LocalSlot, ready); return; }
            // MP: client -> host CupRequest.Ready(1/0); the host runs ApplyReady(slot, ready).
            RaiseRequest(CupRequestKind.Ready, ready ? 1 : 0, null);
        }

        /// <summary>Watch another player's round from the lobby (Head to Head).</summary>
        public void Spectate(int slot)
        {
            if (slot == LocalSlot) return;
            if (IsAuthority) { ApplySpectate(LocalSlot, slot); return; }
            // MP: client -> host CupRequest.Spectate(slot); the host runs ApplySpectate and starts relaying that slot's CupStream.
            RaiseRequest(CupRequestKind.Spectate, slot, null);
        }

        public void StopSpectating()
        {
            if (IsAuthority) { ApplySpectate(LocalSlot, -1); return; }
            // MP: client -> host CupRequest.Unspectate; the host runs ApplySpectate(slot, -1) and stops the relay.
            RaiseRequest(CupRequestKind.Unspectate, 0, null);
        }

        /// <summary>The loading barrier ack: this machine has built the round (MP); a no-op signal in Solo.</summary>
        public void NotifyLoaded()
        {
            if (IsAuthority) { ApplyLoaded(LocalSlot); return; }
            // MP: client -> host CupRequest.Loaded; the host runs ApplyLoaded(slot); AllLoaded (or the 10 s timeout) starts the toss.
            RaiseRequest(CupRequestKind.Loaded, 0, null);
        }

        /// <summary>
        /// Quit to Menu. Solo: ends the cup. MP client: leaves the session (the host sees the peer
        /// drop and hands the nation to AI / drops the order slot). MP host: quitting IS ending the
        /// cup for everyone. Invokes a callback that destroys this director - touch nothing after.
        /// </summary>
        public void QuitToMenu()
        {
            if (!IsNetworked)
            {
                SetPhase(CupPhase.Ended);
                OnMainMenu?.Invoke();
                return;
            }
            if (Multiplayer.IsHost) { EndMatch(); return; }
            // Tell the host on purpose (CupRequest.Quit) so it applies the leave NOW - the nation
            // to AI, the order collapsed - rather than after the transport's 5 s peer timeout;
            // the roster drop that follows is then a no-op for an already-Left player.
            NetSendQuit();
            SetPhase(CupPhase.Ended);
            // MP: GameBootstrap.LeaveNetworkedMatch drops this peer; the host's OnPeerLeft calls ApplyLeave(slot).
            (OnLeave ?? OnMainMenu)?.Invoke();
        }

        /// <summary>Play Again (host / Solo): the same lobby, a new seed, back to CHOOSE YOUR NATION.</summary>
        public void PlayAgain()
        {
            if (!IsAuthority)
            {
                // MP: clients only see "waiting for the host"; the host's CupState brings the new seed and NationPick.
                return;
            }
            ResetForNewCup(RollSeed());
            // MP: host broadcasts CupState (phase NationPick, the new seed); clients call ResetForNewCup(seed) from it.
        }

        /// <summary>End Match (host): the cup is over for everyone; Solo: same as Quit to Menu.</summary>
        public void EndMatch()
        {
            if (IsNetworked && !Multiplayer.IsHost)
            {
                // A client's "End Match" button is its own leave (design 6.6: "client: leaves").
                QuitToMenu();
                return;
            }
            // MP: host broadcasts CupState(Ended) before dissolving so clients show the main menu, not "connection lost".
            SetPhase(CupPhase.Ended);
            OnMainMenu?.Invoke();
        }

        /// <summary>HEADS or TAILS during the coin toss (everyone present calls; the official caller's call decides).</summary>
        public void CallCoin(CoinFace face)
        {
            // The CoinToss phase, or a ceremony open on THIS peer: Head to Head's parallel rounds
            // each run their own toss under the shared Round phase (CupDirector.HeadToHead).
            if (Phase != CupPhase.CoinToss && Toss == null) return;
            if (IsAuthority) { ApplyCoinCall(LocalSlot, face); return; }
            // Local echo so the button lights at once; the host's tally is the truth.
            var me = LocalPlayer;
            if (me != null) { me.CoinCall = face; Notify(); }
            // MP: client -> host CupRequest.CallCoin(face); the host runs ApplyCoinCall(slot, face).
            RaiseRequest(CupRequestKind.CallCoin, (int)face, null);
        }

        /// <summary>The scorer's (or winning keeper's) click to skip the open celebration window.</summary>
        public void SkipCelebration()
        {
            if (Driver == null || !Driver.CanLocalSkip) return;
            if (Driver.Authority == RoundAuthority.Client)
            {
                // MP: client -> host CupRequest.SkipCelebration; the host's driver closes the window and the state echoes.
                RaiseRequest(CupRequestKind.SkipCelebration, 0, null);
                return;
            }
            Driver.SkipCelebration();
        }

        /// <summary>Co-op, the Captain, everyone picked without a majority: the Captain's own pick becomes the team's nation.</summary>
        public void CaptainDecides()
        {
            if (Style != CupStyle.Coop || !LocalIsCaptain || Phase != CupPhase.NationPick) return;
            var c = Captain;
            if (c == null || !c.HasPicked || !AllPicked) return;
            DecideTeamNation(c.Nation);
            // MP: the Captain is the host, so this rides CupState (TeamNation); no request.
        }

        /// <summary>Co-op, the Captain: the shooting order (slot per order index, index 0 = keeper, one entry per active player).</summary>
        public void SetOrder(int[] slotsByOrderIndex)
        {
            if (Style != CupStyle.Coop || !LocalIsCaptain) return;
            ApplyOrder(slotsByOrderIndex);
            // MP: the Captain is the host, so the order rides CupState; no request.
        }

        /// <summary>Co-op, the Captain: the slot-machine lever - a seeded permutation of the team.</summary>
        public void PullLever()
        {
            if (Style != CupStyle.Coop || !LocalIsCaptain || Phase != CupPhase.OrderPick) return;
            RollOrder();
            // MP: the permutation rides CupState BEFORE the reels animate, so every client lands on the same faces.
        }

        /// <summary>Continue from the podium / trophy lift to the results, and from the results to Ended.</summary>
        public void ContinueFromResults()
        {
            switch (Phase)
            {
                case CupPhase.Podium:
                case CupPhase.TrophyLift:
                    SetPhase(CupPhase.Results);
                    break;
                case CupPhase.Results:
                    SetPhase(CupPhase.Ended);
                    break;
            }
            // MP: host-only; the phase rides CupState.
        }

        /// <summary>
        /// Solo KNOCKED OUT card: simulate the next incomplete stage (one press = one stage; the
        /// last press crowns the AI champion). Returns the stage simulated, null when complete.
        /// </summary>
        public CupStage? SimulateRest()
        {
            if (!IsAuthority || Bracket == null || Bracket.IsComplete) return null;
            // The stage the player went out in is COMPLETE (their round + the simulated AI rounds)
            // but was never fed forward - AdvanceStage only runs for a stage the player survives.
            // CupSim.SimulateNextStage works from Bracket.CurrentStage, the lowest stage with an
            // undecided round, which is then the NEXT stage - whose rounds have no entrants yet,
            // so it simulates nothing, finds the stage incomplete and returns null: a press that
            // did nothing, forever. Feeding the finished stage(s) forward first makes the first
            // press land on the next stage, as the card promises ("each press fills one stage").
            FeedForwardCompletedStages();
            var stage = CupSim.SimulateNextStage(Bracket, new SeededRng(Seed));
            // Feed the stage just simulated forward too, so the SAME press also reveals the next
            // stage's pairings (the finalists after the semi-finals) instead of needing a second one.
            FeedForwardCompletedStages();
            Stage = Bracket.CurrentStage;
            RefreshPlayersFromBracket();
            Notify();
            return stage;
        }

        /// <summary>
        /// Bracket.Advance every complete stage whose successor still has a round without
        /// entrants. Advance is safe to repeat (it only resets a fed round whose entrants changed),
        /// so this is idempotent; it stops at the first incomplete stage.
        /// </summary>
        void FeedForwardCompletedStages()
        {
            if (Bracket == null) return;
            for (int s = 0; s < CupStages.Count - 1; s++)
            {
                var stage = (CupStage)s;
                if (!Bracket.StageComplete(stage)) return;
                var next = Bracket.RoundsOf(CupStages.Next(stage));
                bool fed = true;
                for (int i = 0; i < next.Count; i++) if (!next[i].Ready) { fed = false; break; }
                if (!fed) Bracket.Advance(stage);
            }
        }

        void RaiseRequest(CupRequestKind kind, int arg, byte[] payload)
        {
            if (RequestRaised == null)
            {
                CupLog.Warn("CupDirector: request " + kind + "(" + arg + ") with no net layer bound - dropped");
                return;
            }
            RequestRaised(kind, arg, payload);
        }

        // ==========================================================================================
        // Authority-side appliers (called by the intents on the authority, and by the host's
        // request handler for a remote slot). Each returns whether anything changed.
        // ==========================================================================================

        public bool ApplyPick(int slot, int nationIndex)
        {
            if (Phase != CupPhase.NationPick) return false;
            var p = PlayerAt(slot);
            if (p == null || !p.Active) return false;
            if (!CupNations.IsValid(nationIndex) || !CupNations.HasDesign(nationIndex)) return false;
            if (Style == CupStyle.HeadToHead)
            {
                int by;
                if (NationTaken(nationIndex, out by) && by != slot) return false;   // first request wins
            }
            p.Nation = nationIndex;
            if (Style != CupStyle.Coop) p.Ready = true;   // picking is confirming (Solo) / the ready (Head to Head)
            RecountNationVotes();
            Notify();
            return true;
        }

        public bool ApplyReady(int slot, bool ready)
        {
            var p = PlayerAt(slot);
            if (p == null || !p.Active) return false;
            bool v = ready || p.Out;   // the eliminated are always ready
            if (p.Ready == v) return false;
            p.Ready = v;
            Notify();
            return true;
        }

        /// <summary>target -1 stops spectating; otherwise another active player who is Playing.</summary>
        public bool ApplySpectate(int slot, int target)
        {
            var p = PlayerAt(slot);
            if (p == null) return false;
            if (target >= 0)
            {
                var t = PlayerAt(target);
                if (t == null || t == p || !t.Active || !t.Playing) return false;
            }
            if (p.SpectatingSlot == target) return false;
            p.SpectatingSlot = target < 0 ? -1 : target;
            Notify();
            return true;
        }

        public bool ApplyLoaded(int slot)
        {
            var p = PlayerAt(slot);
            if (p == null || p.Loaded) return false;
            p.Loaded = true;
            Notify();
            return true;
        }

        /// <summary>Clear every loading ack (before each round's barrier).</summary>
        public void ClearLoaded()
        {
            for (int i = 0; i < _players.Count; i++) _players[i].Loaded = false;
        }

        /// <summary>Clear every ready flag (a new stage's gate); the eliminated stay ready.</summary>
        public void ClearReady()
        {
            for (int i = 0; i < _players.Count; i++) _players[i].Ready = _players[i].Out;
            Notify();
        }

        public bool ApplyCoinCall(int slot, CoinFace face)
        {
            var p = PlayerAt(slot);
            if (p == null || !p.Active) return false;
            if (p.CoinCall == face) return false;
            p.CoinCall = face;
            // A changed call is a call for a NEW flip (or a changed mind before this one): the
            // verdict of the last judged call must not ride along, or the judge (ResolveCoinCalls,
            // Head to Head's H2HResolveParallelCalls) skips it as already counted and this toss's
            // call is never tallied - a Head to Head late wave after a host round's toss hit that.
            p.CoinCallRight = null;
            Notify();
            return true;
        }

        /// <summary>The coin settled: mark every call right or wrong and bank the tallies (career stats read them).</summary>
        public void ResolveCoinCalls(CoinFace result)
        {
            // A Head to Head PARALLEL round's toss is the owner's alone: every other player's call
            // belongs to their own round (the host judges those against the right seeded face -
            // CupDirector.HeadToHead), so only the local call is judged here. A host round's toss
            // (and every Solo / Co-op one) judges everyone present. A call already carrying a
            // verdict is never counted twice.
            bool localOnly = Style == CupStyle.HeadToHead && IsNetworked && Driver != null && Driver.Authority == RoundAuthority.Local;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (localOnly && p.Slot != LocalSlot) continue;
                if (!p.CoinCall.HasValue) { p.CoinCallRight = null; continue; }
                if (p.CoinCallRight.HasValue) continue;
                bool right = p.CoinCall.Value == result;
                p.CoinCallRight = right;
                p.CoinCallsMade++;
                if (right) p.CoinCallsRight++;
            }
            Notify();
        }

        /// <summary>Forget this round's calls (before the next toss).</summary>
        public void ClearCoinCalls()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                _players[i].CoinCall = null;
                _players[i].CoinCallRight = null;
            }
        }

        /// <summary>Co-op: settle the team's nation (majority, or CAPTAIN DECIDES).</summary>
        public void DecideTeamNation(int nationIndex)
        {
            if (!CupNations.IsValid(nationIndex)) return;
            TeamNation = nationIndex;
            Notify();
        }

        /// <summary>
        /// Co-op: install a shooting order. Valid = one entry per active player, every filled
        /// entry an active player's slot, none twice; index 0 is the keeper. An entry of -1 is an
        /// EMPTY slot: the order screen fills the order one drag at a time and every peer watches
        /// it fill (design 6.8), so a partial order is a legal state of the model - only the flow's
        /// gate (<see cref="CoopOrderComplete"/>) insists on every slot filled. A change un-readies
        /// everyone (a Ready is a Ready for THAT order); an identical order changes nothing.
        /// </summary>
        public bool ApplyOrder(int[] order)
        {
            if (Style != CupStyle.Coop) return false;
            var team = ActiveSlots();
            if (order == null || order.Length != team.Count) return false;
            bool same = CoopOrder.Length == order.Length;
            for (int i = 0; i < order.Length; i++)
            {
                int v = order[i];
                if (v >= 0)
                {
                    if (!team.Contains(v)) return false;
                    for (int j = 0; j < i; j++) if (order[j] == v) return false;
                }
                if (same && CoopOrder[i] != v) same = false;
            }
            if (same) return false;
            CoopOrder = (int[])order.Clone();
            ClearReady();   // Notify()s
            return true;
        }

        /// <summary>Co-op: the lever - shuffle the team with the stage's Order stream (varied per pull) and install it.</summary>
        public bool RollOrder()
        {
            if (Style != CupStyle.Coop) return false;
            var team = ActiveSlots();
            if (team.Count == 0) return false;
            // Stays inside the Order family block (0x6000 + stage + 16 * pulls) so it never collides with another salt family.
            var rng = new SeededRng(Seed).Fork(CupSalts.Order(Stage) + 16u * (uint)LeverPulls);
            rng.Shuffle(team);
            LeverPulls++;
            return ApplyOrder(team.ToArray());
        }

        /// <summary>Co-op: forget the order (a new stage's order screen).</summary>
        public void ClearOrder()
        {
            CoopOrder = new int[0];
            LeverPulls = 0;
            Notify();
        }

        /// <summary>
        /// A player left the session. Head to Head: their nation is played by the AI from here
        /// (the bracket marks it, later rounds are simulated). Co-op: dropped from the order, the
        /// slots collapse (a leaving keeper hands the gloves to the lowest-ordered shooter until the
        /// next order screen). Anyone spectating them stops.
        /// </summary>
        public bool ApplyLeave(int slot)
        {
            var p = PlayerAt(slot);
            if (p == null || p.Left) return false;
            p.Left = true;
            p.Ready = true;
            p.Loaded = true;
            p.SpectatingSlot = -1;
            p.ClearLive();
            if (Style == CupStyle.HeadToHead)
            {
                p.ReplacedByAi = true;
                if (Bracket != null && p.Entrant >= 0 && !p.Out) Bracket.MarkReplacedByAi(p.Entrant);
            }
            else if (Style == CupStyle.Coop && CoopOrder.Length > 0)
            {
                // Dropped from the order, the slots collapse (design 5 / 10): the team is one
                // smaller, so the order is one entry shorter. A leaver who held a slot simply
                // vanishes from it; a leaver on the BENCH (an order still being filled) means a
                // shooter slot has to go instead - an empty one when there is one, else the last
                // shooter slot (its holder returns to the bench). The keeper slot is shed only
                // when it is the last slot standing, so the order's shape never lies about who
                // keeps. The flow partial hands a leaving keeper's gloves on and prompts the
                // Captain (CupDirector.Coop.cs).
                var keep = new List<int>(CoopOrder.Length);
                for (int i = 0; i < CoopOrder.Length; i++) if (CoopOrder[i] != slot) keep.Add(CoopOrder[i]);
                int active = ActiveCount;
                while (keep.Count > active && keep.Count > 0)
                {
                    int drop = -1;
                    for (int i = keep.Count - 1; i >= 1; i--) if (keep[i] < 0) { drop = i; break; }
                    if (drop < 0) drop = keep.Count - 1;
                    keep.RemoveAt(drop);
                }
                CoopOrder = keep.ToArray();
            }
            for (int i = 0; i < _players.Count; i++) if (_players[i].SpectatingSlot == slot) _players[i].SpectatingSlot = -1;
            RecountNationVotes();
            Notify();
            return true;
        }

        /// <summary>
        /// A finished round reported by its owner (Head to Head parallel phase). Validated under
        /// the rules (alternation, no kick after the decision, decided) and folded into the
        /// bracket with the REPLAYED scores. // MP: the host's handler for CupRequest.RoundResult.
        /// </summary>
        public bool ApplyRoundResult(CupRound reported)
        {
            if (Bracket == null || reported == null || !CupStages.IsValid(reported.Stage)) return false;
            if (reported.Index < 0 || reported.Index >= CupStages.RoundsIn(reported.Stage)) return false;
            var round = Bracket.Round(reported.Stage, reported.Index);
            if (round == null || !round.Ready) return false;
            if (round.Done)
            {
                // The first result wins: a round the host has already settled (the owner's earlier
                // report, or the sim after a leaver / a stalled owner) is never overwritten by a
                // late report, or a stage that moved on could have its feed reset under it.
                CupLog.Warn("ApplyRoundResult: " + CupStages.Short(reported.Stage) + " #" + reported.Index + " is already decided - late report ignored");
                return false;
            }
            if (round.EntrantA != reported.EntrantA || round.EntrantB != reported.EntrantB)
            {
                CupLog.Warn("ApplyRoundResult: entrants differ for " + CupStages.Short(reported.Stage) + " #" + reported.Index + " - refused");
                return false;
            }
            if (!reported.FirstKicker.HasValue) { CupLog.Warn("ApplyRoundResult: no first kicker - refused"); return false; }
            RoundLine line;
            string err;
            if (!CupRoundRules.Validate(reported.Kicks, reported.FirstKicker.Value, CupTuning.KicksEach, true, out line, out err))
            {
                CupLog.Warn("ApplyRoundResult: " + err + " - refused");
                return false;
            }
            return RecordResult(round, line, false);
        }

        // ==========================================================================================
        // Bracket and stage plumbing (shared by every flow)
        // ==========================================================================================

        /// <summary>
        /// Build the draw from the picks: Solo / Head to Head enter every active player's nation
        /// under their slot; Co-op enters ONE entrant (the team's nation under the Captain's slot,
        /// named YOUR TEAM). AI nations come from CupNations.ResolvedPool (the same on every peer).
        /// Resets the stage to the Round of 32 and refreshes every player's entrant. False (logged)
        /// when a pick is missing or the draw refuses.
        /// </summary>
        public bool BuildBracket()
        {
            var humans = new List<(int nationIndex, int humanSlot, string humanName)>();
            if (Style == CupStyle.Coop)
            {
                if (TeamNation < 0) { CupLog.Error("BuildBracket: Co-op has no team nation yet"); return false; }
                humans.Add((TeamNation, CaptainSlot, CupText.YourTeam));
            }
            else
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (!p.Active) continue;
                    if (p.Nation < 0) { CupLog.Error("BuildBracket: " + p.Name + " has not picked"); return false; }
                    humans.Add((p.Nation, p.Slot, p.Name));
                }
                if (humans.Count == 0) { CupLog.Error("BuildBracket: no active players"); return false; }
            }
            try
            {
                Bracket = CupBracket.Build(Seed, Format, humans, CupNations.ResolvedPool());
            }
            catch (Exception e)
            {
                CupLog.Error("BuildBracket: " + e.Message);
                Bracket = null;
                return false;
            }
            Stage = CupStage.RoundOf32;
            RefreshPlayersFromBracket();
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(Stage);
            Notify();
            return true;
        }

        /// <summary>Install a bracket received from the host (a late joiner / CupState) and refresh the players.</summary>
        public void SetBracket(CupBracket bracket)
        {
            Bracket = bracket;
            if (bracket != null) Stage = bracket.CurrentStage;
            RefreshPlayersFromBracket();
            Notify();
        }

        /// <summary>Re-derive every player's Entrant / Nation / Out (and LocalEntrant) from the bracket.</summary>
        public void RefreshPlayersFromBracket()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (Bracket == null)
                {
                    p.Entrant = -1;
                    p.Out = false;
                    continue;
                }
                int entrant = Style == CupStyle.Coop ? Bracket.EntrantOfHuman(CaptainSlot) : Bracket.EntrantOfHuman(p.Slot);
                p.Entrant = entrant;
                if (entrant >= 0)
                {
                    p.Nation = Bracket.Entrants[entrant].NationIndex;
                    p.Out = Bracket.IsEliminated(entrant);
                    if (p.Out) p.Ready = true;
                }
                else
                {
                    p.Out = false;
                }
            }
            var me = LocalPlayer;
            LocalEntrant = me != null ? me.Entrant : -1;
        }

        /// <summary>The local player's round this stage (null when out, or before the draw).</summary>
        public CupRound LocalRoundThisStage => Bracket != null && LocalEntrant >= 0 ? Bracket.RoundOfEntrant(Stage, LocalEntrant) : null;

        /// <summary>Simulate every AI-vs-AI round of a stage ("Simulating the rest of the stage"); returns how many.</summary>
        public int SimulateAiRounds(CupStage stage)
        {
            if (Bracket == null) return 0;
            int n = CupSim.SimulateStage(Bracket, stage, new SeededRng(Seed), true);
            RefreshPlayersFromBracket();
            if (n > 0) Notify();
            return n;
        }

        /// <summary>
        /// Store a played round's result (throws nothing: a level line is logged and refused).
        /// Marks the loser Out and refreshes the players.
        /// </summary>
        public bool RecordResult(CupRound round, RoundLine line, bool simulated = false)
        {
            if (Bracket == null || round == null || line == null) return false;
            try
            {
                Bracket.SetResult(round, line, simulated);
            }
            catch (Exception e)
            {
                CupLog.Error("RecordResult: " + e.Message);
                return false;
            }
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Entrant >= 0 && round.Involves(p.Entrant)) p.ClearLive();
            }
            RefreshPlayersFromBracket();
            Notify();
            return true;
        }

        /// <summary>
        /// The stage is complete: feed the winners into the next stage, move Stage on, clear the
        /// ready gate, write the next stage's keeper ramp. False at the Final or while a round of
        /// the stage is still pending.
        /// </summary>
        public bool AdvanceStage()
        {
            if (Bracket == null || !Bracket.StageComplete(Stage) || CupStages.IsLast(Stage)) return false;
            if (!Bracket.Advance(Stage)) return false;
            Stage = CupStages.Next(Stage);
            for (int i = 0; i < _players.Count; i++) _players[i].Ready = _players[i].Out || _players[i].Left;
            ClearLoaded();
            ClearCoinCalls();
            RefreshPlayersFromBracket();
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(Stage);
            Notify();
            return true;
        }

        /// <summary>Play Again: forget the draw and the picks, take a new seed, back to CHOOSE YOUR NATION.</summary>
        public void ResetForNewCup(uint seed)
        {
            EndRound();
            Seed = seed;
            Bracket = null;
            Stage = CupStage.RoundOf32;
            LocalEntrant = -1;
            TeamNation = -1;
            CoopOrder = new int[0];
            LeverPulls = 0;
            for (int i = 0; i < _players.Count; i++) _players[i].ResetForNewCup();
            RecountNationVotes();
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(Stage);
            SetPhase(CupPhase.NationPick);
        }

        // ==========================================================================================
        // Rounds
        // ==========================================================================================

        /// <summary>A fresh per-round root under this director (the previous one is destroyed).</summary>
        public Transform NewRoundRoot()
        {
            DestroyRoundRoot();
            var go = new GameObject(CurrentRound != null
                ? "RoundRoot " + CupStages.Short(CurrentRound.Stage) + " #" + CurrentRound.Index
                : "RoundRoot");
            go.transform.SetParent(transform, false);
            RoundRoot = go.transform;
            return RoundRoot;
        }

        /// <summary>Destroy the per-round root (and everything the round built under it).</summary>
        public void DestroyRoundRoot()
        {
            if (RoundRoot == null) return;
            Destroy(RoundRoot.gameObject);
            RoundRoot = null;
        }

        /// <summary>Which authority a round runs under on THIS machine.</summary>
        public RoundAuthority AuthorityFor(CupRound round)
        {
            if (!IsNetworked || round == null || Bracket == null) return RoundAuthority.Local;
            if (Style == CupStyle.Coop) return Multiplayer.IsHost ? RoundAuthority.Host : RoundAuthority.Client;
            bool humanA = Bracket.IsValidEntrant(round.EntrantA) && Bracket.Entrants[round.EntrantA].IsHuman;
            bool humanB = Bracket.IsValidEntrant(round.EntrantB) && Bracket.Entrants[round.EntrantB].IsHuman;
            if (humanA && humanB) return Multiplayer.IsHost ? RoundAuthority.Host : RoundAuthority.Client;   // head to head: host-simulated
            return RoundAuthority.Local;   // a human-vs-AI round is simulated on its owner's machine
        }

        /// <summary>
        /// The official coin caller of a round: the human against AI; the Captain in Co-op; a
        /// seeded random side when two humans meet (CupSalts.Caller). slot is -1 when an AI calls
        /// (an AI-vs-AI round, never played).
        /// </summary>
        public void CoinCallerFor(CupRound round, out CupSide side, out int slot)
        {
            side = CupSide.A;
            slot = -1;
            if (round == null || Bracket == null) return;
            if (Style == CupStyle.Coop)
            {
                var team = LocalEntrant >= 0 ? round.SideOf(LocalEntrant) : null;
                side = team ?? CupSide.A;
                slot = CaptainSlot;
                return;
            }
            var ea = Bracket.IsValidEntrant(round.EntrantA) ? Bracket.Entrants[round.EntrantA] : null;
            var eb = Bracket.IsValidEntrant(round.EntrantB) ? Bracket.Entrants[round.EntrantB] : null;
            bool humanA = ea != null && ea.IsHuman, humanB = eb != null && eb.IsHuman;
            if (humanA && humanB)
            {
                var rng = new SeededRng(Seed).Fork(CupSalts.Caller(round.Stage, round.Index));
                side = rng.Coin() == CoinFace.Heads ? CupSide.A : CupSide.B;
            }
            else if (humanA) side = CupSide.A;
            else if (humanB) side = CupSide.B;
            else return;
            var e = side == CupSide.A ? ea : eb;
            slot = e != null && e.IsHuman ? e.HumanSlot : -1;
        }

        /// <summary>
        /// Everything a driver needs for a round, from this director's state: format, stage, the
        /// human slot on each side, the Co-op order, the local side, the coin caller, the cup root
        /// RNG and the scene references. Root is NOT set (StartRound sets it to the new RoundRoot).
        /// </summary>
        public CupRoundSetup MakeRoundSetup(CupRound round)
        {
            var s = new CupRoundSetup();
            s.Style = Style;
            s.Format = Format;
            s.Stage = round != null ? round.Stage : Stage;
            s.Rng = new SeededRng(Seed);
            s.Input = Input;
            s.Cam = Cam;
            s.GameCam = GameCam;
            s.Ball = Ball;
            s.Goal = Goal;
            s.Torso = Torso;
            s.Limb = Limb;
            s.Glove = Glove;
            s.LocalSlot = LocalSlot;
            if (round == null || Bracket == null) return s;

            var ea = Bracket.IsValidEntrant(round.EntrantA) ? Bracket.Entrants[round.EntrantA] : null;
            var eb = Bracket.IsValidEntrant(round.EntrantB) ? Bracket.Entrants[round.EntrantB] : null;
            s.HumanSlotA = ea != null && ea.IsHuman ? ea.HumanSlot : -1;
            s.HumanSlotB = eb != null && eb.IsHuman ? eb.HumanSlot : -1;

            if (Style == CupStyle.Coop)
            {
                var team = LocalEntrant >= 0 ? round.SideOf(LocalEntrant) : null;
                s.TeamSide = team ?? CupSide.A;
                s.LocalOnSideA = s.TeamSide == CupSide.A;
                // Only a COMPLETE order reaches a round (the OrderPick gate); a partial one (an
                // empty entry would plan a body for "slot -1") falls back to the slots in order.
                var order = CoopOrderComplete ? CoopOrder : ActiveSlots().ToArray();
                if (!CoopOrderComplete) CupLog.Warn("MakeRoundSetup: Co-op round with no complete shooting order - using the slots in order");
                s.CoopOrderSlots = (int[])order.Clone();
                s.CoopKeeperSlot = order.Length > 0 ? order[0] : -1;
                s.HumanSlotA = s.TeamSide == CupSide.A ? s.CoopKeeperSlot : -1;
                s.HumanSlotB = s.TeamSide == CupSide.B ? s.CoopKeeperSlot : -1;
            }
            else
            {
                var mine = LocalEntrant >= 0 ? round.SideOf(LocalEntrant) : null;
                s.LocalOnSideA = mine.HasValue ? mine.Value == CupSide.A : (s.HumanSlotA == LocalSlot || s.HumanSlotB != LocalSlot);
                s.TeamSide = s.LocalOnSideA ? CupSide.A : CupSide.B;
            }

            CupSide callerSide;
            int callerSlot;
            CoinCallerFor(round, out callerSide, out callerSlot);
            s.CoinCaller = callerSide;
            s.CoinCallerSlot = callerSlot;
            s.FirstKicker = round.FirstKicker ?? CupSide.A;
            return s;
        }

        /// <summary>
        /// Start a round: a fresh RoundRoot, a driver under it, Configure with
        /// <see cref="MakeRoundSetup"/> (the driver builds its scene in Configure - do this under
        /// the loading card). The coin toss runs next; then SetFirstKicker and Begin. Returns the
        /// driver (null and logged when the round is not Ready).
        /// </summary>
        public CupRoundDriver StartRound(CupRound round, RoundAuthority? authority = null)
        {
            if (Bracket == null || round == null || !round.Ready)
            {
                CupLog.Error("StartRound: round is null or not ready");
                return null;
            }
            EndRound();
            CurrentRound = round;
            var root = NewRoundRoot();
            var setup = MakeRoundSetup(round);
            setup.Root = root;
            var go = new GameObject("CupRoundDriver");
            go.transform.SetParent(root, false);
            var drv = go.AddComponent<CupRoundDriver>();
            // The rig BEFORE Configure: the driver calls no camera in Configure (it only parks
            // everyone at their marks), but a driver that reads Rig at all reads it from here on.
            drv.Rig = Rig;
            drv.Configure(this, round, authority ?? AuthorityFor(round), setup);
            Driver = drv;
            if (drv.Configured)
            {
                // The choreography lives under the round root (it dies with the bodies it moves);
                // the HUD binds right after Configure so the 0-0 scoreboard and the coin toss's
                // HEADS / TAILS flash are up during the ceremony; the career-stats listener latches
                // taker / keeper at every whistle. Choreo fires no hooks on a Client, so creating
                // it there costs nothing.
                drv.AttachChoreo(CupChoreo.Create(root, drv, Rig));
                if (Hud != null) Hud.Bind(drv);
                BindRoundStats(drv);
            }
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(round.Stage);   // the baseline; the driver writes it per kick
            var me = LocalPlayer;
            if (me != null && me.Entrant >= 0 && round.Involves(me.Entrant))
                me.SetLive(Bracket.Entrants[round.OpponentOf(me.Entrant)].NationIndex, 0, 0, 1);
            Notify();
            return drv;
        }

        /// <summary>Refresh the local player's live row from the driver (call from a flow tick while Phase == Round).</summary>
        public void UpdateLiveRow()
        {
            var me = LocalPlayer;
            if (me == null || Driver == null || CurrentRound == null || me.Entrant < 0 || !CurrentRound.Involves(me.Entrant)) return;
            var side = CurrentRound.SideOf(me.Entrant) ?? CupSide.A;
            int own = side == CupSide.A ? Driver.ScoreA : Driver.ScoreB;
            int theirs = side == CupSide.A ? Driver.ScoreB : Driver.ScoreA;
            me.SetLive(Bracket.Entrants[CurrentRound.OpponentOf(me.Entrant)].NationIndex, own, theirs, Driver.KickIndex + 1);
        }

        /// <summary>Tear the current round down (aborting it if still running). Its result must have been recorded first.</summary>
        public void EndRound()
        {
            // Unhook everything that listens to the driver BEFORE the root goes: the HUD's
            // events and Callout, the stats listener, a ceremony still walking. The GameObjects
            // die at end of frame either way; the subscriptions would not.
            if (Toss != null) { Toss.Cancel(); Toss = null; }
            EndTrophyLift();   // it runs on this round's bodies; never let it outlive them
            if (Hud != null) Hud.Unbind();
            UnbindRoundStats();
            if (Driver != null && Driver.Running) Driver.Abort();
            bool had = Driver != null || CurrentRound != null || RoundRoot != null;
            Driver = null;
            CurrentRound = null;
            DestroyRoundRoot();
            if (Rig != null) Rig.Release();
            var me = LocalPlayer;
            if (me != null) me.ClearLive();
            // Between rounds in MP (design 9.5): the snapshot buffer and every slot's buffered
            // input are forgotten, so the next round never interpolates from a dead body or
            // inherits a held button. The tick counter is NOT reset (it must stay monotonic).
            if (had) NetRoundEnded();
            if (had) Notify();
        }

        // ==========================================================================================
        // Round services shared by every flow: the coin toss, the career writes, the backdrop,
        // the podium seam. Style-neutral on purpose - the Head to Head and Co-op partials call
        // the same helpers Solo does.
        // ==========================================================================================

        /// <summary>
        /// Start the coin toss ceremony for the configured round (design 7.1 / 6.11). Call it
        /// from the CoinToss phase - CallCoin drops calls outside it - after StartRound. The
        /// previous round's calls are forgotten first (the ceremony reads CupPlayer.CoinCall as
        /// THIS toss's call). `onDone` fires after the flash (and the Co-op calls band); by then
        /// the driver carries the outcome (SetCoinOutcome / SetFirstKicker) and the calls are
        /// resolved on the authority. A round with no scene fires onDone at once; either way
        /// <see cref="EnsureCoinOutcome"/> makes the driver's outcome definite before Begin.
        /// </summary>
        public CupCoinToss BeginCoinToss(Action onDone)
        {
            if (Toss != null) { Toss.Cancel(); Toss = null; }
            ClearCoinCalls();
            Notify();
            var toss = CupCoinToss.Begin(this, Driver, Rig, () =>
            {
                Toss = null;
                onDone?.Invoke();
            });
            // A null return fired onDone synchronously (nothing to run the ceremony in); keeping
            // the field null then is exactly right.
            Toss = toss;
            return toss;
        }

        /// <summary>
        /// The driver must know the coin before Begin: the intro card names the first kicker and
        /// the kick line starts from it. The ceremony records it the moment the flip starts; if
        /// no ceremony ran (or it was cut short), derive the same outcome the ceremony would
        /// have - the Coin stream's first draw is the result, the official caller's recorded
        /// call (an AI caller's is the second draw, an idle human's HEADS) decides kick-off.
        /// </summary>
        public void EnsureCoinOutcome()
        {
            var d = Driver;
            if (d == null || !d.Configured || d.Setup == null || d.Data == null) return;
            if (d.CoinResult.HasValue) return;
            var stream = d.Setup.Stream(CupSalts.Coin(d.Setup.Stage, d.Data.Index));
            var result = stream.Coin();
            var aiCall = stream.Coin();
            CoinFace call;
            int callerSlot = d.Setup.CoinCallerSlot;
            var caller = callerSlot >= 0 ? PlayerAt(callerSlot) : null;
            if (callerSlot < 0) call = aiCall;
            else if (caller != null && caller.CoinCall.HasValue) call = caller.CoinCall.Value;
            else call = CoinFace.Heads;   // design 10: an idle official caller calls heads
            var first = CupRoundRules.FirstKickerFromCall(d.Setup.CoinCaller, call, result);
            d.SetCoinOutcome(call, result);
            d.SetFirstKicker(first);
            if (IsAuthority && !CoinCallsResolved()) ResolveCoinCalls(result);
        }

        /// <summary>True when every recorded call already carries a verdict (ResolveCoinCalls ran for this toss).</summary>
        bool CoinCallsResolved()
        {
            bool any = false;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.CoinCall.HasValue) continue;
                any = true;
                if (!p.CoinCallRight.HasValue) return false;
            }
            return any;
        }

        /// <summary>Career (design 9.7): the local player's call for the toss that just landed, right or wrong.</summary>
        public void RecordLocalCoinCall()
        {
            var me = LocalPlayer;
            var d = Driver;
            if (me == null || !me.CoinCall.HasValue || d == null || !d.CoinResult.HasValue) return;
            CupCareer.CoinCalled(me.CoinCall.Value, d.CoinResult.Value, Style);
        }

        /// <summary>
        /// Career (design 9.7): a decided round the local player was part of - won / lost, sudden
        /// death, clean sheet, giant kill, all derived from the record. Co-op: every teammate
        /// shares the team's entrant, so every machine records its own line.
        /// </summary>
        public void RecordLocalRoundCareer(CupRound round)
        {
            var me = LocalPlayer;
            if (me == null || me.Entrant < 0 || round == null || !round.Done || !round.Involves(me.Entrant)) return;
            var side = round.SideOf(me.Entrant);
            if (!side.HasValue) return;
            CupCareer.RoundDecided(round, side.Value, Bracket, Style);
        }

        /// <summary>The nation a bracket entrant wears (a CupNations index), -1 when unknown.</summary>
        public int NationOfEntrant(int entrant)
        {
            if (Bracket == null || !Bracket.IsValidEntrant(entrant)) return -1;
            return Bracket.Entrants[entrant].NationIndex;
        }

        // ---- per-kick career stats: taker / keeper latched at the whistle, banked at the verdict --
        CupRoundDriver _statsDriver;
        int _statsTaker = -1, _statsKeeper = -1;
        bool _statsLatched;

        /// <summary>
        /// Listen to a round for the local player's own kicks (KickTaken / KickKept). Who took
        /// and who kept are read at the WHISTLE (TakerSlotForNextKick / KeeperSlotForNextKick
        /// describe the next kick), because by the time KickResolved fires the line has moved on
        /// to the following kick. Same latch as CupStatsLedger's, kept separate so the career
        /// file never depends on a screen having been opened.
        /// </summary>
        void BindRoundStats(CupRoundDriver drv)
        {
            UnbindRoundStats();
            if (drv == null) return;
            _statsDriver = drv;
            _statsLatched = false;
            drv.PhaseChanged += OnStatsPhase;
            drv.KickResolved += OnStatsKick;
        }

        void UnbindRoundStats()
        {
            if (_statsDriver != null)
            {
                _statsDriver.PhaseChanged -= OnStatsPhase;
                _statsDriver.KickResolved -= OnStatsKick;
            }
            _statsDriver = null;
            _statsLatched = false;
        }

        void OnStatsPhase(RoundPhase phase)
        {
            if (phase != RoundPhase.Armed || _statsDriver == null) return;
            _statsTaker = _statsDriver.TakerSlotForNextKick;
            _statsKeeper = _statsDriver.KeeperSlotForNextKick;
            _statsLatched = true;
        }

        void OnStatsKick(KickOutcome outcome, CupSide side, int scorerSlot)
        {
            var d = _statsDriver;
            if (d == null || LocalSlot < 0) return;
            int taker, keeper;
            if (_statsLatched)
            {
                taker = _statsTaker;
                keeper = _statsKeeper;
            }
            else
            {
                // No whistle seen for this kick (a client catching up from a state burst): the
                // one-human-per-side styles are still exact from the setup; a Co-op shooter's
                // identity is not (the order cycles), so only the keeper is credited there.
                var s = d.Setup;
                if (s == null) return;
                taker = s.Style == CupStyle.Coop ? -1 : s.HumanSlotOf(side);
                keeper = s.HumanSlotOf(CupSides.Other(side));
            }
            _statsLatched = false;
            if (outcome == KickOutcome.Goal && scorerSlot >= 0) taker = scorerSlot;   // the driver knows best
            if (taker == LocalSlot) CupCareer.KickTaken(outcome, Style);
            else if (keeper == LocalSlot) CupCareer.KickKept(outcome, Style);
        }

        /// <summary>
        /// A deliberate backdrop for the menu screens between rounds: a static wide shot down the
        /// pitch toward the goal from behind the penalty spot (the coin toss framing, turned
        /// round), so the scrimmed screens sit over the empty stadium rather than over whatever
        /// the last camera happened to be looking at. Released again by the next round (the
        /// driver's Intro entry and EndRound both hand the camera back).
        /// </summary>
        public void MenuBackdrop()
        {
            if (Rig == null) return;
            Rig.CoinTossView(CupSpots.Ground(CupSpots.PenaltySpot), Vector3.back);
        }

        // ---- the endings (design 8): the podium and the trophy lift ------------------------------
        // Style-neutral entry helpers: Solo and Head to Head enter the podium after a won Final
        // (TryBeginPodium / EndPodium), Co-op the trophy lift (BeginTrophyLift / EndTrophyLift).
        // Both build under this director, own their props, and lead on through ContinueFromResults.
        CupPodium _podium;
        Transform _podiumRoot;

        /// <summary>The podium in progress (Solo / Head to Head), null outside the phase.</summary>
        public CupPodium Podium => _podium;
        /// <summary>The trophy lift in progress (Co-op), null outside the phase.</summary>
        public CupTrophyLift TrophyLift { get; private set; }
        /// <summary>Kept for callers that probed the seam while the podium was pending: it is in the build.</summary>
        public static bool PodiumAvailable => true;

        /// <summary>
        /// Start the podium (Solo / Head to Head, design 8.1): a fresh PodiumRoot under this
        /// director, the champion from the bracket, the losers from the players and the bracket.
        /// Call it after EndRound (the round's bodies are gone; the podium spawns its own) with the
        /// Final's result recorded. Its Continue is ContinueFromResults (Podium -> Results, the CUP
        /// SUMMARY); its New Cup / Play Again / Main Menu / End Match are the director's intents.
        /// False (logged by the podium) when there is no champion to crown - the flow then goes
        /// straight to the results.
        /// </summary>
        public bool TryBeginPodium()
        {
            EndPodium();
            _podiumRoot = new GameObject("PodiumRoot").transform;
            _podiumRoot.SetParent(transform, false);
            _podium = CupPodium.Begin(this, _podiumRoot, Rig, Hud, Input, Cam, GameCam, Ball, ContinueFromResults);
            if (_podium == null)
            {
                EndPodium();
                return false;
            }
            return true;
        }

        /// <summary>Tear the podium down (the phase ended, Play Again, teardown). Safe when none is up.</summary>
        public void EndPodium()
        {
            if (_podium != null) Destroy(_podium.gameObject);
            _podium = null;
            if (_podiumRoot != null) Destroy(_podiumRoot.gameObject);
            _podiumRoot = null;
        }

        /// <summary>
        /// Start the trophy lift (Co-op, design 8.2) on the ROUND'S bodies: call it at Driver.Phase
        /// == Over with the result recorded and BEFORE EndRound (the bodies must still stand). The
        /// team is the driver's bodies on the team's side, the Captain his body, the referee the
        /// round's. The HUD is unbound here (its banner and scoreboard would sit over the picture).
        /// The lift's Continue ends the lift, ends the round (the bodies die) and then runs
        /// `onContinue` - ContinueFromResults when null (TrophyLift -> Results, CHAMPIONS). False
        /// (logged) when no round is standing - the flow then EndRound()s and goes to the results.
        /// </summary>
        public bool BeginTrophyLift(Action onContinue = null)
        {
            EndTrophyLift();
            var drv = Driver;
            if (drv == null || !drv.Configured || drv.Setup == null)
            {
                CupLog.Warn("CupDirector: BeginTrophyLift with no configured round - skipped");
                return false;
            }
            var side = drv.Setup.TeamSide;
            var team = drv.BodiesOn(side);
            var captain = drv.CaptainBody(side);
            if (Hud != null) Hud.Unbind();
            var cb = onContinue ?? (Action)ContinueFromResults;
            TrophyLift = CupTrophyLift.Begin(this, team, captain, drv.Referee, Rig, () =>
            {
                EndTrophyLift();
                EndRound();
                cb();
            });
            return TrophyLift != null;
        }

        /// <summary>Tear the trophy lift down (Continue, a leave, teardown). Safe when none is up.</summary>
        public void EndTrophyLift()
        {
            var lift = TrophyLift;
            TrophyLift = null;
            if (lift != null) lift.End();
        }

        /// <summary>One log line of the whole read model.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(CupText.Label(Style, Format)).Append(" seed ").Append(Seed).Append(' ').Append(Phase)
              .Append(' ').Append(PhaseTime.ToString("0.0")).Append("s stage ").Append(CupStages.Short(Stage));
            if (CurrentRound != null) sb.Append(" round #").Append(CurrentRound.Index);
            sb.Append(" local ").Append(LocalSlot).Append(" (e").Append(LocalEntrant).Append(") captain ").Append(CaptainSlot);
            for (int i = 0; i < _players.Count; i++) sb.Append("\n  ").Append(_players[i]);
            return sb.ToString();
        }
    }
}
