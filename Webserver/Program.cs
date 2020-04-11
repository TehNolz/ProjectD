using Config;

using Newtonsoft.Json;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

using Webserver.API;
using Webserver.LoadBalancer;
using Webserver.Webserver;

namespace Webserver
{
	class Program
	{
		static void Main()
		{
			//If the config file doesn't exist, create a new one with the default values.
			if (!File.Exists("Config.json"))
			{
				ConfigFile.Write("Config.json");
			}


			//Load the configuration file. If any values are missing, they will be inserted.
			int missing;
			try
			{
				missing = ConfigFile.Load("Config.json");
			}
			catch (JsonReaderException e)
			{
				Console.WriteLine(e);
				Console.WriteLine("The configuration file is not a valid JSON file. The server cannot start.");
				return;
			}

			if (missing > 0)
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
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.wwwroot);
				if (Diff > 0)
				{
					Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", Diff);
					Console.WriteLine("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Console.WriteLine("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.wwwroot, true);
				}
				Console.WriteLine("No integrity issues found.");
			}


			//Crawl through the wwwroot folder to find all resources.
			Resource.Crawl(WebserverConfig.wwwroot);


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
				var Worker = new RequestWorker(queue);
				new Thread(() => Worker.Run()).Start();
			}
			var distributor = new Thread(() => Distributor.Run(localAddress, BalancerConfig.HttpRelayPort, queue));
			distributor.Start();

			//TODO: Implement proper shutdown
			distributor.Join();
		}
	}
}
