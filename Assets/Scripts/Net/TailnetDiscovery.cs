using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// The two UNCONNECTED wire frames that make discovery work, kept in one place so the host side
    /// (DirectIpTransport) and the browsing side (TailnetDiscovery) cannot drift apart.
    ///
    ///   [4] Probe      : [kind][magic u32]
    ///   [5] ProbeReply : [kind][magic u32][players u8][max u8][len u8][mode][len u8][name]
    ///
    /// These are deliberately NOT part of the reliable/unreliable framing: they are exchanged
    /// between machines that have no peer relationship, which is the whole point of discovery.
    /// The magic exists because port 7777 is not ours by right - anything else on the network may
    /// send us a stray datagram, and a 4-byte constant is enough to not mistake it for a probe.
    /// Worst case for a reply is 1+4+1+1+1+63+1+63 = 135 bytes, so it fits Tailscale's 1280 MTU
    /// with room to spare.
    /// </summary>
    public static class LobbyProbe
    {
        public const byte FrameProbe = 4, FrameProbeReply = 5;

        const uint Magic = 0x314B5354u;   // 'TSK1'
        const int MaxStr = 63;            // per string, in BYTES (keeps the reply small + bounded)

        public static byte[] BuildProbe()
        {
            var b = new byte[5];
            b[0] = FrameProbe;
            W32(b, 1, Magic);
            return b;
        }

        public static bool IsProbe(byte[] d)
            => d != null && d.Length >= 5 && d[0] == FrameProbe && R32(d, 1) == Magic;

        public static byte[] BuildReply(LobbyAdvert ad)
        {
            byte[] mode = Clip(ad.mode), name = Clip(ad.name);
            var b = new byte[5 + 2 + 1 + mode.Length + 1 + name.Length];
            int o = 0;
            b[o++] = FrameProbeReply;
            W32(b, o, Magic); o += 4;
            b[o++] = (byte)Mathf.Clamp(ad.players, 0, 255);
            b[o++] = (byte)Mathf.Clamp(ad.maxPlayers, 0, 255);
            b[o++] = (byte)mode.Length; Buffer.BlockCopy(mode, 0, b, o, mode.Length); o += mode.Length;
            b[o++] = (byte)name.Length; Buffer.BlockCopy(name, 0, b, o, name.Length);
            return b;
        }

        public static bool TryReadReply(byte[] d, out LobbyAdvert ad)
        {
            ad = default;
            // 9 = header + both counts + two zero-length strings.
            if (d == null || d.Length < 9 || d[0] != FrameProbeReply || R32(d, 1) != Magic) return false;
            int o = 5;
            ad.players = d[o++];
            ad.maxPlayers = d[o++];
            if (!ReadStr(d, ref o, out ad.mode)) return false;
            if (!ReadStr(d, ref o, out ad.name)) return false;
            ad.visible = true;
            return true;
        }

        static bool ReadStr(byte[] d, ref int o, out string s)
        {
            s = "";
            if (o >= d.Length) return false;
            int n = d[o++];
            if (o + n > d.Length) return false;
            s = Encoding.UTF8.GetString(d, o, n);
            o += n;
            return true;
        }

        // Truncate to MaxStr BYTES on a character boundary. Cutting mid-character would emit an
        // invalid UTF-8 sequence and the far end would render a replacement glyph in a player name.
        static byte[] Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return new byte[0];
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length <= MaxStr) return bytes;
            int chars = s.Length;
            while (chars > 0 && Encoding.UTF8.GetByteCount(s.Substring(0, chars)) > MaxStr) chars--;
            return Encoding.UTF8.GetBytes(s.Substring(0, chars));
        }

        static void W32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF); b[o + 3] = (byte)((v >> 24) & 0xFF);
        }
        static uint R32(byte[] b, int o)
            => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    }

    /// <summary>
    /// Finds joinable hosts without a matchmaker, a tracker or a credential.
    ///
    /// Tailscale is an L3 TUN, so it carries no broadcast, no multicast and no mDNS: the usual
    /// "shout on the LAN and see who answers" discovery cannot work over a tailnet at all, and the
    /// upstream feature requests for it are still open. So we do what the one repo that solves this
    /// properly does (dlroqa/LanChat): ask the LOCAL Tailscale client who our peers are, then probe
    /// each of them directly. Discovery becomes an enumerate-then-probe sweep over a list we
    /// already have on this machine, with no server involved.
    ///
    /// Two paths, and they do not overlap because neither reaches the other's peers:
    ///   - tailnet : `tailscale status` gives every peer's 100.x address; probe each by UNICAST.
    ///   - plain LAN : UDP broadcast, which is the only thing that works with no Tailscale at all.
    ///
    /// Everything expensive (spawning the CLI, blocking socket reads) happens on a worker thread.
    /// Results are handed back on the main thread through Poll(), because the callback ends up
    /// touching Unity UI state.
    ///
    /// Degrading is a first-class case, not an error path: no Tailscale installed, daemon stopped,
    /// logged out, or simply no peers hosting all end in the same place, an empty list and a
    /// specific reason the browser can show. The invite code always works regardless.
    /// </summary>
    public static class TailnetDiscovery
    {
        // Re-running the CLI is a process spawn, so the peer list is cached. The browser refreshes
        // far more often than a tailnet changes shape.
        public const float PeerCacheSeconds = 15f;
        const float ReplyWindowSeconds = 0.9f;   // how long to listen after firing the probes
        const int SocketPollMs = 100;            // Receive timeout granularity inside that window
        const float AbandonSweepSeconds = 6f;    // in-flight sweep older than this is presumed dead
        const int CliTimeoutMs = 3000;

        /// <summary>Why the last sweep found nothing, for an honest message in the browser.</summary>
        public enum Reason { Ok, NoCli, TailnetDown, NoPeers }

        public static Reason LastReason { get; private set; } = Reason.Ok;
        public static bool HasTailnet { get; private set; }
        public static bool Scanning => _busy;
        /// <summary>Tailnet peers seen by the last CLI query (not hosts - just machines).</summary>
        public static int PeerCount { get; private set; }

        static readonly ConcurrentQueue<List<LobbyInfo>> _results = new ConcurrentQueue<List<LobbyInfo>>();
        static readonly ConcurrentQueue<string> _log = new ConcurrentQueue<string>();
        // Worker writes, main thread reads. volatile so the main thread cannot cache a stale value.
        static volatile Reason _workerReason = Reason.Ok;
        static volatile int _workerPeerCount;
        static volatile bool _busy;

        static Action<List<LobbyInfo>> _pending;   // main thread only
        static float _sweepStarted = -9999f;       // main thread only

        // Worker-thread-only state. No lock needed: _busy admits exactly one worker at a time, so
        // nothing else ever touches these while a sweep is running.
        static List<IPAddress> _peers = new List<IPAddress>();
        static float _peersAt = -9999f;

        /// <summary>
        /// Kick off a sweep. Cheap to call repeatedly: while one is in flight the call is ignored,
        /// and the peer list is only re-queried every PeerCacheSeconds. onResults fires later, on
        /// the main thread, from Poll().
        /// </summary>
        public static void Sweep(Action<List<LobbyInfo>> onResults)
        {
            if (onResults == null) return;

            float now = Time.unscaledTime;      // MAIN thread: UnityEngine.Time is not thread safe
            if (_busy)
            {
                // Normally we just let the in-flight sweep finish. But if whoever asked for it went
                // away without draining (the browser being destroyed mid-scan), _busy would latch
                // and discovery would be dead for the rest of the process. Time it out instead.
                if (now - _sweepStarted < AbandonSweepSeconds) return;
                _busy = false;
            }

            // Drop any result nobody collected, so a stale list can't be delivered to a new caller.
            while (_results.TryDequeue(out _)) { }

            _busy = true;
            _pending = onResults;
            _sweepStarted = now;
            var t = new Thread(() => Work(now)) { IsBackground = true, Name = "TrickshotDiscovery" };
            t.Start();
        }

        /// <summary>
        /// Main-thread pump: delivers finished sweeps and refreshes the local network read-out.
        /// SessionBrowserUI calls this every frame while it is up.
        /// </summary>
        public static void Poll()
        {
            RefreshLocalState();
            while (_log.TryDequeue(out var msg)) Debug.LogWarning("TailnetDiscovery: " + msg);

            while (_results.TryDequeue(out var list))
            {
                var cb = _pending;
                _pending = null;
                _busy = false;                       // clear BEFORE the callback so it may re-sweep
                LastReason = _workerReason;
                PeerCount = _workerPeerCount;
                cb?.Invoke(list);
            }
        }

        static float _localAt = -9999f;

        // Enumerating NICs is not free and the browser draws every frame, so throttle it. This is
        // what distinguishes "no Tailscale on this machine" from "Tailscale up, nobody hosting".
        static void RefreshLocalState()
        {
            float now = Time.unscaledTime;
            if (now - _localAt < 5f) return;
            _localAt = now;
            bool has = false;
            foreach (var ip in NetEndpoint.LocalAddresses())
                if (NetEndpoint.IsTailscale(ip)) { has = true; break; }
            HasTailnet = has;
        }

        // ---- worker ----
        static void Work(float now)
        {
            var found = new List<LobbyInfo>();
            try
            {
                if (now - _peersAt > PeerCacheSeconds)
                {
                    _peers = QueryPeers();
                    _peersAt = now;
                    _workerPeerCount = _peers.Count;
                }
                Probe(_peers, found);
                if (found.Count > 0) _workerReason = Reason.Ok;
                else if (_workerReason == Reason.Ok && _peers.Count == 0) _workerReason = Reason.NoPeers;
            }
            catch (Exception e) { _log.Enqueue(e.Message); }
            finally
            {
                // ALWAYS enqueue, even on a throw. This is the only thing that clears _busy, so
                // skipping it would wedge discovery permanently.
                _results.Enqueue(found);
            }
        }

        static readonly string[] CliCandidates =
        {
            @"C:\Program Files\Tailscale\tailscale.exe",
            @"C:\Program Files (x86)\Tailscale\tailscale.exe",
            "/usr/bin/tailscale",
            "/usr/local/bin/tailscale",
            "/usr/sbin/tailscale",                                     // some distro packages
            "/opt/homebrew/bin/tailscale",                             // Apple Silicon Homebrew
            "/snap/bin/tailscale",                                     // Ubuntu snap
            "/Applications/Tailscale.app/Contents/MacOS/Tailscale",     // Mac App Store build
            "/Applications/Tailscale.app/Contents/MacOS/tailscale",     // same bundle, CLI casing
            "tailscale",     // whatever is on PATH, for anything the list above misses
        };

        /// <summary>
        /// Every online tailnet peer's IPv4, from `tailscale status`. The plain text form is parsed
        /// rather than --json on purpose: the JSON keys peers by node key, which is a dynamic map,
        /// and Unity's JsonUtility cannot express that without a real JSON library. The first
        /// whitespace token of a status line is the peer's 100.x address, which is all we need, and
        /// requiring it to parse as an in-range address also throws away headers and health notes.
        /// </summary>
        static List<IPAddress> QueryPeers()
        {
            var list = new List<IPAddress>();
            string outp = RunCli();
            if (outp == null) { _workerReason = Reason.NoCli; return list; }
            if (outp.Length == 0) { _workerReason = Reason.TailnetDown; return list; }
            _workerReason = Reason.Ok;

            // Our own address is in the listing; probing ourselves would list our own lobby.
            var mine = new HashSet<string>();
            try { foreach (var ip in NetEndpoint.LocalAddresses()) mine.Add(ip.ToString()); } catch { }

            foreach (var rawLine in outp.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int sp = line.IndexOf(' ');
                string first = sp < 0 ? line : line.Substring(0, sp);
                if (!IPAddress.TryParse(first, out var addr)) continue;
                if (!NetEndpoint.IsTailscale(addr)) continue;
                if (line.IndexOf("offline", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (mine.Contains(addr.ToString())) continue;
                list.Add(addr);
            }
            return list;
        }

        /// <summary>
        /// Run the CLI. Returns its stdout, "" if the CLI exists but has no tailnet to report
        /// (daemon stopped, logged out), or null if there is no Tailscale on this machine at all.
        /// </summary>
        static string RunCli()
        {
            foreach (var path in CliCandidates)
            {
                bool bare = path.IndexOf('/') < 0 && path.IndexOf('\\') < 0;
                if (!bare && !System.IO.File.Exists(path)) continue;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(path, "status")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        if (p == null) continue;
                        // Drain stdout FIRST (it is the big one), then stderr, then wait. Waiting
                        // before draining can deadlock on a full pipe buffer.
                        string so = p.StandardOutput.ReadToEnd();
                        try { p.StandardError.ReadToEnd(); } catch { }
                        if (!p.WaitForExit(CliTimeoutMs)) { try { p.Kill(); } catch { } continue; }
                        // Non-zero means the daemon is stopped or we are logged out. The CLI is
                        // here, so this is a different problem from not having Tailscale.
                        return p.ExitCode == 0 ? so : "";
                    }
                }
                catch { }   // not this candidate (missing, not executable, blocked); try the next
            }
            return null;
        }

        static void Probe(List<IPAddress> peers, List<LobbyInfo> found)
        {
            var targets = new List<IPEndPoint>();
            foreach (var ip in peers) targets.Add(new IPEndPoint(ip, NetEndpoint.DefaultPort));
            targets.AddRange(BroadcastTargets());
            if (targets.Count == 0) return;

            UdpClient udp = null;
            try
            {
                udp = new UdpClient(0);                       // ephemeral: never collides with 7777
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = SocketPollMs;

                byte[] probe = LobbyProbe.BuildProbe();
                foreach (var t in targets)
                    try { udp.Send(probe, probe.Length, t); } catch { }   // one bad NIC ends nothing

                var seen = new HashSet<ulong>();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var from = new IPEndPoint(IPAddress.Any, 0);
                while (sw.Elapsed.TotalSeconds < ReplyWindowSeconds)
                {
                    byte[] data;
                    try { data = udp.Receive(ref from); }
                    catch (SocketException) { continue; }     // ReceiveTimeout; keep listening
                    catch { break; }                          // socket closed or broken: done

                    if (from.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (!LobbyProbe.TryReadReply(data, out var ad)) continue;

                    // The endpoint that answered is by definition the endpoint that works, so use
                    // it verbatim as the join handle rather than assuming the default port.
                    ulong handle;
                    try { handle = NetEndpoint.Encode(new IPEndPoint(from.Address, from.Port)); }
                    catch { continue; }
                    if (handle == 0 || !seen.Add(handle)) continue;   // dedupe: unicast + broadcast

                    found.Add(new LobbyInfo
                    {
                        handle = handle,
                        name = string.IsNullOrEmpty(ad.name) ? from.Address.ToString() : ad.name,
                        mode = ad.mode ?? "",
                        players = ad.players,
                        maxPlayers = ad.maxPlayers,
                    });
                }
            }
            finally { try { udp?.Close(); } catch { } }
        }

        /// <summary>
        /// Where to send the plain-LAN broadcast. 255.255.255.255 alone is not enough on Windows:
        /// with more than one adapter up (Ethernet plus Wi-Fi, plus any virtual switch) the stack
        /// sends it out exactly one of them, so a host on the other adapter never hears it. Each
        /// interface's DIRECTED broadcast is added as well.
        ///
        /// The Tailscale adapter is skipped deliberately. It is a /32 whose directed broadcast is
        /// just its own address, and the tunnel carries no broadcast traffic in the first place -
        /// tailnet peers are reached by unicast from the status list instead.
        /// </summary>
        static List<IPEndPoint> BroadcastTargets()
        {
            var list = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, NetEndpoint.DefaultPort) };
            var seen = new HashSet<string>();
            // The try/catch is PER INTERFACE and PER ADDRESS, not one around the whole walk. VPN
            // tunnels and virtual switches do throw while being queried, on all three platforms, and
            // a single catch outside the loop abandoned every remaining adapter the moment one did.
            // That bites hardest on macOS and some Linux setups, where the kernel refuses a send to
            // 255.255.255.255 outright: there the per-subnet addresses computed below are the ONLY
            // broadcast targets that work, so losing them means finding nobody on the LAN at all.
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return list; }
            foreach (var ni in nics)
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        try
                        {
                            var ip = ua.Address;
                            if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                            if (NetEndpoint.IsTailscale(ip)) continue;
                            byte[] m = MaskBytes(ua);
                            byte[] a = ip.GetAddressBytes();
                            if (m == null || a.Length != 4) continue;
                            var b = new byte[4];
                            for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | (byte)~m[i]);
                            var bc = new IPAddress(b);
                            // Several adapters on one subnet would otherwise be probed repeatedly.
                            if (seen.Add(bc.ToString())) list.Add(new IPEndPoint(bc, NetEndpoint.DefaultPort));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>
        /// The 4-byte IPv4 netmask for one unicast address, or null if nothing usable can be found.
        ///
        /// IPv4Mask is the obvious source and the one that works on Windows. It is NOT reliably
        /// populated on macOS or on some Linux builds, where it comes back as 0.0.0.0 or throws -
        /// which made BroadcastTargets skip every address on those platforms and leave the sweep
        /// with no LAN targets at all. PrefixLength IS populated there, so it is the fallback, and a
        /// /24 is the last resort: a home or office LAN is one far more often than not, and a
        /// plausible-but-wrong broadcast address still reaches the subnet it names, whereas
        /// skipping the address reaches nothing.
        /// </summary>
        static byte[] MaskBytes(UnicastIPAddressInformation ua)
        {
            try
            {
                var mask = ua.IPv4Mask;
                if (mask != null)
                {
                    byte[] m = mask.GetAddressBytes();
                    // An all-zero mask is the "not populated" answer, not a real /0.
                    if (m.Length == 4 && (m[0] | m[1] | m[2] | m[3]) != 0) return m;
                }
            }
            catch { }
            int bits = 24;
            try { int p = ua.PrefixLength; if (p >= 8 && p <= 32) bits = p; } catch { }
            var built = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                int take = Mathf.Clamp(bits - i * 8, 0, 8);
                built[i] = (byte)(take == 0 ? 0 : (0xFF << (8 - take)) & 0xFF);
            }
            return built;
        }
    }
}
