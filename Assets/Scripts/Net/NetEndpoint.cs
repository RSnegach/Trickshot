using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

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
        /// Parse "1.2.3.4", "1.2.3.4:7777", or a NAME - a Tailscale MagicDNS hostname ("gaming-pc",
        /// "gaming-pc.tail1234.ts.net") or a plain LAN hostname - into a Join handle. Bare input uses
        /// DefaultPort. Returns false on empty/garbage input, on anything that resolves to no IPv4, or
        /// if the result would encode to 0 (0 is the reserved "invalid" handle).
        ///
        /// Names matter because a 100.x address is the one thing about Tailscale a player has to look
        /// up, while MagicDNS gives every machine on the tailnet a name its owner already knows. It
        /// also sidesteps the 48-bit handle's IPv4-only limit from the player's side: they type a name,
        /// and we resolve whichever IPv4 it has.
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

            if (!IPAddress.TryParse(host, out var addr))
            {
                addr = ResolveHost(host);            // not a literal: try it as a name
                if (addr == null) return false;
            }
            if (addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false; // IPv4 only

            handle = Encode(new IPEndPoint(addr, port));
            return handle != 0;
        }

        /// <summary>
        /// Resolve a hostname to one IPv4, preferring a tailnet address.
        ///
        /// Bounded on purpose. This runs on the main thread from a button press, and a name that does
        /// not exist is the NORMAL case here (a typo, or Tailscale being down): the OS resolver then
        /// queries each configured server in turn and can block for several seconds, which reads as
        /// the game hanging. Wait a short while for the answer and give up otherwise - a join is
        /// retryable, a frozen window is not. The lookup itself is left to finish in the background
        /// rather than aborted, since there is no way to cancel it.
        ///
        /// A tailnet result wins when there are several, because MagicDNS and the LAN can both answer
        /// for the same machine and the 100.x address is the one that works from anywhere.
        /// </summary>
        static IPAddress ResolveHost(string host)
        {
            // Reject what cannot be a hostname before paying for a lookup. A label is letters, digits
            // and hyphens; dots separate labels. Anything else (spaces, slashes, an invite code's
            // leftovers) is a typo, not a name.
            if (string.IsNullOrEmpty(host) || host.Length > 253) return null;
            foreach (char c in host)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == '_';
                if (!ok) return null;
            }

            try
            {
                var task = Dns.GetHostAddressesAsync(host);
                if (!task.Wait(HostLookupMs)) return null;      // resolver too slow / name does not exist
                IPAddress best = null;
                foreach (var ip in task.Result)
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IsTailscale(ip)) return ip;             // tailnet: always the right answer
                    if (best == null) best = ip;
                }
                return best;
            }
            catch { return null; }   // NXDOMAIN, no resolver, or the task faulted
        }

        const int HostLookupMs = 2000;

        /// <summary>True for 100.64.0.0/10, the CGNAT range Tailscale hands out.</summary>
        public static bool IsTailscale(IPAddress ip)
        {
            if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = ip.GetAddressBytes();
            return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
        }

        // Link-local autoconfig (169.254.x) is never reachable by a friend; hide it from the host's
        // read-out so the useful addresses aren't buried among six APIPA entries.
        static bool IsLinkLocal(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 169 && b[1] == 254;
        }

        /// <summary>
        /// Every usable local IPv4, TAILSCALE FIRST. Walks the network interfaces directly rather
        /// than resolving our own hostname: Dns.GetHostAddresses(Dns.GetHostName()) goes through the
        /// DNS resolver and on Windows routinely OMITS the Tailscale adapter, so a host would read
        /// out only its LAN 192.168.x address and a remote friend typing it could never connect.
        /// Loopback and link-local (169.254.x) are dropped as unreachable.
        /// </summary>
        public static List<IPAddress> LocalAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ip) || IsLinkLocal(ip)) continue;
                        list.Add(ip);
                    }
                }
            }
            catch { }
            // Tailscale addresses are the ones that work for a remote friend, so surface them first.
            list.Sort((a, b) => IsTailscale(b).CompareTo(IsTailscale(a)));
            return list;
        }

        /// <summary>
        /// The address a friend should actually be given: the Tailscale one if this machine has it,
        /// else the first usable LAN address, else null.
        /// </summary>
        public static IPAddress BestHostAddress()
        {
            var all = LocalAddresses();
            return all.Count > 0 ? all[0] : null;   // LocalAddresses already sorts Tailscale first
        }

        /// <summary>
        /// This machine's local IPv4 addresses as display strings, for a host to read out to
        /// friends. Tailscale addresses come first and are tagged so it's obvious which one a
        /// remote friend needs. Empty if none found.
        /// </summary>
        public static List<string> LocalIPv4s()
        {
            var list = new List<string>();
            foreach (var ip in LocalAddresses())
                list.Add(ip + (IsTailscale(ip) ? " (Tailscale)" : " (LAN)"));
            return list;
        }

        // ---- invite codes ----
        // A join handle is 48 bits. Crockford base32 packs that into 10 characters a friend can read
        // off a screen or paste from a chat - no dotted quads, no port to explain. Crockford's
        // alphabet omits I, L, O and U precisely so the remaining symbols can't be confused, and
        // Normalize folds the omitted letters back onto their twins (I/L->1, O->0, U->V), so a friend
        // who reads "O" for zero still lands on the right code.
        const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";   // 32 symbols = 5 bits
        const int CodeLength = 10;                                        // 10 * 5 = 50 bits >= 48

        /// <summary>
        /// Encode a join handle as a short shareable invite code, e.g. "K7M2-QP9X-4B".
        /// Dashes are cosmetic; TryParseInvite ignores them.
        /// </summary>
        public static string ToInvite(ulong handle)
        {
            if (handle == 0) return "";
            var raw = new char[CodeLength];
            for (int i = CodeLength - 1; i >= 0; i--) { raw[i] = CodeAlphabet[(int)(handle & 31UL)]; handle >>= 5; }
            var sb = new StringBuilder(CodeLength + 2);
            for (int i = 0; i < CodeLength; i++)
            {
                if (i == 4 || i == 8) sb.Append('-');
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Decode an invite code back into a join handle. Case-insensitive; dashes, spaces and the
        /// ambiguous lookalikes are normalised. False if it isn't a valid code.
        /// </summary>
        public static bool TryParseInvite(string code, out ulong handle)
        {
            handle = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;

            ulong v = 0;
            int digits = 0;
            foreach (char raw in code)
            {
                if (raw == '-' || raw == ' ' || raw == '_') continue;
                int d = CodeAlphabet.IndexOf(Normalize(char.ToUpperInvariant(raw)));
                if (d < 0) return false;                        // junk character
                if (++digits > CodeLength) return false;         // too long to be a code
                v = (v << 5) | (uint)d;                         // via uint: no sign extension
            }
            if (digits != CodeLength) return false;
            if (v > 0xFFFFFFFFFFFFUL) return false;             // outside the 48-bit handle space
            handle = v;
            return handle != 0;
        }

        // Crockford's decode aliases: the four letters left out of the alphabet map onto the symbols
        // they look like, so a misread character still decodes correctly.
        static char Normalize(char c)
        {
            switch (c)
            {
                case 'I': case 'L': return '1';
                case 'O': return '0';
                case 'U': return 'V';
                default: return c;
            }
        }

        /// <summary>
        /// Accept ANY form a friend might be given: a short invite code, a raw "ip" / "ip:port", or a
        /// MagicDNS / LAN hostname. This is what the join box should call.
        ///
        /// Invite is tried FIRST, and that order matters: IPAddress.TryParse also accepts a bare
        /// integer as a packed address, so the all-digit invite code "1234567890" would otherwise
        /// parse as the IP 73.150.2.210 and quietly dial a machine that has nothing to do with the
        /// lobby. A valid invite is exactly 10 code characters, which no dotted-quad ever is, so
        /// preferring it costs nothing.
        ///
        /// Hostnames do not reopen that ambiguity, for a reason worth writing down rather than
        /// rediscovering: 10 base32 digits carry 50 bits and a handle is 48, so TryParseInvite's range
        /// check rejects any code whose FIRST character is not one of 0-3. Every hostname beginning
        /// with a letter therefore falls straight through to the resolver, and it also means the invite
        /// attempt never costs a DNS lookup.
        /// </summary>
        public static bool TryParseAny(string text, out ulong handle)
            => TryParseInvite(text, out handle) || TryParse(text, out handle);

        /// <summary>The invite code for THIS machine as a host, or "" if it has no usable address.</summary>
        public static string LocalInvite()
        {
            var ip = BestHostAddress();
            return ip == null ? "" : ToInvite(Encode(new IPEndPoint(ip, DefaultPort)));
        }
    }
}
