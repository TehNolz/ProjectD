using Config;
using Database.SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using Webserver.API;
using Webserver.API.Endpoints;
using Webserver.LoadBalancer;
using Webserver.Models;
using Webserver.Webserver;

namespace Webserver
{
	class Program
	{
		public const string DatabaseName = "Database.db";
		public static SQLiteAdapter Database;

		public static void Main()
		{
			//Load config file
			if (!File.Exists("Config.json"))
				ConfigFile.Write("Config.json");

			if (ConfigFile.Load("Config.json") is int missing && missing > 0)
			{
				ConfigFile.Write("Config.json");
				Console.WriteLine("{0} configuration setting(s) are missing. The missing settings have been inserted.", missing);
			}

			//Check for duplicate network ports. Each port setting needs to be unique as we can't bind to one port multiple times.
			var ports = new List<int>() { BalancerConfig.BalancerPort, BalancerConfig.DiscoveryPort, BalancerConfig.HttpRelayPort, BalancerConfig.HttpPort };
			if (ports.Distinct().Count() != ports.Count)
			{
				Console.WriteLine("One or more duplicate network port settings have been detected. The server cannot start.");
				Console.WriteLine("Press any key to exit.");
				Console.ReadKey();
				return;
			}

			//If the VerifyIntegrity config option is enabled, check all files in wwwroot for corruption.
			//If at least one checksum mismatch is found, pause startup and show a warning.
			if (MiscConfig.VerifyIntegrity)
			{
				Console.WriteLine("Checking file integrity...");
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.WWWRoot);
				if (Diff > 0)
				{
					Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", Diff);
					Console.WriteLine("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Console.WriteLine("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.WWWRoot, true);
				}
				Console.WriteLine("No integrity issues found.");
			}

			// Initialize database
			Database = new SQLiteAdapter(DatabaseName);
			Database.TryCreateTable<ExampleModel>();
			Database.TryCreateTable<User>();
			
			//Crawl through the wwwroot folder to find all resources.
			Resource.Crawl(WebserverConfig.WWWRoot);


			//Parse Redirects.config to register all HTTP redirections.
			Redirects.LoadRedirects("Redirects.config");
			Console.WriteLine("Registered {0} redirections", Redirects.RedirectDict.Count);


			//Register all API endpoints
			APIEndpoint.DiscoverEndpoints();

			//Start load balancer
			IPAddress localAddress;
			try
			{
				localAddress = Balancer.Init();
				Console.WriteLine("Started load balancer.");
			}
			catch (ArgumentException e)
			{
				Console.WriteLine(e.Message);
				Console.WriteLine("The server could not start.");
				Console.ReadLine();
				return;
			}

			//Start distributor and worker threads
			var queue = new BlockingCollection<ContextProvider>();
			var workers = new List<RequestWorker>();
			for (int i = 0; i < WebserverConfig.WorkerThreadCount; i++)
			{
				var worker = new RequestWorker(queue);
				workers.Add(worker);
				worker.Start();
			}

			var distributor = new Thread(() => Distributor.Run(localAddress, BalancerConfig.HttpRelayPort, queue));
			distributor.Start();

			foreach (var worker in workers)
				worker.Join();

			//TODO: Implement proper shutdown
			Distributor.Dispose();
			distributor.Join();
		}
	}
}
