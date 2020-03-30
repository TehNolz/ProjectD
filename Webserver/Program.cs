using Config;
using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Webserver.API;
using Webserver.LoadBalancer;
using Webserver.Models;
using Webserver.Webserver;

namespace Webserver
{
	class Program
	{
		public static SQLiteAdapter Database;

		public static void Main()
		{
			// Initialize database
			//if (File.Exists("Database.db"))
			//	File.Delete("Database.db");
			Database = new SQLiteAdapter("Database.db");
			try
			{
				// TODO: Add TryCreateTable
				Database.CreateTable<ExampleModel>();
			}
			catch (Exception) { }

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
			var queue = new BlockingCollection<ContextProvider>();
			var workers = new List<RequestWorker>();
			for (int i = 0; i < WebserverConfig.WorkerThreadCount; i++)
			{
				var worker = new RequestWorker(queue);
				workers.Add(worker);
				worker.Start();
			}

			var distributor = new Thread(() => Distributor.Run(localAddress, 12001, queue));
			distributor.Start();

			foreach (var worker in workers)
				worker.Join();

			//TODO: Implement proper shutdown
			Distributor.Dispose();
			distributor.Join();
		}
	}
}
