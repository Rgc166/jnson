using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace VeyonSync.Student;

public record Collected(string? Ip, string? Mac, string? User);
public static class Collector
{
    [DllImport("kernel32")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Query(IntPtr server, uint session, int type, out IntPtr buffer, out uint length);
    [DllImport("wtsapi32")] private static extern void WTSFreeMemory(IntPtr buffer);
    private static string? Info(uint session, int type)
    {
        if (!Query(IntPtr.Zero, session, type, out var ptr, out _)) return null;
        try { return Marshal.PtrToStringUni(ptr); } finally { WTSFreeMemory(ptr); }
    }
    public static Collected Collect(string teacherUrl)
    {
        string? ip = null, mac = null;
        try
        {
            var uri = new Uri(teacherUrl);
            foreach (var remote in Dns.GetHostAddresses(uri.DnsSafeHost).OrderBy(x => x.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
            {
                try
                {
                    using var socket = new Socket(remote.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                    socket.Connect(remote, uri.Port); var address = ((IPEndPoint)socket.LocalEndPoint!).Address;
                    var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
                    ip = address.ToString(); var bytes = nic?.GetPhysicalAddress().GetAddressBytes();
                    if (bytes?.Length == 6) mac = string.Join("-", bytes.Select(b => b.ToString("X2")));
                    break;
                }
                catch (SocketException) { }
            }
        }
        catch (Exception e) when (e is SocketException or UriFormatException) { }
        var session = WTSGetActiveConsoleSessionId(); string? user = null;
        if (session != uint.MaxValue) { var name = Info(session, 5); var domain = Info(session, 7); if (!string.IsNullOrEmpty(name)) user = string.IsNullOrEmpty(domain) ? name : $"{domain}\\{name}"; }
        return new(ip, mac, user);
    }
}
