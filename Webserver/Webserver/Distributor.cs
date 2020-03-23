using System;
using System.Collections.Concurrent;
using System.Net;

namespace Webserver.Webserver {
	class Distributor {
		/// <summary>
		/// Distributes relayed requests over the various worker threads.
		/// </summary>
		/// <param name="Address">The address the distributor will listen on.</param>
		/// <param name="Port">The port the distributor will listen on.</param>
		/// <param name="Queue">The BlockingCollection queue that incoming requests will be played in.</param>
		public static void Run(IPAddress Address, int Port, BlockingCollection<ContextProvider> Queue) {
			//Create and start the HttpListener
			HttpListener Listener = new HttpListener();
			Listener.Prefixes.Add(string.Format("http://{0}:{1}/", Address.ToString(), Port));
			Listener.Start();

			Console.WriteLine("Distributor listening on {0}:{1}", Address, Port);

			//Main loop. Accepts incoming requests and places them in the queue.
			while(true) {
				try {
					HttpListenerContext Context = Listener.GetContext();
					Console.WriteLine("Received request from {0}", Context.Request.RemoteEndPoint);
					Queue.Add(new ContextProvider(Context));
				} catch(HttpListenerException e) {
					Console.WriteLine(e);
				}
			}
		}
	}
}
