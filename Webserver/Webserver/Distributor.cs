using System.Net;

using Webserver.Chat;

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

			Log.Info($"Distributor listening on {address}:{port}");
			while (true)
			{
				try
				{
					//Wait for incoming requests.
					var context = new ContextProvider(Listener.GetContext());

					//If the received request is a request to open a websocket, accept it only if the URL ends with /chat
					if (context.Request.IsWebSocketRequest)
					{
						if (context.Request.Url.LocalPath.EndsWith("/chat"))
							new ChatConnection(context);
						else
							context.Response.Send(HttpStatusCode.BadRequest);
						continue;
					}

					Log.Trace($"Received request from {context.Request.RemoteEndPoint}");
					RequestWorker.Queue.Add(context);
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
