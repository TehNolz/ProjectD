using System;
using System.Net;

namespace Webserver.Webserver
{
	internal static class Distributor
	{
		private static HttpListener Listener { get; set; }

		/// <summary>
		/// Distributes relayed requests over the various worker threads.
		/// </summary>
		/// <param name="address">The address the distributor will listen on.</param>
		/// <param name="port">The port the distributor will listen on.</param>
		/// <param name="queue">The BlockingCollection queue that incoming requests will be played in.</param>
		public static void Run(IPAddress address, int port)
		{
			// Create and start a new HttpListener for the given port and address
			Listener = new HttpListener();
			Listener.Prefixes.Add($"http://{address}:{port}/");
			Listener.Start();

			Console.WriteLine("Distributor listening on {0}:{1}", address, port);
			while (true)
			{
				try
				{
					HttpListenerContext context = Listener.GetContext();
					Console.WriteLine("Received request from {0}", context.Request.RemoteEndPoint);
					RequestWorker.Queue.Add(new ContextProvider(context));
				}
				catch (HttpListenerException e)
				{
					Console.WriteLine(e);
				}
			}
		}

		public static void Dispose() => Listener.Close();
	}
}
