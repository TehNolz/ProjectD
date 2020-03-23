using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Webserver.API;
using Webserver.LoadBalancer;

namespace Webserver.Webserver {
	class RequestWorker {
		public BlockingCollection<ContextProvider> Queue;
		private readonly bool Debug = false;
		/// <summary>
		/// Create a new RequestWorker, which processes incoming HTTP requests.
		/// </summary>
		/// <param name="Queue">A BlockingCollection where new requests will be placed.</param>
		/// <param name="Debug">Debug mode. If true, the RequestWorker will automatically shutdown once all requests have been processed. Used for unit testing.</param>
		public RequestWorker (BlockingCollection<ContextProvider> Queue, bool Debug = false){
			this.Queue = Queue;
			this.Debug = Debug;
		}

		/// <summary>
		/// Start this RequestWorker. Should be run in its own thread.
		/// </summary>
		public void Run(){
			do {
				ContextProvider Context = Queue.Take();
				RequestProvider Request = Context.Request;
				ResponseProvider Response = Context.Response;

				//Block requests that weren't sent through the load balancer.
				//TODO: Actually make this work lol. RemoteEndPoint is incorrect somehow
				/* if (Request.RemoteEndPoint.Address.ToString() != Balancer.MasterEndpoint.Address.ToString()){
					Console.WriteLine("Refused request: Direct access attempt blocked.");
					Response.Send(HttpStatusCode.Forbidden);
					continue;
				}*/

				//Parse redirects
				string URL = Redirects.Resolve(Request.Url.LocalPath.ToLower());
				if (URL == null) {
					Console.WriteLine("Couldn't resolve URL; infinite redirection loop. URL: " + Request.Url.LocalPath.ToLower());
					continue;
				}
				//Remove trailing /
				if (URL.EndsWith('/') && URL.Length > 1) URL = URL.Remove(URL.Length - 1);

				//Redirect if necessary
				if (URL != Request.Url.LocalPath.ToLower()) {
					Console.WriteLine("Request redirected to " + URL);
					Response.Redirect = URL;
					Response.Send(HttpStatusCode.PermanentRedirect);
					continue;
				}

				//If the
				if(URL.StartsWith("/api")){
					APIEndpoint.ProcessEndpoint(Context);
				} else {
					Resource.ProcessResource(Context);
				}

			//If Debug mode is enabled and the queue is empty, stop the worker.
			} while (!Debug || Queue.Count != 0);	
		}
	}
}
