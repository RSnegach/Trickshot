using System;
using System.Collections.Generic;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// The cup's deterministic random stream. One <c>uint</c> seed (the host's MatchConfig.fkSeed,
    /// or a Solo roll) drives the whole draw, every free-kick spot, every coin result and every
    /// simulated AI round, on every peer, without any of it ever being synced.
    ///
    /// NEVER replace this with System.Random: its algorithm is not guaranteed stable across .NET
    /// runtimes (Mono, IL2CPP, CoreCLR) or versions, and two peers on different builds would draw
    /// different brackets from the same seed. This is xorshift32 with a SplitMix-style seed
    /// scramble, all integer arithmetic, so every platform agrees bit for bit.
    ///
    /// Design notes:
    /// - The scramble makes seed 0 and seed 1 unrelated streams, and the state can never be 0
    ///   (xorshift's fixed point).
    /// - <see cref="Next01"/> is derived from the top 24 bits of an integer draw, so it is exact
    ///   in a float and identical everywhere. The float helpers on top of it are ordinary
    ///   single-precision arithmetic; where peers must agree bit for bit on a float, derive an
    ///   integer with <see cref="Range(int,int)"/> and scale it yourself.
    /// - <see cref="Fork"/> is a PURE function of the parent's SEED and the salt: it neither
    ///   advances nor reads the parent's position, so "the coin of round 3" is the same stream on
    ///   every peer no matter how many draws each has taken from the parent. Use the salts in
    ///   <see cref="CupSalts"/>.
    /// </summary>
    public sealed class SeededRng
    {
        uint _state;
        readonly uint _seed;

        /// <summary>Start a stream from a seed. Any value is fine, including 0.</summary>
        public SeededRng(uint seed)
        {
            _seed = seed;
            _state = Scramble(seed);
        }

        /// <summary>The seed this stream was constructed from.</summary>
        public uint Seed => _seed;

        /// <summary>The current internal state (debugging / logging only; never 0).</summary>
        public uint State => _state;

        // SplitMix32-style finaliser: spreads a seed's bits so neighbouring seeds diverge at once,
        // and steers a zero result away from xorshift's fixed point.
        static uint Scramble(uint seed)
        {
            uint z = seed + 0x9E3779B9u;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            z ^= z >> 16;
            return z == 0u ? 0x2545F491u : z;
        }

        // Boost-style hash_combine of a seed and a salt, then scrambled by the child's constructor.
        static uint Mix(uint seed, uint salt)
        {
            uint h = seed;
            h ^= salt + 0x9E3779B9u + (h << 6) + (h >> 2);
            h ^= 0x7F4A7C15u;
            return h;
        }

        /// <summary>Next 32 random bits (xorshift32; never returns 0).</summary>
        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>A float in [0, 1): (NextUInt() >> 8) / 2^24, exact in single precision.</summary>
        public float Next01()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>
        /// An int in [minInclusive, maxExclusive). Returns minInclusive when the range is empty.
        /// Uses the multiply-high reduction (no modulo bias worth the name for any range the cup
        /// draws from, and no division).
        /// </summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            long span = (long)maxExclusive - minInclusive;
            if (span <= 0) return minInclusive;
            ulong r = ((ulong)NextUInt() * (ulong)span) >> 32;
            return (int)(minInclusive + (long)r);
        }

        /// <summary>A float in [min, max).</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * Next01();
        }

        /// <summary>True with probability p (p &lt;= 0 never, p &gt;= 1 always).</summary>
        public bool Chance(float p)
        {
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            return Next01() < p;
        }

        /// <summary>A fair coin, from the top bit of a draw (the best-mixed bit of xorshift32).</summary>
        public CoinFace Coin()
        {
            return (NextUInt() >> 31) == 0u ? CoinFace.Heads : CoinFace.Tails;
        }

        /// <summary>One element of a non-empty list; default(T) for an empty or null list.</summary>
        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[Range(0, list.Count)];
        }

        /// <summary>In-place Fisher-Yates shuffle.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                if (j == i) continue;
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>
        /// An independent child stream keyed by (this stream's SEED, salt). Pure: calling it twice
        /// with the same salt returns two identical streams, and it never advances the parent. Use a
        /// distinct salt per purpose (<see cref="CupSalts"/>); Fork(1) and Fork(2) are unrelated.
        /// </summary>
        public SeededRng Fork(uint salt)
        {
            return new SeededRng(Mix(_seed, salt));
        }

        /// <summary>Rewind to the first draw (same seed, fresh state).</summary>
        public void Reset()
        {
            _state = Scramble(_seed);
        }

        public override string ToString() => "SeededRng(seed=" + _seed + ", state=" + _state + ")";
    }
}
