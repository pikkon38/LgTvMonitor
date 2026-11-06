using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LgTvMonitor;

public static partial class Network
{
    // ---------------- Wake-on-LAN ----------------

    public static byte[] ParseMac(string mac)
    {
        var hex = Regex.Replace(mac ?? "", "[^0-9A-Fa-f]", "");
        if (hex.Length != 12) throw new FormatException($"'{mac}' is not a valid MAC address.");
        return Convert.FromHexString(hex);
    }

    public static string FormatMac(byte[] mac) => string.Join(":", mac.Select(b => b.ToString("X2")));

    /// <summary>
    /// Sends the WoL magic packet to the global broadcast address and to the directed broadcast
    /// of every active IPv4 interface (so it goes out the right NIC on multi-adapter PCs), on ports 9 and 7.
    /// </summary>
    public static void SendMagicPacket(string mac, string? tvIp = null)
    {
        var macBytes = ParseMac(mac);
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);

        var targets = new List<(IPAddress local, IPAddress dest)>();
        foreach (var (addr, mask) in LocalIPv4())
        {
            var a = addr.GetAddressBytes();
            var m = mask.GetAddressBytes();
            var bcast = new byte[4];
            for (int i = 0; i < 4; i++) bcast[i] = (byte)(a[i] | ~m[i]);
            targets.Add((addr, new IPAddress(bcast)));
            targets.Add((addr, IPAddress.Broadcast));
        }
        if (targets.Count == 0) targets.Add((IPAddress.Any, IPAddress.Broadcast));

        int sent = 0;
        foreach (var (local, dest) in targets.Distinct())
        {
            foreach (var port in new[] { 9, 7 })
            {
                try
                {
                    using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                    udp.Send(packet, packet.Length, new IPEndPoint(dest, port));
                    sent++;
                }
                catch
                {
                    // interface may be going up/down; ignore
                }
            }
        }
        // Unicast to the TV too: works when the router still has the TV in its ARP cache.
        if (!string.IsNullOrWhiteSpace(tvIp) && IPAddress.TryParse(tvIp, out var tv))
        {
            try
            {
                using var udp = new UdpClient();
                udp.Send(packet, packet.Length, new IPEndPoint(tv, 9));
                sent++;
            }
            catch { }
        }
        Log.Write($"Wake-on-LAN sent to {mac} ({sent} packets)");
    }

    public static IEnumerable<(IPAddress addr, IPAddress mask)> LocalIPv4()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (ua.Address.ToString().StartsWith("169.254.")) continue;
                yield return (ua.Address, ua.IPv4Mask);
            }
        }
    }

    /// <summary>True once at least one adapter has a real IPv4 address (used at boot).</summary>
    public static bool HasUsableIPv4() => LocalIPv4().Any();

    // ---------------- MAC lookup ----------------

    [LibraryImport("iphlpapi.dll")]
    private static partial int SendARP(uint destIp, uint srcIp, [Out] byte[] macAddr, ref uint physAddrLen);

    /// <summary>Asks Windows for the MAC of a LAN IP (ARP). The TV must be on.</summary>
    public static string? LookupMac(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return null;
        var mac = new byte[6];
        uint len = 6;
        var dest = BitConverter.ToUInt32(addr.GetAddressBytes(), 0);
        return SendARP(dest, 0, mac, ref len) == 0 && len == 6 ? FormatMac(mac) : null;
    }

    // ---------------- Discovery (SSDP) ----------------

    public sealed record FoundTv(string Ip, string Name);

    /// <summary>Finds LG webOS TVs on the LAN. The TV must be on.</summary>
    public static async Task<List<FoundTv>> DiscoverAsync(int seconds = 4)
    {
        var found = new Dictionary<string, string>();
        var req = Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            "ST: urn:lge-com:service:webos-second-screen:1\r\n\r\n");
        var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        var locations = new Dictionary<string, string>();

        var sockets = new List<UdpClient>();
        foreach (var (addr, _) in LocalIPv4())
        {
            try { sockets.Add(new UdpClient(new IPEndPoint(addr, 0))); } catch { }
        }
        if (sockets.Count == 0) sockets.Add(new UdpClient(0));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var tasks = sockets.Select(async udp =>
        {
            try
            {
                for (int i = 0; i < 3; i++) await udp.SendAsync(req, req.Length, group);
                while (!cts.IsCancellationRequested)
                {
                    var r = await udp.ReceiveAsync(cts.Token);
                    var text = Encoding.ASCII.GetString(r.Buffer);
                    var ip = r.RemoteEndPoint.Address.ToString();
                    lock (found)
                    {
                        found.TryAdd(ip, "LG webOS TV");
                        var m = Regex.Match(text, @"^LOCATION:\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        if (m.Success) locations.TryAdd(ip, m.Groups[1].Value);
                    }
                }
            }
            catch { }
            finally { udp.Dispose(); }
        }).ToList();
        await Task.WhenAll(tasks);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        foreach (var (ip, loc) in locations)
        {
            try
            {
                var xml = await http.GetStringAsync(loc);
                var m = Regex.Match(xml, "<friendlyName>([^<]+)</friendlyName>");
                if (m.Success) found[ip] = WebUtility.HtmlDecode(m.Groups[1].Value);
            }
            catch { }
        }
        return found.Select(kv => new FoundTv(kv.Key, kv.Value)).ToList();
    }
}
