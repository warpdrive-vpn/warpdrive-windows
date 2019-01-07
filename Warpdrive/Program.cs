using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;
using Microsoft.Win32;

using NDesk.Options;
using NLog;

using Bifrost;
using Bifrost.Udp;

namespace Warpdrive
{
	class MainClass
	{
        public static EncryptedLink Link;
        static Logger Log = LogManager.GetCurrentClassLogger();

		public static void Main (string[] args)
        {
            Bifrost.Utilities.LogVersion();
            SuiteRegistry.Initialize();
            Identifier.Create();
            TimerResolution.TryOptimizeTimerResolution();

            string config_file = "";

            OptionSet set = new OptionSet();
            set = new OptionSet()
            {
                {"server", "Runs this instance as a server.", s => Config.SetValue("network.server", true) },
                {"ip=", "Sets the IP and subnet mask for the tun interface.", addr => Config.SetValue("network.tun.addr", addr) },
                {"d|dev=", "Sets the name of the tun interface device(POSIX only)", dev => Config.SetValue("network.tun.name", dev) },
                {"c|connect=", "Connects to the provided IP and port.", c => Config.SetValue("network.connect", c) },
                {"l|listen=", "Starts listening on the provided IP and port.", l => Config.SetValue("network.listen", l) },
                {"no-encryption", "Turns off encryption.", e => Config.SetValue("bifrost.encrypt", false) },
                {"ignore-auth", "Ignores authentication errors.", a => Config.SetValue("bifrost.warn_auth", false) },
                {"disable-auth", "Turns off authentication altogether.", a => Config.SetValue("bifrost.auth", false) },
                {"v|verbosity=", "Sets the output verbosity level.", v => { Config.SetValue("log.verbosity", v); } },
                {"p|key-path=", "Sets the private key path.", p => Config.SetValue("bifrost.sk_path", p) },
                {"s|signature-path=", "Sets the signature file path.", s => Config.SetValue("bifrost.sign_path", s)},
                {"ca|certificate-authority-path=", "Sets the certificate authority public key path.", c => Config.SetValue("bifrost.ca_path", c) },
                {"?|h|help", "Shows help text.", h => ShowHelp(set) },
                {"autoconf", "Attempts to automatically configure routes when running as a client.", a => Config.SetValue("network.autoconf", true) },
                {"config=", "Sets the path to the config file.", c => config_file = c },
                {"attest", "Performs an attestation and prints the token.", a => Attest() }
            };

            var leftovers = set.Parse(args);

            if(config_file == "")
            {
                var possible_files = leftovers.Where(File.Exists);

                if (possible_files.Any())
                {
                    config_file = possible_files.First();
                }
                else
                {
                    config_file = "./config.json";
                }
            }

            Log.Info("Loading config from {0}", config_file);

            try
            {
                Config.Load(config_file);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Couldn't load config file.");
            }

            set.Parse(args);

            if(!Config.Loaded)
            {
                Log.Warn("No config file loaded. If you're configuring Warpdrive using command-line arguments, you can safely ignore this. " +
                    "However, if you haven't supplied any meaningful arguments (or if you don't know what this message means), any crashes " +
                    "can be attributed to a lack of configuration. Warpdrive will now try to set some sane defaults.");

                Config.Init();

                Config.SetValue("network.server", false);
                Config.SetValue("bifrost.encrypt", true);
                Config.SetValue("bifrost.warnauth", true);
                Config.SetValue("bifrost.auth", true);
                Config.SetValue("log.verbosity", "info");
            }
            
            foreach (var rule in LogManager.Configuration.LoggingRules)
                rule.EnableLoggingForLevel(LogLevel.FromString(Config.GetString("log.verbosity")));

            LogManager.ReconfigExistingLoggers();

            if (string.IsNullOrWhiteSpace(Config.GetString("bifrost.ca_path")))
                Config.SetValue("bifrost.ca_path", "./ca");

            if (string.IsNullOrWhiteSpace(Config.GetString("bifrost.sk_path")))
                Config.SetValue("bifrost.sk_path", Config.GetBool("network.server") ? "./server.sk" : "./client.sk");

            if (string.IsNullOrWhiteSpace(Config.GetString("bifrost.sign_path")))
                Config.SetValue("bifrost.sign_path", Config.GetBool("network.server") ? "./server.sign" : "./client.sign");

            string gateway = "";
            
            if (!Config.GetBool("network.server") && Config.GetBool("network.autoconf"))
            {
                Uri uri = new Uri(Config.GetString("network.connect"));

                if (!IPAddress.TryParse(uri.Host, out IPAddress addr))
                {
                    Log.Info("Resolving {0}...", uri.Host);
                    addr = Dns.GetHostEntry(uri.Host).AddressList[0];
                    Log.Info("{0} is {1}", uri.Host, addr);

                    UriBuilder builder = new UriBuilder(uri)
                    {
                        Host = addr.ToString()
                    };

                    Config.SetValue("network.connect", builder.Uri.ToString());
                }

                gateway = NetworkTools.GetDefaultGatewayWindows(addr.ToString());
            }

            TunInterface tun = OpenTun(Config.GetString("network.tun.name") ?? "", Config.GetString("network.tun.addr"));

            Utilities.StartThread(delegate 
            {
                if (Config.GetBool("network.server"))
                {
                    var eps = Config.GetArray<string>("network.listen");
                    List<IListener> listeners = new List<IListener>();

                    foreach (var ep_str in eps)
                    {
                        Uri uri = new Uri(ep_str);
                        var ep = new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port);

                        switch (uri.Scheme)
                        {
                            case "ws":
                                listeners.Add(new WebSocketListener(ep, uri.Host, uri.Host));
                                Log.Info("Added Websocket listener at {0}", ep);
                                break;
                            case "udp":
                                listeners.Add(new UdpListener(ep));
                                Log.Info("Added UDP listener at {0}", ep);
                                break;
                        }
                    }

                    ServerHandler handler = new ServerHandler();

                    handler.Listeners = listeners;

                    handler.Certificate = Config.GetString("bifrost.sk_path");
                    handler.CertificateAuthority = Config.GetString("bifrost.ca_path");
                    handler.Signature = Config.GetString("bifrost.sign_path");

                    handler.Tun = tun;
                
                    handler.Start();
                }
                else
                {
                    string connect_config = Config.GetString("network.connect");

                    if (Config.GetBool("network.autoconf"))
                    {
                        NetworkTools.ConfigureRoutesWindows(connect_config, gateway);
                    }

                    ClientHandler handler = new ClientHandler()
                    {
                        Certificate = Config.GetString("bifrost.sk_path"),
                        CertificateAuthority = Config.GetString("bifrost.ca_path"),
                        Signature = Config.GetString("bifrost.sign_path"),

                        Tun = tun
                    };

                    SystemEvents.PowerModeChanged += handler.HandlePowerEvents;

                    handler.Start(connect_config);

                    Link = handler.Link;
                }
            });

            while(true)
            {
                string command = Console.ReadLine();
                Log.Trace("Read \"{0}\" from stdin.", command);

                switch(command)
                {
                    case "kill":
                        Console.WriteLine("Exiting...");

                        Link.Tunnel.Close();
                        Thread.Sleep(250);
                        Environment.Exit(0);
                        break;
                    default:
                        break;
                }
            }
		}

        public static TunInterface OpenTun(string name, string ip)
        {
            var ip_parts = ip.Split('/');

            IPAddress actual_ip = IPAddress.Parse(ip_parts[0]);
            string gateway = string.Join(".", actual_ip.ToString().Split('.').Take(3)) + ".1";

            int subnet_cidr = int.Parse(ip_parts[1]);
            IPAddress subnet_mask = new IPAddress(IPAddress.HostToNetworkOrder(((int)(Math.Pow(2, subnet_cidr) - 1) << (32 - subnet_cidr))));

            Log.Debug("Opening tun device {0}", name);

            TunInterface tun = TunInterface.Open(name, actual_ip.GetAddressBytes(), subnet_mask.GetAddressBytes());

            Log.Info("Opened tun device {0}", tun.Name);

            Log.Debug("Setting IP and subnet mask to {0}", ip);
            Process.Start("netsh", "interface ip set address tundev static " + actual_ip + " " + subnet_mask + " " + gateway);

            return tun;
        }

        private static void Attest()
        {
            Log.Info("Attestation token: {0}", Identifier.Value.ToUsefulString());
            Environment.Exit(0);
        }

        private static void ShowHelp(OptionSet set)
        {
            Console.WriteLine("Usage: ");
            Console.WriteLine("warpdrive [--server] --connect <IP:port> --listen <IP:port> [OPTIONS]");
            Console.WriteLine();
            set.WriteOptionDescriptions(Console.Out);
            Console.WriteLine();
        }
	}
}
