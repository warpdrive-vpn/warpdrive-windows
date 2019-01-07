using System;
using System.Net;
using System.Diagnostics;

using NLog;
using System.Threading.Tasks;

namespace Warpdrive
{
    public class NetworkTools
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public static void ConfigureRoutes(string connect_ep)
        {
            Log.Info("Building routes...");
            Log.Info("Adding exception route for the server...");

            Uri uri = new Uri(connect_ep);
            IPAddress addr;

            if (!IPAddress.TryParse(uri.Host, out addr))
            {
                Log.Info("Resolving {0}...", uri.Host);
                addr = Dns.GetHostEntry(uri.Host).AddressList[0];
                Log.Info("{0} is {1}", uri.Host, addr);
            }

            string gateway = GetDefaultGateway(addr.ToString());

            Process.Start("route", string.Format("add -host {0} gw {1}", addr, gateway));

            Log.Info("Adding gateway part 1/2 (0.0.0.0/1)...");
            Process.Start("route", "add -net 0.0.0.0/1 tun1");

            Log.Info("Adding gateway part 2/2 (128.0.0.0/1)...");
            Process.Start("route", "add -net 128.0.0.0/1 tun1");

            Log.Info("Finished configuring routes.");
        }

        public static void ConfigureRoutesWindows(string connect_ep, string gateway)
        {
            Utilities.StartThread(delegate
            {
                Log.Info("Building routes...");
                Log.Info("Adding exception route for the server...");

                Uri uri = new Uri(connect_ep);
                IPAddress addr;

                string id = "";

                Utilities.StartThread(delegate
                {
                    id = GetTunDeviceId();
                });

                if (!IPAddress.TryParse(uri.Host, out addr))
                {
                    Log.Info("Resolving {0}...", uri.Host);
                    addr = Dns.GetHostEntry(uri.Host).AddressList[0];
                    Log.Info("{0} is {1}", uri.Host, addr);
                }

                Log.Info("Tun device ID is {0}", id);
                Log.Info("Connection gateway is {0}", gateway);

                IPAddress temp_addr;
                int temp_i;

                if (!IPAddress.TryParse(gateway, out temp_addr))
                {
                    Log.Warn("Invalid IP address \"{0}\" for gateway, aborting route autoconfiguration.", gateway);
                    return;
                }

                if (!int.TryParse(id, out temp_i))
                {
                    Log.Warn("Invalid interface ID \"{0}\" for tun device!", id);
                    id = "";
                }

                Utilities.StartThread(delegate {
                    Process.Start("route", string.Format("add {0} mask 255.255.255.255 {1}", addr, gateway));
                });

                Log.Info("Adding gateway part 1/2 (0.0.0.0/1)...");
                Utilities.StartThread(delegate {
                    Process.Start("route", "add 0.0.0.0 mask 128.0.0.0 10.0.0.1 metric 1" + (id != "" ? " if " + id : ""));
                });

                Log.Info("Adding gateway part 2/2 (128.0.0.0/1)...");
                Utilities.StartThread(delegate {
                    Process.Start("route", "add 128.0.0.0 mask 128.0.0.0 10.0.0.1 metric 1" + (id != "" ? " if " + id : ""));
                });

                Log.Info("Finished configuring routes.");
            });
        }

        public static IPAddress GetDestination(byte[] packet)
        {
            return new IPAddress(BitConverter.ToUInt32(packet, 16));
        }

        public static IPAddress GetSource(byte[] packet)
        {
            return new IPAddress(BitConverter.ToUInt32(packet, 12));
        }

        public static byte[] GetDestinationBytes(byte[] packet)
        {
            return new byte[4] { packet[16], packet[17], packet[18], packet[19] };
        }

        public static byte[] SetSource(byte[] packet, byte[] addr)
        {
            for (int i = 0; i < 4; i++)
                packet[12 + i] = addr[i];

            return packet;
        }

        public static byte[] SetDestination(byte[] packet, byte[] addr)
        {
            for (int i = 0; i < 4; i++)
                packet[16 + i] = addr[i];

            return packet;
        }

        public static byte[] SwapDestinationSource(byte[] packet)
        {
            byte[] source = new byte[4];
            Array.Copy(packet, 12, source, 0, 4);
            Array.Copy(packet, 16, packet, 12, 4);
            Array.Copy(source, 0, packet, 16, 4);
            return packet;
        }

        public static byte[] MakeICMPResponse(byte[] packet)
        {
            if (!IsICMPPacket(packet))
                return packet;

            int offset = GetIPHeaderLength(packet);
            packet[offset] = 0x00;

            packet[offset + 2] = packet[offset + 3] = 0;
            ushort checksum = CalculateIPChecksum(packet, offset);
            
            packet[offset + 2] = (byte)((checksum & 0x00FF));
            packet[offset + 3] = (byte)((checksum & 0xFF00) >> 8);

            return packet;
        }

        public static ushort CalculateIPChecksum(byte[] data, int offset = 0)
        {
            data[10] = data[11] = 0;

            long sum = 0;
            int len = GetIPHeaderLength(data);

            for(int i = offset; i < len; i += 2)
                sum += BitConverter.ToUInt16(data, i);

            while((sum >> 16) != 0)
                sum = (sum & 0xFFFF) + (sum >> 16);

            return (ushort)~sum;
        }

        public static int GetIPHeaderLength(byte[] packet)
        {
            return (packet[0] & 0x0F) << 2;
        }

        public static int GetIPVersion(byte[] packet)
        {
            return packet[0] >> 4;
        }

        public static bool IsICMPPacket(byte[] packet)
        {
            return (GetIPVersion(packet)) == 4 && (packet[9] == 0x01);
        }

        public static string GetDefaultGateway(string ip)
        {
            ProcessStartInfo psi = new ProcessStartInfo("ip", "route get " + ip);
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;

            var proc = Process.Start(psi);
            string line = proc.StandardOutput.ReadLine();

            if (!line.Contains("via") || !line.Contains("dev"))
                return null;

            return line.Split(new[] { "via" })[1].Split(new[] { "dev" })[0].Trim();
        }

        public static string GetDefaultGatewayWindows(string ip)
        {
            ProcessStartInfo psi = new ProcessStartInfo("pathping", "-n -w 1 -h 1 -q 1 " + ip);
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.Verb = "runas";

            var proc = Process.Start(psi);
            string line = "";
            proc.WaitForExit();

            while (!(line = proc.StandardOutput.ReadLine().Trim()).StartsWith("1") && !proc.StandardOutput.EndOfStream)
            {
                Log.Trace(line);
            }
            Log.Trace(line);

            if (!line.StartsWith("1"))
                return "";

            return line.Substring(1).Trim();
        }

        public static string GetTunDeviceId()
        {
            ProcessStartInfo psi = new ProcessStartInfo("route", "print -4");
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.Verb = "runas";

            var proc = Process.Start(psi);
            string line = "";

            while (!(line = proc.StandardOutput.ReadLine().Trim()).Contains("TAP-Windows Adapter V9") && !proc.StandardOutput.EndOfStream)
            {
                Log.Trace(line);
            }
            Log.Trace(line);

            return line.Split('.')[0];
        }
    }
}

