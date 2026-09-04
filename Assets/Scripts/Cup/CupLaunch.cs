using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The three numbers a cup is launched with - style, format, seed - parked between the screen
    /// that decided them and the BuildMode that needs them. GameBootstrap.BuildMode(TrickshotCup)
    /// reads only this: it has to apply the cup statics (regulation goal, PenaltyMode per format)
    /// ABOVE Arena.Build, before there is any director to ask, and the director's Launch takes the
    /// same three values a moment later.
    ///
    ///   Solo: the fork screen (CupSetupUI) picks the format; the seed is rolled right there
    ///         (CupDirector.RollSeed) - see <see cref="Solo"/>.
    ///   Head to Head / Co-op: the host's MatchConfig carries cupStyle / cupFormat / fkSeed to
    ///         every peer, and StartNetworkedMatch copies them in - see <see cref="FromConfig"/>.
    ///
    /// Plain statics with no behaviour: this is a hand-off, not a state machine. Nothing reads it
    /// after Launch; Play Again rolls its own seed inside the director.
    /// </summary>
    public static class CupLaunch
    {
        /// <summary>Solo (SP fork), Head to Head or Co-op (the host's config).</summary>
        public static CupStyle Style = CupStyle.Solo;
        /// <summary>Penalties or Free Kicks. Decides SimConfig.PenaltyMode before the arena is built.</summary>
        public static CupFormat Format = CupFormat.Penalties;
        /// <summary>The cup seed (the whole draw, every spot, every coin, every simulated round).
        /// 0 = not set: <see cref="Seed"/>'s reader rolls one for Solo and substitutes a fixed
        /// fallback in multiplayer, where every peer must agree without talking.</summary>
        public static uint Seed;

        /// <summary>Kept for the SP path's callers by name: the format the fork screen picked.</summary>
        public static CupFormat SoloFormat
        {
            get => Format;
            set => Format = value;
        }

        /// <summary>The seed peers fall back to when a config arrives without one (never expected:
        /// HostSetupUI always rolls fkSeed). A constant, so the fallback is still identical everywhere.</summary>
        public const uint FallbackSeed = 0x9E3779B9u;

        /// <summary>A solo cup: the fork's format, a fresh seed.</summary>
        public static void Solo(CupFormat format)
        {
            Style = CupStyle.Solo;
            Format = format;
            Seed = CupDirector.RollSeed();
        }

        /// <summary>A networked cup from the host's synced config (StartNetworkedMatch, every peer).
        /// A config with cupStyle 0 (Solo, which is never hosted) or an unknown value reads as
        /// Head to Head, exactly as NetSession labels it.</summary>
        public static void FromConfig(in MatchConfig cfg)
        {
            Style = NetSession.CupStyleOf(cfg);
            Format = cfg.cupFormat == (byte)CupFormat.FreeKicks ? CupFormat.FreeKicks : CupFormat.Penalties;
            Seed = cfg.fkSeed != 0u ? cfg.fkSeed : FallbackSeed;
        }

        /// <summary>The seed to launch with: what was set, else (Solo) a fresh roll or (MP) the
        /// shared fallback - a solo cup must never reuse the previous cup's draw by accident, and
        /// a networked one must never differ between peers.</summary>
        public static uint SeedForLaunch()
        {
            if (Seed != 0u) return Seed;
            Seed = Style == CupStyle.Solo ? CupDirector.RollSeed() : FallbackSeed;
            return Seed;
        }

        /// <summary>Forget the parked values (match teardown), so a later BuildMode that somehow
        /// skipped the screens cannot launch yesterday's cup.</summary>
        public static void Clear()
        {
            Style = CupStyle.Solo;
            Format = CupFormat.Penalties;
            Seed = 0u;
        }
    }
}
