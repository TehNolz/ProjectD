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

namespace Webserver {
	class Program {
		static void Main() {
			//Load config file
			if(!File.Exists("Config.json")){
				ConfigFile.Write("Config.json");
			}
			int Missing = ConfigFile.Load("Config.json");
			if(Missing > 0){
				ConfigFile.Write("Config.json");
				Console.WriteLine("{0} configuration setting(s) are missing. The missing settings have been inserted.", Missing);
			}

			//Check file integrity if necessary.
			if(MiscConfig.VerifyIntegrity){
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.wwwroot);
				Console.WriteLine("Checking file integrity...");
				if (Diff > 0) {
					Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", Diff);
					Console.WriteLine("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Console.WriteLine("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.wwwroot, true);
				}
				Console.WriteLine("No integrity issues found.");
			}

			//Crawl webpages.
			Resource.Crawl(WebserverConfig.wwwroot);

			//Parse redirects
			Redirects.LoadRedirects("Redirects.config");
			Console.WriteLine("Registered {0} redirections", Redirects.RedirectDict.Count);

			//Discover endpoints
			APIEndpoint.DiscoverEndpoints();

			//Check multicast address
			if(!IPAddress.TryParse(BalancerConfig.MulticastAddress, out IPAddress Multicast)){
				Console.WriteLine("The MulticastAddress specified in the configuration file is not a valid IP address. The server cannot start.");
				return;
			}

			//Check addresses
			List<IPAddress> Addresses = new List<IPAddress>();
			if(BalancerConfig.IPAddresses.Count == 0){
				//Autodetect
				using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
				socket.Connect("8.8.8.8", 65530);
				Addresses.Add((socket.LocalEndPoint as IPEndPoint).Address);

			} else {
				foreach(string Addr in BalancerConfig.IPAddresses){
					if(!IPAddress.TryParse(Addr, out IPAddress Address)){
						Console.WriteLine("Skipping invalid address {0}", Addr);
					} else {
						Addresses.Add(Address);
					}
				}
			}
			if(Addresses.Count == 0){
				Console.WriteLine("No addresses were configured. The server cannot start.");
				return;
			}

			//Start load balancer
			IPAddress Local = Balancer.Init(Addresses, Multicast, BalancerConfig.BalancerPort, BalancerConfig.HttpRelayPort);

			//Start distributor and worker threads
			BlockingCollection<ContextProvider> Queue = new BlockingCollection<ContextProvider>();
			List<RequestWorker> Workers = new List<RequestWorker>();
			for(int i = 0; i < WebserverConfig.WorkerThreadCount; i++){
				RequestWorker Worker = new RequestWorker(Queue);
				new Thread(() => Worker.Run()).Start();
			}
			Thread Distr = new Thread(() => Distributor.Run(Local, 12001, Queue));
			Distr.Start();

			//TODO: Implement proper shutdown
			Distr.Join();
		}
	}
}
