using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Webserver.Webserver;

namespace Webserver.API
{
	public abstract partial class APIEndpoint
	{
		public static List<Type> Endpoints;
		/// <summary>
		/// Discover all existing endpoints.
		/// </summary>
		public static void DiscoverEndpoints() => Endpoints = (from T in Assembly.GetExecutingAssembly().GetTypes() where typeof(APIEndpoint).IsAssignableFrom(T) && !T.IsAbstract select T).ToList();

		public static void ProcessEndpoint(ContextProvider Context)
		{
			RequestProvider request = Context.Request;
			ResponseProvider response = Context.Response;

			//Check if the requested endpoint exists. If it doesn't, send a 404.
			var endpointType = (from e in Endpoints where "/api" + e.GetCustomAttribute<RouteAttribute>()?.Route == request.Url.LocalPath.ToLower() select e).FirstOrDefault();
			if (endpointType == null)
			{
				response.Send(HttpStatusCode.NotFound);
				return;
			}

			//Create a new instance of the endpoint
			APIEndpoint endpoint = (APIEndpoint)Activator.CreateInstance(endpointType);
			endpoint.Context = Context;

			//TODO: Set headers for CORS support
			/* List<string> AllowedMethods = (from MethodInfo M in EPType.GetMethods() where M.DeclaringType == EPType select M.Name).ToList();
			 * AllowedMethods.ForEach(Console.WriteLine);
			 */

			//Get the required endpoint method
			var method = endpointType.GetMethod(request.HttpMethod.ToString());

			//TODO: Permission check

			//Check content type if necessary
			var contentType = method.GetCustomAttribute<ContentTypeAttribute>();
			if (contentType != null)
			{
				//If the content type doesn't match, send an Unsupported Media Type status code and cancel.
				if (contentType.Type != request.ContentType)
				{
					response.Send(HttpStatusCode.UnsupportedMediaType);
					return;
				}

				//Additional parsing for content types, if necessary.
				switch (contentType.Type)
				{
					case "application/json":
						var reader = new StreamReader(request.InputStream, request.ContentEncoding);
						string json = reader.ReadToEnd();
						reader.Dispose();
						try
						{
							endpoint.Data = JObject.Parse(json);
						}
						catch (JsonReaderException)
						{
							Console.WriteLine("Received invalid request for endpoint {0}.{1}. Could not parse JSON", endpointType.Name, method.Name);
							response.Send(HttpStatusCode.BadRequest);
							return;
						}
						break;
				}
			}

			//Invoke the method
			try
			{
				method.Invoke(endpoint, null);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				response.Send(HttpStatusCode.InternalServerError);
			}
		}
	}
}
