using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

using Bifrost;

using NLog;
using System.Collections;
using Bifrost.Udp;
using System.Collections.Concurrent;

namespace Warpdrive
{
    public class ServerHandler
    {
        public List<IListener> Listeners { get; set; }

        public string CertificateAuthority { get; set; }
        public string Certificate { get; set; }
        public string Signature { get; set; }

        public bool Running { get; set; }
        public TunInterface Tun { get; set; }

        public ServerLink Link;
        public Dictionary<byte[], ClientInstance> Clients = new Dictionary<byte[], ClientInstance>(new StructuralEqualityComparer<byte[]>());
        public List<ClientInstance> UnenumeratedClients = new List<ClientInstance>();

        private Logger Log = LogManager.GetCurrentClassLogger();

        public ServerHandler()
        {
        }

        public void Start()
        {
            Running = true;
            Listeners.ForEach(l => l.Start());

            Utilities.StartThread(ListenLoop);
            Utilities.StartThread(TunEmptyLoop);
            Utilities.StartThread(DataLoop);
            Utilities.StartThread(ResolveAddressesLoop);
        }

        public void ResolveAddressesLoop()
        {
            while (true)
            {
                Thread.Sleep(100);

                foreach (var client in UnenumeratedClients)
                {
                    if (client.Address != null)
                    {
                        if (Clients.ContainsKey(client.Address.GetAddressBytes()))
                        {
                            Log.Warn("Kicking client with IP {0}", client.Address);
                            Clients[client.Address.GetAddressBytes()].Link.Tunnel.Close();
                            Clients.Remove(client.Address.GetAddressBytes());
                        }

                        if (Clients.Any(c => client.Link.PeerSignature.SequenceEqual(c.Value.Link.PeerSignature)))
                        {
                            Log.Warn("Kicking client with signature {0}", client.Link.PeerSignature.ToUsefulString());

                            foreach (var dupe in Clients.Where(c => client.Link.PeerSignature.SequenceEqual(c.Value.Link.PeerSignature)).ToList())
                            {
                                Log.Warn("Kicked client with IP {0}/signature {1}", dupe.Value.Address, dupe.Value.Link.PeerSignature.ToUsefulString());
                                Clients[dupe.Key].Link.Tunnel.Close();
                                Clients.Remove(dupe.Key);
                            }
                        }

                        Clients.Add(client.Address.GetAddressBytes(), client);
                        Log.Info("Moved client to enumerated pool with address {0}/signature {1}", client.Address, client.Link.PeerSignature.ToUsefulString());
                    }
                }

                UnenumeratedClients.RemoveAll(client => Clients.ContainsValue(client));

                var list = new List<KeyValuePair<byte[], ClientInstance>>();

                foreach (var pair in Clients)
                    if (pair.Value.Link.Tunnel.Closed)
                        list.Add(pair);

                list.ForEach(pair => Clients.Remove(pair.Key));
                if (list.Any())
                    Log.Info("Purged {0} dead clients from routing table", list.Count);
            }
        }

        public void ListenLoop()
        {
            while (Running)
            {
                BlockingCollection<ITunnel>.TakeFromAny(Listeners.Select(l => l.Queue).ToArray(), out ITunnel tunnel);

                Log.Info("Accepted tunnel {0}", tunnel.ToString());

                ClientInstance instance = new ClientInstance(tunnel, CertificateAuthority, Certificate, Signature);
                instance.Tun = Tun;

                if (!instance.Init())
                    continue;

                instance.Link.BufferedWrite = false;

                UnenumeratedClients.Add(instance);
            }
        }

        public void DataLoop()
        {
            while (true)
            {
                byte[] read = Tun.Read();

                var route = GetRoute(read);

                if (route != null)
                {
                    Log.Trace("Routing {0} long packet to {1}", read.Length, route.Address);
                    route.WritePacket(read);
                }
                else
                {
                    Log.Warn("Couldn't find route for packet with destination address {0}", NetworkTools.GetDestination(read));
                    continue;
                }
            }
        }

        public void TunEmptyLoop()
        {
            return;
        }

        private SizeQueue<byte[]> Packets = new SizeQueue<byte[]>(500);

        public ClientInstance GetRoute(byte[] packet)
        {
            var addr = NetworkTools.GetDestinationBytes(packet);

            if (Clients.ContainsKey(addr))
                return Clients[addr];

            return null;
        }
    }
}

