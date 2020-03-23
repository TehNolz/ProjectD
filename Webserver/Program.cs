using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Config;
using Webserver.API;
using Webserver.LoadBalancer;
using Webserver.Webserver;

namespace Webserver {
	class Program {
		static void Main() {
			//Check if the config file exists.
			if(!File.Exists("Config.json")) {
				//The file doesn't exist, so create one with default values.
				ConfigFile.Write("Config.json");
			} else {
				//The file exists, so try to load it. If its incomplete, add the missing values, show a warning, and save it again.
				int Missing = ConfigFile.Load("Config.json");
				if(Missing > 0) {
					ConfigFile.Write("Config.json");
					Console.WriteLine("{0} configuration setting(s) are missing. The missing settings have been inserted.", Missing);
				}
			}

			//If the VerifyIntegrity option is enabled, check all files in wwwroot for corruption.
			//If at least one checksum mismatch is found, pause startup and show a warning.
			if(MiscConfig.VerifyIntegrity) {
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.wwwroot);
				Console.WriteLine("Checking file integrity...");
				if(Diff > 0) {
					Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", Diff);
					Console.WriteLine("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Console.WriteLine("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.wwwroot, true);
				}
				Console.WriteLine("No integrity issues found.");
			}

			//Crawl through wwwroot to find all resources.
			Resource.Crawl(WebserverConfig.wwwroot);

			//Parse Redirects.config
			Redirects.LoadRedirects("Redirects.config");
			Console.WriteLine("Registered {0} redirections", Redirects.RedirectDict.Count);

			//Register all API endpoints
			APIEndpoint.DiscoverEndpoints();

			//Check multicast address. If its not a valid IP address, we can't proceed, so cancel startup.
			if(!IPAddress.TryParse(BalancerConfig.MulticastAddress, out IPAddress Multicast)) {
				Console.WriteLine("The MulticastAddress specified in the configuration file is not a valid IP address. The server cannot start.");
				return;
			}

			//Check addresses
			List<IPAddress> Addresses = new List<IPAddress>();
			if(BalancerConfig.IPAddresses.Count == 0) {
				//Autodetect
				using Socket Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
				Socket.Connect("8.8.8.8", 65530);
				Addresses.Add((Socket.LocalEndPoint as IPEndPoint).Address);
			} else {
				foreach(string RawAddress in BalancerConfig.IPAddresses) {
					if(!IPAddress.TryParse(RawAddress, out IPAddress Address)) {
						Console.WriteLine("Skipping invalid address {0}", Address);
					} else {
						Addresses.Add(Address);
					}
				}
			}
			//Show a warning if no addresses are configured even after the above checks. We can't start the server without an IP address to bind to.
			if(Addresses.Count == 0) {
				Console.WriteLine("No addresses were configured. The server cannot start.");
				Console.ReadLine();
				return;
			}

			//Start load balancer
			IPAddress Local;
			try {
				Local = Balancer.Init(Multicast, Addresses);
			} catch(ArgumentException e) {
				Console.WriteLine(e.Message);
				Console.WriteLine("The server could not start.");
				Console.ReadLine();
				return;
			}


			//Start distributor and worker threads
			BlockingCollection<ContextProvider> Queue = new BlockingCollection<ContextProvider>();
			List<RequestWorker> Workers = new List<RequestWorker>();
			for(int i = 0; i < WebserverConfig.WorkerThreadCount; i++) {
				RequestWorker Worker = new RequestWorker(Queue);
				new Thread(() => Worker.Run()).Start();
			}
			Thread Distr = new Thread(() => Distributor.Run(Local, BalancerConfig.HttpRelayPort, Queue));
			Distr.Start();

			//TODO: Implement proper shutdown
			Distr.Join();
		}
	}
}
