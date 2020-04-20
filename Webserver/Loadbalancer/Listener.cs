using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

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
				string slaveAddress = GetBestSlave();
				string URL = slaveAddress + context.Request.Url.LocalPath;
				Log.Trace($"Relaying request for {URL} to {slaveAddress}");

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
				requestRelay.BeginGetResponse(Respond, new RequestState(requestRelay, context));
			}
		}

		private static int serverIndex;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		public static string GetBestSlave()
		{
			ICollection<ServerProfile> servers = ServerProfile.KnownServers.Values;

			//If the server list is empty, we're still starting up. Wait until startup finishes.
			while (ServerProfile.KnownServers.Count == 0)
				Thread.Sleep(10);

			// Increment the server index and wrap back to 0 when the index reaches servers.Count
			serverIndex = (serverIndex + 1) % servers.Count;

			return $"http://{servers.ElementAt(serverIndex).Address}:{BalancerConfig.HttpRelayPort}";
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
				workerResponse = (HttpWebResponse)data.WebRequest.EndGetResponse(result);
			}
			catch (WebException e)
			{
				//We don't care about exceptions here; they need to be transferred to the client.
				workerResponse = e.Response as HttpWebResponse;
			}

			HttpListenerResponse response = data.Context.Response;
			response.Headers = workerResponse.Headers;
			response.StatusCode = (int)workerResponse.StatusCode;
			response.StatusDescription = workerResponse.StatusDescription;

			using Stream outStream = workerResponse.GetResponseStream();
			outStream.CopyTo(response.OutputStream);

			try
			{
				response.OutputStream.Close();
			}
			catch (Exception) { }

			//Dispose the response, transmitting it to the client.
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