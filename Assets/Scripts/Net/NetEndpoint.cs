using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Trickshot.Net
{
    /// <summary>
    /// Codec between an IPv4 endpoint (address + port) and the opaque ulong "handle" that
    /// INetTransport.Join(ulong) already takes. This lets the direct-IP transport reuse the
    /// existing Join path (and the lobby-handle plumbing) without changing the interface:
    /// the browser/host UI turns a typed "ip" / "ip:port" string into a handle and passes it
    /// straight to Multiplayer.Join(ulong).
    ///
    /// Layout: bits 47..16 = IPv4 address (big-endian byte order, a.b.c.d -> a is the high
    /// byte), bits 15..0 = port. 48 bits total, well inside a ulong. IPv4 ONLY - IPv6 (incl.
    /// Tailscale's fd7a:... ULAs) is 128-bit and cannot be encoded; friends type the IPv4
    /// form (Tailscale also assigns a 100.x IPv4, which encodes fine).
    /// </summary>
    public static class NetEndpoint
    {
        public const int DefaultPort = 7777;

        // a.b.c.d -> (a<<24 | b<<16 | c<<8 | d). Explicit shifts (NOT BitConverter, which is
        // host-endian and would silently byte-swap depending on the machine).
        public static ulong Encode(IPEndPoint ep)
        {
            byte[] b = ep.Address.GetAddressBytes();               // 4 bytes for IPv4, network order
            uint ip = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            return ((ulong)ip << 16) | (ushort)ep.Port;
        }

        public static IPEndPoint Decode(ulong handle)
        {
            ushort port = (ushort)(handle & 0xFFFF);
            uint ip = (uint)(handle >> 16);
            var bytes = new byte[]
            {
                (byte)((ip >> 24) & 0xFF), (byte)((ip >> 16) & 0xFF),
                (byte)((ip >> 8) & 0xFF),  (byte)(ip & 0xFF),
            };
            return new IPEndPoint(new IPAddress(bytes), port);
        }

        /// <summary>
        /// Parse "1.2.3.4" or "1.2.3.4:7777" into a Join handle. Bare address uses
        /// DefaultPort. Returns false on empty/garbage/non-IPv4 input, or if the result
        /// would encode to 0 (0 is the reserved "invalid" handle).
        /// </summary>
        public static bool TryParse(string text, out ulong handle)
        {
            handle = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();

            string host = text;
            int port = DefaultPort;
            int colon = text.LastIndexOf(':');
            if (colon >= 0)
            {
                host = text.Substring(0, colon);
                string portStr = text.Substring(colon + 1);
                if (!int.TryParse(portStr, out port) || port < 1 || port > 65535) return false;
            }

            if (!IPAddress.TryParse(host, out var addr)) return false;
            if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; // IPv4 only

            handle = Encode(new IPEndPoint(addr, port));
            return handle != 0;
        }

        /// <summary>
        /// This machine's local IPv4 addresses, for a host to read out to friends. Loopback
        /// (127.x) is excluded; a Tailscale address (100.64.0.0/10) is flagged with " (Tailscale)"
        /// so remote friends know which one to type. Empty if none found.
        /// </summary>
        public static List<string> LocalIPv4s()
        {
            var list = new List<string>();
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip)) continue;
                    string s = ip.ToString();
                    byte[] b = ip.GetAddressBytes();
                    // 100.64.0.0/10 (CGNAT range Tailscale uses) -> hint for remote play.
                    if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) s += " (Tailscale)";
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }
    }
}
