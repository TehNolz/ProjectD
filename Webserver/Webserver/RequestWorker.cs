using System;
using System.Collections.Concurrent;
using System.Net;

using Webserver.API;

namespace Webserver.Webserver
{
	class RequestWorker
	{
		public BlockingCollection<ContextProvider> Queue;
		private readonly bool Debug = false;
		/// <summary>
		/// Create a new RequestWorker, which processes incoming HTTP requests.
		/// </summary>
		/// <param name="queue">A BlockingCollection where new requests will be placed.</param>
		/// <param name="debug">Debug mode. If true, the RequestWorker will automatically shutdown once all requests have been processed. Used for unit testing.</param>
		public RequestWorker(BlockingCollection<ContextProvider> queue, bool debug = false)
		{
			Queue = queue;
			Debug = debug;
		}

		/// <summary>
		/// Start this RequestWorker. Should be run in its own thread.
		/// </summary>
		public void Run()
		{
			do
			{
				ContextProvider context = Queue.Take();
				RequestProvider request = context.Request;
				ResponseProvider response = context.Response;

				//Block requests that weren't sent through the load balancer.
				//TODO: Actually make this work lol. RemoteEndPoint is incorrect somehow
				/* if (Request.RemoteEndPoint.Address.ToString() != Balancer.MasterEndpoint.Address.ToString()){
					Console.WriteLine("Refused request: Direct access attempt blocked.");
					Response.Send(HttpStatusCode.Forbidden);
					continue;
				}*/

				//Parse redirects
				string URL = Redirects.Resolve(request.Url.LocalPath.ToLower());
				if (URL == null)
				{
					Console.WriteLine("Couldn't resolve URL; infinite redirection loop. URL: " + request.Url.LocalPath.ToLower());
					continue;
				}
				//Remove trailing /
				if (URL.EndsWith('/') && URL.Length > 1)
					URL = URL.Remove(URL.Length - 1);

				//Redirect if necessary
				if (URL != request.Url.LocalPath.ToLower())
				{
					Console.WriteLine("Request redirected to " + URL);
					response.Redirect = URL;
					response.Send(HttpStatusCode.PermanentRedirect);
					continue;
				}

				//If the URL starts with /api, assume that its a request for an API endpoint.
				if (URL.StartsWith("/api"))
				{
					APIEndpoint.ProcessEndpoint(context);
				}
				else
				{
					Resource.ProcessResource(context);
				}

				//If Debug mode is enabled and the queue is empty, stop the worker.
			} while (!Debug || Queue.Count != 0);
		}
	}
}
