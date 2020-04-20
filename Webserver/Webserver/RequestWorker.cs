using Database.SQLite;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;

using Webserver.API;
//using Webserver.Chat;
using Webserver.LoadBalancer;

namespace Webserver.Webserver
{
	public class RequestWorker : IDisposable
	{
		/// <summary>
		/// The global request queue. The distributor inserts requests, and the workers process them.
		/// </summary>
		public static BlockingCollection<ContextProvider> Queue;
		/// <summary>
		/// Debug mode. If true, the RequestWorker will automatically shutdown once all requests have been processed. Used for unit testing.
		/// </summary>
		private readonly bool Debug = false;
		/// <summary>
		/// The worker theead object.
		/// </summary>
		private readonly Thread Thread;
		/// <summary>
		/// This worker thread's database connection.
		/// </summary>
		private readonly SQLiteAdapter Database;

		/// <summary>
		/// Create a new RequestWorker, which processes incoming HTTP requests.
		/// </summary>
		/// <param name="queue">A BlockingCollection where new requests will be placed.</param>
		/// <param name="debug">Debug mode. If true, the RequestWorker will automatically shutdown once all requests have been processed. Used for unit testing.</param>
		public RequestWorker(SQLiteAdapter database, bool debug = false)
		{
			Database = database;
			Debug = debug;
			Thread = new Thread(Run) { Name = GetType().Name };
		}

		/// <summary>
		/// Launches a new <see cref="Thread"/> that calls <see cref="RequestWorker.Run"/>.
		/// </summary>
		public void Start() => Thread.Start();

		/// <inheritdoc cref="Thread.Join"/>
		public void Join() => Thread.Join();

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
				string url = Redirects.Resolve(request.Url.LocalPath.ToLower());
				if (url == null)
				{
					Console.WriteLine("Couldn't resolve URL; infinite redirection loop. URL: " + request.Url.LocalPath.ToLower());
					continue;
				}

				//Remove trailing /
				if (url.EndsWith('/') && url.Length > 1)
					url = url.Remove(url.Length - 1);

				//Redirect if necessary
				if (url != request.Url.LocalPath.ToLower())
				{
					Console.WriteLine("Request redirected to " + url);
					response.Redirect = url;
					response.Send(HttpStatusCode.PermanentRedirect);
					continue;
				}

				// If the url starts with /api, pass the request to the API Endpoints
				if (url.StartsWith("/api/")) // TODO: Remove hardcoded string
				{
					APIEndpoint.ProcessEndpoint(context, Database);
				}
				else
				{
					Resource.ProcessResource(context);
				}

				//If Debug mode is enabled and the queue is empty, stop the worker.
			} while (!Debug || Queue.Count != 0);
		}

		public void Dispose()
		{
			Database.Dispose();
		}
	}
}
