using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

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
			Console.WriteLine("Load Balancer Listener now listening on {0}:{1}", address, port);

			//Main loop
			while (true)
			{
				//Get incoming requests
				HttpListenerContext context = listener.GetContext();
				string slaveAddress = GetBestSlave();
				string URL = slaveAddress + context.Request.Url.LocalPath;
				Console.WriteLine($"Relaying request for {URL} to {slaveAddress}");

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

		//TODO: Implement better load balancing algorithm.
		private static int ServerIndex;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		private static string GetBestSlave()
		{
			//Get the IP addresses of all servers, including ourselves.
			var allServers = (from S in ServerProfile.KnownServers.Keys select S).ToList();
			allServers.Add(Balancer.LocalAddress);

			//TODO: Implement a better load balancing algorithm. Roundabout works, but surely we can do something fancier!
			ServerIndex = Math.Clamp(ServerIndex, 0, allServers.Count - 1);
			ServerIndex++;
			if (ServerIndex == allServers.Count)
				ServerIndex = 0;

			//Return the URL of the slave.
			return string.Format("http://{0}:{1}", allServers[ServerIndex], BalancerConfig.HttpRelayPort);
		}

		/// <summary>
		/// Respond to an incoming HTTP request
		/// </summary>
		/// <param name="result"></param>
		private static void Respond(IAsyncResult result)
		{
			//Get the RequestState
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

			//Set the response headers, status code, status description
			HttpListenerResponse response = data.Context.Response;
			response.Headers = workerResponse.Headers;
			response.StatusCode = (int)workerResponse.StatusCode;
			response.StatusDescription = workerResponse.StatusDescription;

			//Set the output stream.
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