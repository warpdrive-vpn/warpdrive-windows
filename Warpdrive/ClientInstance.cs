using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

using Bifrost;
using NLog;

namespace Warpdrive
{
    public class ClientInstance
    {
        public TunInterface Tun;
        public ServerLink Link;
        
        public ITunnel Tunnel;

        public string CertificatePath;
        public string CertificateAuthorityPath;
        public string SignaturePath;

        public IPAddress Address;

        private Thread TunToLink;
        private DataReceived LinkToTun;

        private Logger Log = LogManager.GetCurrentClassLogger();

        public ClientInstance(ITunnel tunnel, string ca_path, string cert_path, string sign_path)
        {
            Tunnel = tunnel;
            CertificatePath = cert_path;
            CertificateAuthorityPath = ca_path;
            SignaturePath = sign_path;
        }

        public void WritePacket(byte[] data)
        {
            Link.SendData(data);
        }

        public bool Init()
        {
            try
            {
                Link = new ServerLink(Tunnel);

                Link.LoadCertificatesFromFiles(
                    CertificateAuthorityPath,
                    CertificatePath,
                    SignaturePath);

                if (Link.PerformHandshake().Type != HandshakeResultType.Successful)
                {
                    Log.Error("Handshake failed, can't init");
                    return false;
                }

                LinkToTun = (sender, data) =>
                {
                    Tun.Write(data);
                    Log.Trace("Wrote {0} bytes", data.Length);
                    Log.Trace("Packet from link: {0} -> {1}", NetworkTools.GetSource(data), NetworkTools.GetDestination(data));

                    if (Address == null)
                    {
                        if (NetworkTools.GetSource(data).ToString().StartsWith("10.0.0"))
                            Address = NetworkTools.GetSource(data);
                    }
                };

                Link.OnDataReceived += LinkToTun;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to initialize client instance");
                Log.Error("\tRemote endpoint: {0}", Tunnel.ToString());
                Log.Error(ex);

                try
                {
                    End();
                    Log.Info("Successfully terminated client instance");
                }
                catch
                {
                    Log.Info("Failed to terminate client instance");
                }

                return false;
            }
        }

        public void End()
        {
            Link.Tunnel.Close();
            Link.OnDataReceived -= LinkToTun;
        }
    }
}

