using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Webserver.LoadBalancer;

namespace Webserver {
	class Program {
		static void Main() {
			IPAddress Local = Balancer.Init(
				new List<IPAddress>() { IPAddress.Parse("192.168.178.9"), IPAddress.Parse("192.168.178.8"), IPAddress.Parse("192.168.178.7")},
				IPAddress.Parse("224.0.0.1"),
				12000,
				12001
			);
			new Thread(() => Worker(Local, 12001)).Start();
		}

		public static void Worker(IPAddress Addr, int HttpPort) {
			HttpListener Listener = new HttpListener();
			Console.WriteLine(string.Format("Worker listening on {0}:{1}", Addr, HttpPort));
			Listener.Prefixes.Add(string.Format("http://{0}:{1}/", Addr.ToString(), HttpPort));
			Listener.Start();

			while (true) {
				HttpListenerContext Context = Listener.GetContext();
				Console.WriteLine("Got request!");
				Context.Response.StatusCode = 200;
				Context.Response.OutputStream.Write(Encoding.UTF8.GetBytes("Success"!));
				Context.Response.OutputStream.Close();
			}
		}
	}
}
