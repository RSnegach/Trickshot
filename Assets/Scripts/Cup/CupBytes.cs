using System;
using System.Collections.Generic;
using System.Text;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// A tiny little-endian byte writer for the cup's own records (bracket, rounds, entrants), so
    /// the pure layer can serialise itself without the net layer's NetWriter. The net agent wraps
    /// the resulting byte[] in a message; nothing here knows about packets.
    /// </summary>
    public sealed class CupByteWriter
    {
        readonly List<byte> _b;

        public CupByteWriter(int capacity = 256)
        {
            _b = new List<byte>(capacity);
        }

        /// <summary>Bytes written so far.</summary>
        public int Count => _b.Count;

        public void U8(byte v) { _b.Add(v); }
        public void U8(int v) { _b.Add((byte)v); }
        public void Bool(bool v) { _b.Add(v ? (byte)1 : (byte)0); }

        public void U16(ushort v)
        {
            _b.Add((byte)v);
            _b.Add((byte)(v >> 8));
        }

        public void U16(int v) { U16((ushort)v); }

        public void U32(uint v)
        {
            _b.Add((byte)v);
            _b.Add((byte)(v >> 8));
            _b.Add((byte)(v >> 16));
            _b.Add((byte)(v >> 24));
        }

        public void I32(int v) { U32((uint)v); }

        /// <summary>A float, as its IEEE bits.</summary>
        public void F(float v)
        {
            U32((uint)BitConverter.ToInt32(BitConverter.GetBytes(v), 0));
        }

        /// <summary>A small signed index: -1 is written as 255; 0..254 as themselves.</summary>
        public void Slot(int v)
        {
            if (v < 0 || v > 254) _b.Add(255);
            else _b.Add((byte)v);
        }

        /// <summary>A UTF-8 string with a one-byte length (truncated to 255 bytes; null writes empty).</summary>
        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { _b.Add(0); return; }
            byte[] utf = Encoding.UTF8.GetBytes(s);
            int n = utf.Length;
            if (n > 255)
            {
                // Trim to 255 bytes on a character boundary so the reader never sees a torn code point.
                n = 255;
                while (n > 0 && (utf[n] & 0xC0) == 0x80) n--;
            }
            _b.Add((byte)n);
            for (int i = 0; i < n; i++) _b.Add(utf[i]);
        }

        /// <summary>Raw bytes with a two-byte length.</summary>
        public void Bytes(byte[] data)
        {
            if (data == null) { U16(0); return; }
            U16((ushort)Math.Min(data.Length, ushort.MaxValue));
            for (int i = 0; i < data.Length && i < ushort.MaxValue; i++) _b.Add(data[i]);
        }

        public byte[] ToArray() => _b.ToArray();
    }

    /// <summary>
    /// The matching reader. Every read throws <see cref="FormatException"/> on a truncated buffer
    /// rather than returning garbage, so a malformed packet surfaces at the parse, not later.
    /// </summary>
    public sealed class CupByteReader
    {
        readonly byte[] _d;
        int _pos;

        public CupByteReader(byte[] data, int offset = 0)
        {
            _d = data ?? new byte[0];
            _pos = offset;
        }

        public int Position => _pos;
        public int Remaining => _d.Length - _pos;
        public bool More => _pos < _d.Length;

        void Need(int n)
        {
            if (_pos + n > _d.Length)
                throw new FormatException("CupByteReader: truncated record (need " + n + " byte(s) at " + _pos + " of " + _d.Length + ")");
        }

        public byte U8()
        {
            Need(1);
            return _d[_pos++];
        }

        public bool Bool() => U8() != 0;

        public ushort U16()
        {
            Need(2);
            ushort v = (ushort)(_d[_pos] | (_d[_pos + 1] << 8));
            _pos += 2;
            return v;
        }

        public uint U32()
        {
            Need(4);
            uint v = (uint)(_d[_pos] | (_d[_pos + 1] << 8) | (_d[_pos + 2] << 16) | (_d[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int I32() => (int)U32();

        public float F()
        {
            uint bits = U32();
            return BitConverter.ToSingle(BitConverter.GetBytes((int)bits), 0);
        }

        /// <summary>The inverse of <see cref="CupByteWriter.Slot"/>: 255 reads as -1.</summary>
        public int Slot()
        {
            byte b = U8();
            return b == 255 ? -1 : b;
        }

        public string Str()
        {
            int n = U8();
            if (n == 0) return "";
            Need(n);
            string s = Encoding.UTF8.GetString(_d, _pos, n);
            _pos += n;
            return s;
        }

        public byte[] Bytes()
        {
            int n = U16();
            Need(n);
            var r = new byte[n];
            Array.Copy(_d, _pos, r, 0, n);
            _pos += n;
            return r;
        }
    }
}
