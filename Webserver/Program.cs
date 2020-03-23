using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Webserver.API;
using Webserver.LoadBalancer;
using Webserver.Webserver;
using Config;
using System.IO;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Linq;

namespace Webserver
{
	class Program
	{
		static void Main()
		{
			//Load config file
			if (!File.Exists("Config.json"))
				ConfigFile.Write("Config.json");

			if (ConfigFile.Load("Config.json") is int missing && missing > 0)
			{
				ConfigFile.Write("Config.json");
				Console.WriteLine("{0} configuration setting(s) are missing. The missing settings have been inserted.", missing);
			}

			//Check file integrity if necessary.
			if (MiscConfig.VerifyIntegrity)
			{
				Console.WriteLine("Checking file integrity...");
				if (Integrity.VerifyIntegrity(WebserverConfig.WWWRoot) is int diff && diff > 0)
				{
					Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", diff);
					Console.WriteLine("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Console.WriteLine("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.WWWRoot, true);
				}
				Console.WriteLine("No integrity issues found.");
			}

			//Crawl webpages.
			Resource.Crawl(WebserverConfig.WWWRoot);

			//Parse redirects
			Redirects.LoadRedirects("Redirects.config");
			Console.WriteLine("Registered {0} redirections", Redirects.RedirectDict.Count);

			//Discover endpoints
			APIEndpoint.DiscoverEndpoints();

			//Check multicast address
			if (!IPAddress.TryParse(BalancerConfig.MulticastAddress, out IPAddress multicast))
			{
				Console.WriteLine("The MulticastAddress specified in the configuration file is not a valid IP address. The server cannot start.");
				return;
			}

			//Check addresses
			List<IPAddress> addresses = new List<IPAddress>();
			if (BalancerConfig.IPAddresses.Count == 0)
			{
				//Autodetect
				using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
				socket.Connect("8.8.8.8", 65530);
				addresses.Add((socket.LocalEndPoint as IPEndPoint).Address);
			}
			else
			{
				foreach (string address in BalancerConfig.IPAddresses)
				{
					if (!IPAddress.TryParse(address, out IPAddress Address))
					{
						Console.WriteLine("Skipping invalid address {0}", address);
					}
					else addresses.Add(Address);
				}
			}
			if (!addresses.Any())
			{
				Console.WriteLine("No addresses were configured. The server cannot start.");
				return;
			}

			//Start load balancer
			var localAddress = Balancer.Init(addresses, multicast, BalancerConfig.BalancerPort, BalancerConfig.HttpRelayPort);

			//Start distributor and worker threads
			var Queue = new BlockingCollection<ContextProvider>();
			var Workers = new List<RequestWorker>();
			for (int i = 0; i < WebserverConfig.WorkerThreadCount; i++)
				new RequestWorker(Queue).Start();

			Thread distributor = new Thread(() => Distributor.Run(localAddress, 12001, Queue));
			distributor.Start();

			//TODO: Implement proper shutdown
			distributor.Join();
		}
	}
}
