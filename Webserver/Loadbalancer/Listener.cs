using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Webserver.LoadBalancer {
	class Listener {

		public static void Listen() {
			HttpListener Listener = new HttpListener();
			Listener.Prefixes.Add("http://localhost:80/");
			Listener.Start();

			HttpClient Client = new HttpClient();

			while (true) {
				HttpListenerContext Context = Listener.GetContext();

				string URL = GetBestSlave();
				HttpWebRequest RelayRequest = (HttpWebRequest)WebRequest.Create(URL);
				RelayRequest.UserAgent = Context.Request.UserAgent;

				RelayRequest.BeginGetResponse(Respond, new RequestState(RelayRequest, Context));
				
			}
		}

		private static int ServerIterator;
		private static string GetBestSlave(){
			List<ServerProfile> Servers = new List<ServerProfile>(Balancer.Servers.Values);
			if(ServerIterator + 1 == Servers.Count){
				ServerIterator = 0;
			} else {
				ServerIterator++;
			}
			ServerProfile Server = Servers[ServerIterator];
			return string.Format("http://{0}:{1}/", Server.Endpoint.Address.ToString(), Balancer.HttpPort);
		}

		private static void Respond(IAsyncResult Result) {
			RequestState Data = (RequestState)Result.AsyncState;

			using HttpWebResponse WorkerResponse = (HttpWebResponse)Data.WebRequest.EndGetResponse(Result);
			using Stream WorkerResponseStream = WorkerResponse.GetResponseStream();

			HttpListenerResponse Response = Data.Context.Response;
			WorkerResponseStream.CopyTo(Response.OutputStream);
			try {
				Response.OutputStream.Close();
			} catch (Exception) {}
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
