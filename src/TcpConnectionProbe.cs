using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Aloha
{

    public class TcpConnectionProbe
    {
        public class Conn
        {
            public int Pid;
            public string LocalIp;
            public int LocalPort;
            public string RemoteIp;
            public int RemotePort;
            public string State;
            public string Transport;
            public string Proto;
            public string Adapter;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
            int ipVersion, int tableClass, int reserved);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedUdpTable(
            IntPtr pUdpTable, ref int dwOutBufLen, bool sort,
            int ipVersion, int tableClass, int reserved);

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int UDP_TABLE_OWNER_PID = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_UDPROW_OWNER_PID
        {
            public uint localAddr;
            public uint localPort;
            public uint owningPid;
        }

        private static readonly string[] StateNames =
        {
            "?", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED",
            "FIN_WAIT1", "FIN_WAIT2", "CLOSE_WAIT", "CLOSING", "LAST_ACK",
            "TIME_WAIT", "DELETE_TCB"
        };

        private static int Port(uint p) { return ((int)(p & 0xFF) << 8) | (int)((p >> 8) & 0xFF); }

        private static string ProtoForPort(int port)
        {
            switch (port)
            {
                case 443:  return "HTTPS";
                case 80:   return "HTTP";
                case 53:   return "DNS";
                case 853:  return "DoT";
                case 8080: return "HTTP-alt";
                case 8443: return "HTTPS-alt";
                case 22:   return "SSH";
                case 21:   return "FTP";
                case 25: case 587: case 465: return "SMTP";
                case 993:  return "IMAPS";
                case 995:  return "POP3S";
                case 123:  return "NTP";
                case 1900: return "SSDP";
                case 5353: return "mDNS";
                case 3478: return "STUN";
                case 9001: case 9030: return "Tor";
                default:   return port >= 49152 ? "ephemeral" : ("port " + port);
            }
        }

        public List<Conn> ForPids(HashSet<int> pids)
        {
            var outp = new List<Conn>();
            var localIpToAdapter = BuildAdapterMap();

            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size > 0)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) == 0)
                    {
                        int count = Marshal.ReadInt32(buf);
                        IntPtr row = (IntPtr)((long)buf + 4);
                        int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                        for (int i = 0; i < count; i++)
                        {
                            var r = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(row, typeof(MIB_TCPROW_OWNER_PID));
                            row = (IntPtr)((long)row + rowSize);
                            int pid = (int)r.owningPid;
                            if (pids != null && pids.Count > 0 && !pids.Contains(pid)) continue;

                            int rport = Port(r.remotePort);
                            string rip = new IPAddress(r.remoteAddr).ToString();
                            if (rip == "0.0.0.0" || rport == 0) continue;     

                            string lip = new IPAddress(r.localAddr).ToString();
                            uint st = r.state;
                            string state = (st < StateNames.Length) ? StateNames[st] : "?";
                            outp.Add(new Conn {
                                Pid = pid, LocalIp = lip, LocalPort = Port(r.localPort),
                                RemoteIp = rip, RemotePort = rport, State = state,
                                Transport = "TCP", Proto = ProtoForPort(rport),
                                Adapter = AdapterFor(localIpToAdapter, lip)
                            });
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }

            int usize = 0;
            GetExtendedUdpTable(IntPtr.Zero, ref usize, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (usize > 0)
            {
                IntPtr ubuf = Marshal.AllocHGlobal(usize);
                try
                {
                    if (GetExtendedUdpTable(ubuf, ref usize, false, AF_INET, UDP_TABLE_OWNER_PID, 0) == 0)
                    {
                        int count = Marshal.ReadInt32(ubuf);
                        IntPtr row = (IntPtr)((long)ubuf + 4);
                        int rowSize = Marshal.SizeOf(typeof(MIB_UDPROW_OWNER_PID));
                        for (int i = 0; i < count; i++)
                        {
                            var r = (MIB_UDPROW_OWNER_PID)Marshal.PtrToStructure(row, typeof(MIB_UDPROW_OWNER_PID));
                            row = (IntPtr)((long)row + rowSize);
                            int pid = (int)r.owningPid;
                            if (pids != null && pids.Count > 0 && !pids.Contains(pid)) continue;

                            string lip = new IPAddress(r.localAddr).ToString();
                            int lport = Port(r.localPort);
                            if (lip == "0.0.0.0" && lport == 0) continue;
                            outp.Add(new Conn {
                                Pid = pid, LocalIp = lip, LocalPort = lport,
                                RemoteIp = "*", RemotePort = 0, State = "",
                                Transport = "UDP", Proto = ProtoForPort(lport),
                                Adapter = AdapterFor(localIpToAdapter, lip)
                            });
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(ubuf); }
            }

            return outp;
        }

        private static Dictionary<string, string> BuildAdapterMap()
        {
            var map = new Dictionary<string, string>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    string nice = FriendlyAdapter(ni);
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            map[ua.Address.ToString()] = nice;
                    }
                }
            }
            catch { }
            return map;
        }

        private static string FriendlyAdapter(System.Net.NetworkInformation.NetworkInterface ni)
        {
            var t = ni.NetworkInterfaceType;
            string kind =
                t == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? "Wi-Fi" :
                t == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ? "Ethernet" :
                t == System.Net.NetworkInformation.NetworkInterfaceType.Loopback ? "Loopback" :
                t == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel ? "Tunnel/VPN" :
                t == System.Net.NetworkInformation.NetworkInterfaceType.Ppp ? "VPN/PPP" : t.ToString();

            string n = ni.Name ?? "";
            string desc = ni.Description ?? "";
            if (desc.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("wireguard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0)
                kind = "VPN";
            return kind + ": " + n;
        }

        private static string AdapterFor(Dictionary<string, string> map, string localIp)
        {
            if (localIp != null && map.TryGetValue(localIp, out var nm)) return nm;
            if (localIp == "127.0.0.1") return "Loopback";
            return "?";
        }

        private readonly Dictionary<string, string> dnsCache = new Dictionary<string, string>();
        private readonly HashSet<string> dnsInFlight = new HashSet<string>();

        public string HostFor(string ip)
        {
            lock (dnsCache)
            {
                if (dnsCache.TryGetValue(ip, out var h)) return h;
                if (dnsInFlight.Contains(ip)) return null;
                dnsInFlight.Add(ip);
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string host = null;
                try { host = Dns.GetHostEntry(ip).HostName; } catch { host = null; }
                lock (dnsCache)
                {
                    dnsCache[ip] = host;
                    dnsInFlight.Remove(ip);
                }
            });
            return null;
        }

        public string ToJson(List<Conn> conns)
        {
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"conns\",\"list\":[");
            for (int i = 0; i < conns.Count; i++)
            {
                var c = conns[i];
                if (i > 0) sb.Append(',');
                string host = (c.RemoteIp == "*") ? null : HostFor(c.RemoteIp);
                sb.Append("{\"pid\":").Append(c.Pid)
                  .Append(",\"ip\":\"").Append(c.RemoteIp).Append('"')
                  .Append(",\"port\":").Append(c.RemotePort)
                  .Append(",\"lport\":").Append(c.LocalPort)
                  .Append(",\"host\":").Append(host == null ? "null" : ("\"" + Esc(host) + "\""))
                  .Append(",\"state\":\"").Append(c.State).Append('"')
                  .Append(",\"tp\":\"").Append(c.Transport).Append('"')
                  .Append(",\"proto\":\"").Append(Esc(c.Proto)).Append('"')
                  .Append(",\"adapter\":\"").Append(Esc(c.Adapter)).Append('"')
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
