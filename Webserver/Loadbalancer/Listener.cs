using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace Webserver.LoadBalancer {
	class Listener {

		/// <summary>
		/// Listen for incoming HTTP requests and relay them to slave servers
		/// </summary>
		public static void Listen(string Prefix) {
			Console.WriteLine("Load Balancer Listener now listening on {0}", Prefix);
			HttpListener Listener = new HttpListener();
			Listener.Prefixes.Add(Prefix);
			Listener.Prefixes.Add("http://localhost:80/");
			Listener.Start();

			while (true) {
				HttpListenerContext Context = Listener.GetContext();
				string URL = GetBestSlave()+Context.Request.Url.LocalPath;
				HttpWebRequest RelayRequest = (HttpWebRequest)WebRequest.Create(URL);
				RelayRequest.UserAgent = Context.Request.UserAgent;

				RelayRequest.BeginGetResponse(Respond, new RequestState(RelayRequest, Context));
				
			}
		}

		private static int ServerIterator;
		/// <summary>
		/// Find the best slave to relay incoming requests to.
		/// </summary>
		/// <returns>The URL of the chosen slave</returns>
		private static string GetBestSlave(){
			List<ServerProfile> Servers = new List<ServerProfile>(Balancer.Servers.Values);
			if(ServerIterator + 1 == Servers.Count){
				ServerIterator = 0;
			} else {
				ServerIterator++;
			}
			ServerProfile Server = Servers[ServerIterator];
			return string.Format("http://{0}:{1}", Server.Endpoint.Address.ToString(), Balancer.HttpPort);
		}

		/// <summary>
		/// Respond to an incoming HTTP request
		/// </summary>
		/// <param name="Result"></param>
		private static void Respond(IAsyncResult Result) {
			RequestState Data = (RequestState)Result.AsyncState;

			HttpWebResponse WorkerResponse;
			try {
				WorkerResponse = (HttpWebResponse)Data.WebRequest.EndGetResponse(Result);
			} catch (WebException e){
				WorkerResponse = e.Response as HttpWebResponse;
			}

			HttpListenerResponse Response = Data.Context.Response;
			Response.Headers = WorkerResponse.Headers;
			Response.StatusCode = (int)WorkerResponse.StatusCode;
			
			using Stream WorkerResponseStream = WorkerResponse.GetResponseStream();
			WorkerResponseStream.CopyTo(Response.OutputStream);

			try {
				Response.OutputStream.Close();
			} catch (Exception) {}
			WorkerResponse.Dispose();
		}

		public class RequestState {
			public readonly HttpWebRequest WebRequest;
			public readonly HttpListenerContext Context;

			public RequestState(HttpWebRequest Request, HttpListenerContext Context) {
				WebRequest = Request;
				this.Context = Context;
			}
		}
	}
}
