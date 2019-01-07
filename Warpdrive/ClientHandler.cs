using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Bifrost;

using NLog;
using System.Diagnostics;

using Bifrost.Udp;
using Microsoft.Win32;

namespace Warpdrive
{
    public class ClientHandler
    {
        public string CertificateAuthority { get; set; }
        public string Certificate { get; set; }
        public string Signature { get; set; }

        public TunInterface Tun { get; set; }

        private Logger Log = LogManager.GetCurrentClassLogger();
        public ClientLink Link;
        private IPAddress ServerAddress;

        private long LastDataBytesReceived = 0;
        private long LastRawBytesReceived = 0;
        private long LastDataBytesSent = 0;
        private long LastRawBytesSent = 0;

        private long LastCompressed = 0;

        private TimeSpan LastProcessorTime = TimeSpan.Zero;
        private Process Process = Process.GetCurrentProcess();

        private DateTime LastMeasured = DateTime.Now;

        public bool Running { get; set; }
        
        public ClientHandler()
        {
        }

        public void Start(string host)
        {
            Utilities.StartThread(() => ConnectLoop(host));
            Utilities.StartThread(delegate
            {
                while (Link == null)
                    Thread.Sleep(100);

                while(true)
                {
                    LastMeasured = DateTime.Now;

                    LastDataBytesReceived = 0;
                    LastDataBytesSent = 0;
                    LastRawBytesReceived = 0;
                    LastRawBytesSent = 0;
                    
                    while(Link != null && Link.Tunnel != null && !Link.Tunnel.Closed)
                    {
                        PrintEfficiency();
                        Thread.Sleep(3000);
                    }
                    Thread.Sleep(1000);
                }
            });
        }

        public void PrintEfficiency()
        {
            long data_rx_delta = Link.Tunnel.DataBytesReceived - LastDataBytesReceived;
            long raw_rx_delta = Link.Tunnel.RawBytesReceived - LastRawBytesReceived;

            long data_tx_delta = Link.Tunnel.DataBytesSent - LastDataBytesSent;
            long raw_tx_delta = Link.Tunnel.RawBytesSent - LastRawBytesSent;

            long raw_delta = raw_tx_delta + raw_rx_delta;
            long data_delta = data_tx_delta + data_rx_delta;

            TimeSpan cpu_delta = Process.TotalProcessorTime - LastProcessorTime;

            if (data_rx_delta == 0 && data_tx_delta == 0 && raw_rx_delta == 0 && raw_tx_delta == 0)
            {
                LastMeasured = DateTime.Now;
                return;
            }

            LastDataBytesReceived = Link.Tunnel.DataBytesReceived;
            LastDataBytesSent = Link.Tunnel.DataBytesSent;
            LastRawBytesReceived = Link.Tunnel.RawBytesReceived;
            LastRawBytesSent = Link.Tunnel.RawBytesSent;

            LastProcessorTime = Process.TotalProcessorTime;

            double overhead_rx = ((double)(raw_rx_delta - data_rx_delta) / (double)raw_rx_delta);
            double overhead_tx = ((double)(raw_tx_delta - data_tx_delta) / (double)raw_tx_delta);

            Log.Info("Incoming overhead: {0:0.00}% ({1:N0} data bytes out of {2:N0} raw bytes)", overhead_rx * 100d, data_rx_delta, raw_rx_delta);
            Log.Info("Outgoing overhead: {0:0.00}% ({1:N0} data bytes out of {2:N0} raw bytes)", overhead_tx * 100d, data_tx_delta, raw_tx_delta);

            double passed_time = (DateTime.Now - LastMeasured).TotalSeconds;

            double data_rx_rate = data_rx_delta / passed_time;
            double data_tx_rate = data_tx_delta / passed_time;

            double raw_rx_rate = raw_rx_delta / passed_time;
            double raw_tx_rate = raw_tx_delta / passed_time;

            double raw_cpu_rate = raw_delta / cpu_delta.Duration().TotalSeconds;
            double data_cpu_rate = data_delta / cpu_delta.Duration().TotalSeconds;

            Log.Info("Incoming data speed: {0:0.00} kb/s, raw speed: {1:0.00} kb/s", data_rx_rate / 1024d, raw_rx_rate / 1024d);
            Log.Info("Outgoing data speed: {0:0.00} kb/s, raw speed: {1:0.00} kb/s", data_tx_rate / 1024d, raw_tx_rate / 1024d);
            
            Log.Info("Processor time spent: {0}({1:0.00}%) (data speed: {2:0.00} kb/s, raw speed: {3:0.00} kb/s)", cpu_delta, (cpu_delta.TotalSeconds / passed_time) * 100d, data_cpu_rate / 1024d, raw_cpu_rate / 1024d);

            LastMeasured = DateTime.Now;
        }

        public void ConnectLoop(string host)
        {
            int seconds_to_wait = 0;

            while (Link == null || Link.Closed)
            {
                ManualResetEvent closed = new ManualResetEvent(false);
                Uri uri = new Uri(host);

                try
                {
                    switch(uri.Scheme)
                    {
                        case "ws":
                            Log.Info("Connecting to {0} over Websocket", uri);

                            var spoofed_host = Config.GetString("network.websocket.spoof_host");
                            var spoofed_origin = Config.GetString("network.websocket.spoof_origin");

                            spoofed_host = string.IsNullOrWhiteSpace(spoofed_host) ? uri.Host : spoofed_host;
                            spoofed_origin = string.IsNullOrWhiteSpace(spoofed_origin) ? uri.Host : spoofed_origin;

                            Link = new ClientLink(new WebSocketTunnel(new TcpClient(uri.Host, uri.Port), spoofed_host, spoofed_origin, false));
                            break;
                        case "udp":
                            Log.Info("Connecting to {0} over UDP", uri);
                            Link = new ClientLink(new UdpTunnel(IPAddress.Parse(uri.Host), uri.Port));
                            break;
                    }

                    Link.OnLinkClosed += (sender) =>
                    {
                        closed.Set();
                    };

                    Link.LoadCertificatesFromFiles(
                        CertificateAuthority,
                        Certificate,
                        Signature);

                    Link.AttestationToken = Identifier.Value;

                    var result = Link.PerformHandshake();

                    if (result.Type != HandshakeResultType.Successful)
                    {
                        Log.Error("Failed handshake: {0}", result.Message);
                        Link = null;
                        throw new Exception(result.Message);
                    }

                    Link.BufferedWrite = false;

                }
                catch (Exception ex)
                {
                    seconds_to_wait += 3;
                    Log.Error(ex, "Failed to connect, retrying in {0} seconds...", seconds_to_wait);
                    Log.Error(ex);
                    Thread.Sleep(seconds_to_wait * 1000);
                    continue;
                }

                seconds_to_wait = 0;
                
                Utilities.StartThread(delegate
                {
                    while(Link != null && Link.Tunnel != null && !Link.Tunnel.Closed)
                    {
                        try
                        {
                            var data = Tun.Read();

                            if (data == null)
                                continue;

                            if (data[0] == 0x60)
                            {
                                Log.Trace("Dropping IPv6 packet");
                                continue;
                            }
                            
                            Log.Trace("Received {0} bytes", data.Length);
                            Link.SendData(data);
                        }
                        catch (Exception ex)
                        {
                            if (ex.Message == "A device attached to the system is not functioning.") // temp hack
                            {
                                Tun.Close();
                                Tun = Tun.Reopen();
                            }

                            Log.Error("Error while reading from tun. HResult: {0}", ex.HResult);
                            Log.Error(ex);
                        }
                    }

                    try
                    {
                        Link?.Close();
                    }
                    catch
                    {

                    }
                    closed.Set();
                });

                Link.OnDataReceived += (sender, data) => {
                    Tun.Write(data);
                    Log.Trace("Wrote {0} bytes", data.Length);
                };

                closed.WaitOne();
                Thread.Sleep(5000);
            }
        }

        public void HandlePowerEvents(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    Running = false;
                    Link.Close();
                    Tun.Close();
                    break;
                case PowerModes.Resume:
                    Tun = Tun.Reopen();
                    Running = true;
                    break;
            }
        }
    }
}

