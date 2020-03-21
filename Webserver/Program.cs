using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Webserver.API;
using Webserver.LoadBalancer;
using Webserver.Webserver;

namespace Webserver {
	class Program {
		public const string wwwroot = "./wwwroot/";

		static void Main() {
			//Check file integrity
			int Diff = Integrity.VerifyIntegrity(wwwroot);
			Console.WriteLine("Checking file integrity...");
			if (Diff > 0){
				Console.WriteLine("Integrity check failed. Validation failed for {0} file(s).", Diff);
				Console.WriteLine("If you modified any files within wwwroot, remember to delete Checksums.json afterwards");
				Console.ReadLine();
				return;
			}
			Console.WriteLine("No integrity issues found.");

			//Parse redirects
			Redirects.LoadRedirects("Redirects.config");
			Console.WriteLine("Registered {0} redirections", Redirects.RedirectDict.Count);

			//Discover endpoints
			APIEndpoint.DiscoverEndpoints();

			//Start load balancer
			IPAddress Local = Balancer.Init(
				new List<IPAddress>() { IPAddress.Parse("192.168.178.9"), IPAddress.Parse("192.168.178.8"), IPAddress.Parse("192.168.178.7")},
				IPAddress.Parse("224.0.0.1"),
				12000,
				12001
			);

			//Start distributor and worker threads
			BlockingCollection<ContextProvider> Queue = new BlockingCollection<ContextProvider>();
			List<RequestWorker> Workers = new List<RequestWorker>();
			for(int i = 0; i < 6; i++){
				RequestWorker Worker = new RequestWorker(Queue);
				new Thread(() => Worker.Run()).Start();
			}
			new Thread(() => Distributor.Run(Local, 12001, Queue)).Start();
		}
	}
}
