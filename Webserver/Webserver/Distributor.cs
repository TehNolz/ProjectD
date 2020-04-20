using System;
using System.Net;

using static Webserver.Program;

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

			Log.Config($"Distributor listening on {address}:{port}");
			while (true)
			{
				try
				{
					HttpListenerContext context = Listener.GetContext();
					Log.Trace($"Received request from {context.Request.RemoteEndPoint}");
					RequestWorker.Queue.Add(new ContextProvider(context));
				}
				catch (HttpListenerException e)
				{
					Log.Warning($"{e.GetType().Name}: {e.Message}");
				}
			}
		}

		public static void Dispose() => Listener.Close();
	}
}
