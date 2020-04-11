using System;
using System.Collections.Concurrent;
using System.Net;

namespace Webserver.Webserver
{
	class Distributor
	{
		/// <summary>
		/// Distributes relayed requests over the various worker threads.
		/// </summary>
		/// <param name="address">The address the distributor will listen on.</param>
		/// <param name="port">The port the distributor will listen on.</param>
		/// <param name="queue">The BlockingCollection queue that incoming requests will be played in.</param>
		public static void Run(IPAddress address, int port, BlockingCollection<ContextProvider> queue)
		{
			//Create and start the HttpListener. Localhost is not allowed because we do not want these to be used directly. All traffic should go through the load balancer first.
			var listener = new HttpListener();
			listener.Prefixes.Add(string.Format("http://{0}:{1}/", address.ToString(), port));
			listener.Start();

			Console.WriteLine("Distributor listening on {0}:{1}", address, port);

			//Main loop. Accepts incoming requests and places them in the queue.
			while (true)
			{
				try
				{
					HttpListenerContext Context = listener.GetContext();
					Console.WriteLine("Received request from {0}", Context.Request.RemoteEndPoint);
					queue.Add(new ContextProvider(Context));
				}
				catch (HttpListenerException e)
				{
					Console.WriteLine(e);
				}
			}
		}
	}
}
