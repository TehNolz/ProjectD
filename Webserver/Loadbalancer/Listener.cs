using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

using Webserver.Chat;
using Webserver.Config;

using static Webserver.Program;

namespace Webserver.LoadBalancer
{
	class Listener
	{
		/// <summary>
		/// Listener thread. Waits for incoming HTTP requests and relays them to slaves.
		/// </summary>
		public static Thread ListenerThread;
		///<inheritdoc cref="ListenerThread"/>
		public static void Listen(IPAddress address, int port)
		{
			///Create a new HttpListener.
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://{address}:{port}/");
			listener.Prefixes.Add($"http://localhost:{port}/");
			listener.Start();
			Log.Config($"Load Balancer Listener now listening on {address}:{port}");

			//Main loop
			while (true)
			{
				//Get incoming requests
				HttpListenerContext context = listener.GetContext();

				//If the received request is a request to open a websocket, accept it only if the URL ends with /chat
				if (context.Request.IsWebSocketRequest)
				{
					if (context.Request.Url.LocalPath.EndsWith("/chat"))
					{
						Log.Trace("Received websocket request");
						new WebSocketRelay(context).Start();
						continue;
					}
					else
					{
						context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
						context.Response.OutputStream.Dispose();
					}
				}
				IPAddress slaveAddress = GetBestSlave();
				string URL = $"http://{slaveAddress}:{BalancerConfig.HttpRelayPort}{context.Request.Url.LocalPath}";

				//Start relaying the request.
				var requestRelay = (HttpWebRequest)WebRequest.Create(URL);

				// Transfer headers.
				foreach (string headerName in context.Request.Headers.AllKeys)
					foreach (string headerValue in context.Request.Headers.GetValues(headerName))
						requestRelay.Headers.Add(headerName, headerValue);

				//Transfer method, user agent.
				requestRelay.Method = context.Request.HttpMethod;
				requestRelay.UserAgent = context.Request.UserAgent;

				//Only set the body stream if the HTTP method is not GET
				//Relaying message-body for GET requests is a protocol violation. 
				if (context.Request.HttpMethod != "GET")
					context.Request.InputStream.CopyTo(requestRelay.GetRequestStream());

				//Wait for a response from the slave.
				Log.Trace($"Relaying request for {context.Request.Url.LocalPath} to {slaveAddress}");
				requestRelay.BeginGetResponse(Respond, new RequestState(requestRelay, context));
			}
		}

		private static int serverIndex;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		public static IPAddress GetBestSlave()
		{
			ICollection<ServerProfile> servers = ServerProfile.KnownServers.Values;

			//If the server list is empty, we're still starting up. Wait until startup finishes.
			while (ServerProfile.KnownServers.Count == 0)
				Thread.Sleep(10);

			// Increment the server index and wrap back to 0 when the index reaches servers.Count
			serverIndex = (serverIndex + 1) % servers.Count;

			return servers.ElementAt(serverIndex).Address;
		}

		/// <summary>
		/// Respond to an incoming HTTP request
		/// </summary>
		/// <param name="result"></param>
		private static void Respond(IAsyncResult result)
		{
			var data = (RequestState)result.AsyncState;

			//Attempt to retrieve the slave's response to this request.
			HttpWebResponse workerResponse;
			try
			{
				Log.Trace("Received websocket request.");
				workerResponse = (HttpWebResponse)data.WebRequest.EndGetResponse(result);
			}
			catch (WebException e)
			{
				//We don't care about exceptions here; they need to be transferred to the client.
				workerResponse = e.Response as HttpWebResponse;
			}

			HttpListenerResponse response = data.Context.Response;


			//If the worker didn't send a response for some reason, send an InternalServerError.
			if (workerResponse == null)
			{
				//How did we get here?
				Log.Error($"Slave {data.WebRequest.Address.Host} did not respond in time.");

				response.StatusCode = (int)HttpStatusCode.InternalServerError;
				response.OutputStream.Close();
				return;
			}

			//Transfer worker response headers, status code.
			response.Headers = workerResponse.Headers;
			response.StatusCode = (int)workerResponse.StatusCode;
			response.StatusDescription = workerResponse.StatusDescription;

			using Stream outStream = workerResponse.GetResponseStream();
			outStream.CopyTo(response.OutputStream);

			//Dispose the response, transmitting it to the client.
			try
			{
				response.OutputStream.Close();
			}
			catch (Exception) { }
			workerResponse.Dispose();
		}

		/// <summary>
		/// Record class, containing information about a request.
		/// </summary>
		// TODO: Convert into proper record class once C# 9.0 is (assuming the proposal is accepted)
		// See https://github.com/dotnet/csharplang/blob/master/proposals/records.md
		public struct RequestState
		{
			public readonly HttpWebRequest WebRequest;
			public readonly HttpListenerContext Context;

			public RequestState(HttpWebRequest request, HttpListenerContext context)
			{
				WebRequest = request;
				Context = context;
			}
		}
	}
}