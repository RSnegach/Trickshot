using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The AI taker of a cup round: an <see cref="IStrikerInput"/> that plays a <see cref="SetPieceTaker"/>
    /// the way a person does, so the AI gets the REAL charge, run-up and strike (design 9.1) instead
    /// of NetSetPieceMatch's standing AutoLaunch. Everything the taker reads while charging is
    /// synthesised here from the round's seeded stream:
    ///
    ///   - waits CupTuning.BotDelayMin..BotDelayMax seconds after the whistle (a beat of composure);
    ///   - HOLDS Space (JumpHeld) so the meter starts its sweep, and RELEASES it the first time the
    ///     meter reaches the stage's power target (CupTuning.TakerPower, plus a hair of jitter so
    ///     five AI kicks in a row are not carbon copies);
    ///   - holds one seeded WASD spin (curl left / right, topspin, or none) for a seeded fraction of
    ///     the charge, short of the over-hold botch window, so the flavour varies without a spray.
    ///
    /// How SetPieceTaker reads it (SetPieceTaker.TickCharging): ChargeHeld == JumpHeld; a held
    /// button ping-pongs the meter and charges the dominant Move axis as spin; the FIRST release
    /// after SetPieceMinChargeTime commits, after SetPieceReleaseDebounce of continuous "up". The
    /// meter target is always below the 0.97 peg, so the bot never overcharges. Aim is left to
    /// BallController's corner auto-aim (aimPoint null) with the stage's `combinedOverride`
    /// (CupTuning.TakerCombined), which is the difficulty ramp of design 2.2.
    ///
    /// Every other input is false / zero, `Fresh` is true (a synthetic source is always current)
    /// and `EmoteId` is 255 (the AI never picks an emote; the choreography plays its emotes by code).
    ///
    /// Usage per kick: Arm(taker, stage) BEFORE taker.Begin(this, ...) (so Begin sees Space UP and
    /// does not latch its stale-press guard), then Tick(dt) every frame ahead of taker.Tick().
    /// </summary>
    public sealed class CupBotTaker : IStrikerInput
    {
        /// <summary>
        /// The bot's own salt family (0x8000 block): outside every family CupSalts defines (0x1000
        /// Sim .. 0x6000 Order, 0x7000 Podium / 0x7001 Confetti), so a bot stream can never collide
        /// with a peer-shared stream. The bot only runs on the machine simulating the round, so its
        /// draws need not agree across peers - but deriving them from the cup seed keeps a round
        /// reproducible, which is worth more than nothing when tuning.
        /// </summary>
        const uint SaltFamily = 0x8000u;

        /// <summary>The per-round salt for a bot stream (family + stage * 16 + round index).</summary>
        public static uint Salt(CupStage stage, int roundIndex) => SaltFamily + (uint)stage * 16u + (uint)roundIndex;

        // Power target jitter, either way, around CupTuning.TakerPower(stage). Small on purpose:
        // the stage ramp is the difficulty, this is only texture (design 2.2 forbids a knob here).
        const float PowerJitter = 0.04f;
        // Never let the target reach the taker's peg band (meter > 0.97 = overcharge).
        const float PowerCeiling = 0.95f;
        // How long the spin key is held once charging starts (seconds): the taker charges spin at
        // SetPieceSpinChargeRate per second, so this range lands 0.35..0.9 of spin charge, and it
        // stays well inside SetPieceSpinOverTime so an AI kick is never botched by its own spin.
        const float SpinHoldMin = 0.32f;
        const float SpinHoldMax = 0.82f;
        // How often an AI taker kicks with his left foot (footedness is cosmetic - the swing side).
        const float LeftFootChance = 0.25f;

        readonly SeededRng _rng;
        SetPieceTaker _taker;

        bool _armed;
        bool _released;      // Space has come up: the commit is the taker's from here
        float _t;            // seconds since Arm
        float _delay;        // seconds of composure before the charge starts
        float _target;       // meter value that triggers the release
        float _chargeT;      // seconds Space has been held
        Vector2 _spinKey;    // the WASD direction held while charging (zero = no spin)
        float _spinHold;     // seconds the spin key stays down
        bool _leftFooted;

        public CupBotTaker(SeededRng rng)
        {
            _rng = rng ?? new SeededRng(1u);
        }

        /// <summary>An attempt is in progress (between Arm and Disarm).</summary>
        public bool Armed => _armed;
        /// <summary>Space has been released: the kick is committed (the taker runs it from here).</summary>
        public bool Fired => _released;
        /// <summary>The meter value this attempt releases at.</summary>
        public float TargetPower => _target;
        /// <summary>Seconds the bot waits after the whistle before charging.</summary>
        public float Delay => _delay;
        /// <summary>Which foot this attempt swings with (pass as SetPieceTaker.Begin's leftFootedOverride: 1 left, 0 right).</summary>
        public bool LeftFooted => _leftFooted;
        /// <summary>The spin flavour of this attempt, for logs.</summary>
        public BallController.SetPieceSpin Spin
        {
            get
            {
                if (_spinKey.x > 0.5f) return BallController.SetPieceSpin.CurveRight;
                if (_spinKey.x < -0.5f) return BallController.SetPieceSpin.CurveLeft;
                if (_spinKey.y > 0.5f) return BallController.SetPieceSpin.TopSpin;
                return BallController.SetPieceSpin.None;
            }
        }

        /// <summary>
        /// Roll this kick (delay, power target, spin, foot) and bind the taker whose meter the
        /// release watches. Call BEFORE taker.Begin so the first frame reads Space up.
        /// </summary>
        public void Arm(SetPieceTaker taker, CupStage stage)
        {
            _taker = taker;
            _armed = true;
            _released = false;
            _t = 0f;
            _chargeT = 0f;
            _delay = _rng.Range(CupTuning.BotDelayMin, CupTuning.BotDelayMax);
            _target = Mathf.Clamp(CupTuning.TakerPower(stage) + _rng.Range(-PowerJitter, PowerJitter), 0.2f, PowerCeiling);
            // Four flavours, equally likely: none, curl left, curl right, topspin (the same set
            // NetSetPieceMatch.AutoLaunch draws from). Knuckle is left out on purpose - it is the
            // "gamble" spin and reads as a fluke from an AI.
            switch (_rng.Range(0, 4))
            {
                case 1: _spinKey = new Vector2(-1f, 0f); break;
                case 2: _spinKey = new Vector2(1f, 0f); break;
                case 3: _spinKey = new Vector2(0f, 1f); break;
                default: _spinKey = Vector2.zero; break;
            }
            _spinHold = _rng.Range(SpinHoldMin, SpinHoldMax);
            _leftFooted = _rng.Chance(LeftFootChance);
        }

        /// <summary>Forget the attempt (the kick resolved, or the round was aborted). Every input reads idle.</summary>
        public void Disarm()
        {
            _armed = false;
            _released = false;
            _taker = null;
            _t = 0f;
            _chargeT = 0f;
        }

        /// <summary>
        /// Advance the bot's clock. Call once per frame BEFORE taker.Tick(): the release decision
        /// is made here off LAST frame's meter, so the taker then sees Space up and commits the
        /// value that was on the bar - exactly what a human release does.
        /// </summary>
        public void Tick(float dt)
        {
            if (!_armed || _released) return;
            _t += dt;
            if (!Charging) return;
            _chargeT += dt;
            // Release the first time the sweep reaches the target. HasCharged guards the frame the
            // hold begins (the meter is still 0 then, and a target of 0.2+ can never be met on it).
            if (_taker != null && _taker.HasCharged && _taker.Meter >= _target) _released = true;
        }

        /// <summary>Space is down: past the composure delay and not yet released.</summary>
        bool Charging => _armed && !_released && _t >= _delay;

        // ---- IStrikerInput --------------------------------------------------------------------
        /// <summary>The spin key, held for the first `_spinHold` seconds of the charge only.</summary>
        public Vector2 Move => Charging && _chargeT < _spinHold ? _spinKey : Vector2.zero;
        public float Scroll => 0f;
        public bool SprintHeld => false;
        public bool CloseControlHeld => false;
        public bool JumpPressed => false;
        public bool JumpHeld => Charging;
        public bool JumpReleased => false;
        public bool LeftLegHeld => false;
        public bool RightLegHeld => false;
        public bool ResetPressed => false;
        public bool LeftClickPressed => false;
        public bool RightClickPressed => false;
        public bool PassGroundPressed => false;
        public bool PassLoftedPressed => false;
        public bool PassGroundHeld => false;
        public bool PassLoftedHeld => false;
        public bool PassGroundReleased => false;
        public bool PassLoftedReleased => false;
        public bool PassChipPressed => false;
        public bool PassChipHeld => false;
        public bool PassChipReleased => false;
        /// <summary>A synthetic source is always current.</summary>
        public bool Fresh => true;
        /// <summary>The AI never picks an emote (255 = none); the choreography plays its emotes by code.</summary>
        public int EmoteId => 255;
        public bool CrossPressed => false;
        public bool ThirdLegHeld => false;
    }
}
