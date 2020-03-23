using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Webserver.Webserver {
	class Distributor {
		/// <summary>
		/// Distributes relayed requests over the various worker threads.
		/// </summary>
		/// <param name="Addr">The address the distributor will listen on.</param>
		/// <param name="HttpPort">The port the distributor will listen on.</param>
		/// <param name="Queue">The BlockingCollection queue that incoming requests will be played in.</param>
		public static void Run(IPAddress Addr, int HttpPort, BlockingCollection<ContextProvider> Queue) {
			HttpListener Listener = new HttpListener();
			Console.WriteLine(string.Format("Distributor listening on {0}:{1}", Addr, HttpPort));
			Listener.Prefixes.Add(string.Format("http://{0}:{1}/", Addr.ToString(), HttpPort));
			Listener.Start();

			while (true) {
				try{
					HttpListenerContext Context = Listener.GetContext();
					Console.WriteLine("Received request from {0}", Context.Request.RemoteEndPoint);
					Queue.Add(new ContextProvider(Context));
				} catch (HttpListenerException e){
					Console.WriteLine(e);
				}				
			}
		}
	}
}
