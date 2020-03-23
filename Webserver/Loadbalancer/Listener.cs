using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace Webserver.LoadBalancer
{
	class Listener
	{
		/// <summary>
		/// Listen for incoming HTTP requests and relay them to slave servers
		/// </summary>
		public static void Listen(string address, int port)
		{
			var listener = new HttpListener();
			listener.Prefixes.Add($"http://{address}:{port}/");
			listener.Prefixes.Add($"http://localhost:{port}/");
			listener.Start();

			Console.WriteLine("Load Balancer Listener now listening on {0}:{1}", address, port);
			while (true)
			{
				var context = listener.GetContext();
				string URL = GetBestSlave() + context.Request.Url.LocalPath;
				
				var requestRelay = (HttpWebRequest)WebRequest.Create(URL);
				requestRelay.UserAgent = context.Request.UserAgent;

				requestRelay.BeginGetResponse(Respond, new RequestState(requestRelay, context));
			}
		}

		private static int serverIndex;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		private static string GetBestSlave()
		{
			var servers = Balancer.Servers.Values;
			// Increment the server index and wrap back to 0 when the index reaches servers.Count
			serverIndex = (serverIndex + 1) % servers.Count;
			return $"http://{servers.ElementAt(serverIndex).Endpoint.Address}:{Balancer.Port}";
		}

		/// <summary>
		/// Respond to an incoming HTTP request
		/// </summary>
		/// <param name="result"></param>
		private static void Respond(IAsyncResult result)
		{
			var data = (RequestState)result.AsyncState;

			HttpWebResponse workerResponse;
			try
			{
				workerResponse = (HttpWebResponse)data.WebRequest.EndGetResponse(result);
			}
			catch (WebException e)
			{
				workerResponse = e.Response as HttpWebResponse;
			}

			var response = data.Context.Response;
			response.Headers = workerResponse.Headers;
			response.StatusCode = (int)workerResponse.StatusCode;

			using var outStream = workerResponse.GetResponseStream();
			outStream.CopyTo(response.OutputStream);

			try
			{
				response.OutputStream.Close();
			}
			catch (Exception) { }

			workerResponse.Dispose();
		}

		public struct RequestState
		{
			public readonly HttpWebRequest WebRequest;
			public readonly HttpListenerContext Context;

			public RequestState(HttpWebRequest Request, HttpListenerContext Context)
			{
				WebRequest = Request;
				this.Context = Context;
			}
		}
	}
}
