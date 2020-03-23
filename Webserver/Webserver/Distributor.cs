using System;
using System.Collections.Concurrent;
using System.Net;

namespace Webserver.Webserver
{
	internal static class Distributor
	{
		/// <summary>
		/// Distributes relayed requests over the various worker threads.
		/// </summary>
		/// <param name="address">The address the distributor will listen on.</param>
		/// <param name="port">The port the distributor will listen on.</param>
		/// <param name="queue">The BlockingCollection queue that incoming requests will be played in.</param>
		public static void Run(IPAddress address, int port, BlockingCollection<ContextProvider> queue)
		{
			// Create and start a new HttpListener for the given port and address
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://{address.ToString()}:{port}/");
			listener.Start();

			Console.WriteLine("Distributor listening on {0}:{1}", address, port);
			while (true)
			{
				try
				{
					var context = listener.GetContext();
					Console.WriteLine("Received request from {0}", context.Request.RemoteEndPoint);
					queue.Add(new ContextProvider(context));
				}
				catch (HttpListenerException e)
				{
					Console.WriteLine(e);
				}
			}
		}
	}
}
