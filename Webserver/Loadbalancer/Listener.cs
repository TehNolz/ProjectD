using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Webserver.LoadBalancer {
	public static class Listener {
		/// <summary>
		/// Listen for incoming HTTP requests and relay them to slave servers.
		/// This is the main entry point for all HTTP traffic in the system. The clients connect to- and will receive answers from this listener.
		/// </summary>
		public static void Listen() {
			Console.WriteLine("Load Balancer Listener now listening on http://{0}:{1}/", Networking.LocalEndPoint.Address, BalancerConfig.HttpPort);

			//Create a new HttpListener
			HttpListener Listener = new HttpListener();
			Listener.Prefixes.Add(string.Format("http://{0}:{1}/", Networking.LocalEndPoint.Address, BalancerConfig.HttpPort));
			Listener.Prefixes.Add(string.Format("http://localhost:{0}/", BalancerConfig.HttpPort));
			Listener.Start();

			// Main loop. Accepts incoming requests, then relays them to the Distributors running on each server.
			while(true) {
				//Accept the request
				HttpListenerContext Context = Listener.GetContext();
				string URL = GetBestSlave() + Context.Request.Url.LocalPath;

				//Relay to a Distributor
				HttpWebRequest RelayRequest = (HttpWebRequest)WebRequest.Create(URL);
				RelayRequest.UserAgent = Context.Request.UserAgent;
				RelayRequest.BeginGetResponse(Respond, new RequestState(RelayRequest, Context));

			}
		}

		/// <summary>
		/// Respond to an incoming HTTP request
		/// </summary>
		/// <param name="Result"></param>
		private static void Respond(IAsyncResult Result) {
			RequestState Data = (RequestState)Result.AsyncState;

			//Get the response from the server that processed the request.
			//We have to ignore whatever WebException we get, because someone decided to throw an exception when a 4xx or 5xx status code is received. They're an idiot.
			HttpWebResponse WorkerResponse;
			try {
				WorkerResponse = (HttpWebResponse)Data.WebRequest.EndGetResponse(Result);
			} catch(WebException e) {
				WorkerResponse = e.Response as HttpWebResponse;
				if(WorkerResponse == null)
					throw;
			}

			//Relay the response to the client.
			HttpListenerResponse Response = Data.Context.Response;
			Response.Headers = WorkerResponse.Headers;
			Response.StatusCode = (int)WorkerResponse.StatusCode;
			using Stream WorkerResponseStream = WorkerResponse.GetResponseStream();
			WorkerResponseStream.CopyTo(Response.OutputStream);
			//If an exception is thrown while sending the message, it means the client aborted the connection before
			//a response could be send. There's nothing we can do about it.
			try {
				Response.OutputStream.Close();
			} catch(Exception) { }
			WorkerResponse.Dispose();
		}


		private static int ServerIterator;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		private static string GetBestSlave() {
			//Cycle to the next server
			//TODO: Implement a better load balancing algorithm.
			List<ServerProfile> Servers = new List<ServerProfile>(Balancer.Servers.Values);
			if(ServerIterator + 1 == Servers.Count) {
				ServerIterator = 0;
			} else {
				ServerIterator++;
			}
			ServerProfile Server = Servers[ServerIterator];
			return string.Format("http://{0}:{1}", Server.Endpoint.Address.ToString(), BalancerConfig.HttpRelayPort);
		}

		/// <summary>
		/// Record class for temporarily storing connection info about the request.
		/// </summary>
		public class RequestState {
			public readonly HttpWebRequest WebRequest;
			public readonly HttpListenerContext Context;

			public RequestState(HttpWebRequest Request, HttpListenerContext Context) {
				this.WebRequest = Request;
				this.Context = Context;
			}
		}
	}
}
