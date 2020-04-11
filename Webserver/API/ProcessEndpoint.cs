using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

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

		/// <summary>
		/// Processes an incoming request to an endpoint.
		/// </summary>
		/// <param name="Context">The ContextProvider representing the request</param>
		public static void ProcessEndpoint(ContextProvider Context)
		{
			RequestProvider request = Context.Request;
			ResponseProvider response = Context.Response;

			//Check if the requested endpoint exists. If it doesn't, send a 404.
			Type endpointType = (from E in Endpoints where ("/api/" + E.GetCustomAttribute<RouteAttribute>()?.Route).ToLower() == request.Url.LocalPath.ToLower() select E).FirstOrDefault();
			if (endpointType == null)
			{
				response.Send(HttpStatusCode.NotFound);
				return;
			}

			//Create a new instance of the endpoint
			var endpoint = (APIEndpoint)Activator.CreateInstance(endpointType);
			endpoint.Context = Context;

			//TODO: Set headers for CORS support
			/* List<string> AllowedMethods = (from MethodInfo M in EPType.GetMethods() where M.DeclaringType == EPType select M.Name).ToList();
			 * AllowedMethods.ForEach(Console.WriteLine);
			 */

			//Get the required endpoint method
			MethodInfo method = endpointType.GetMethod(request.HttpMethod.ToString());

			//TODO: Permission check

			//Check content type if necessary
			ContentTypeAttribute contentTypeAttribute = method.GetCustomAttribute<ContentTypeAttribute>();
			if (contentTypeAttribute != null)
			{
				//If the content type doesn't match, send an Unsupported Media Type status code and cancel.
				if (contentTypeAttribute.Type != request.ContentType)
				{
					response.Send(HttpStatusCode.UnsupportedMediaType);
					return;
				}

				//Additional parsing for content types, if necessary.
				switch (contentTypeAttribute.Type)
				{
					case "application/json":
						string rawJSON = new StreamReader(request.InputStream, request.ContentEncoding).ReadToEnd();
						try
						{
							endpoint.Data = JObject.Parse(rawJSON);
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

			//Invoke the method. If this fails for whatever reason, return a 500 Internal Server Error.
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
