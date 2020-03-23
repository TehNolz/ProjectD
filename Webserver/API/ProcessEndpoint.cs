using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Webserver.Webserver;

namespace Webserver.API {
	public abstract partial class APIEndpoint {
		public static List<Type> Endpoints;
		/// <summary>
		/// Discover all existing endpoints.
		/// </summary>
		public static void DiscoverEndpoints() => Endpoints = (from T in Assembly.GetExecutingAssembly().GetTypes() where typeof(APIEndpoint).IsAssignableFrom(T) && !T.IsAbstract select T).ToList();

		/// <summary>
		/// Processes an incoming request to an endpoint.
		/// </summary>
		/// <param name="Context">The ContextProvider representing the request</param>
		public static void ProcessEndpoint(ContextProvider Context) {
			RequestProvider Request = Context.Request;
			ResponseProvider Response = Context.Response;

			//Check if the requested endpoint exists. If it doesn't, send a 404.
			Type EndpointType = (from E in Endpoints where "/api/" + E.GetCustomAttribute<RouteAttribute>()?.Route == Request.Url.LocalPath.ToLower() select E).FirstOrDefault();
			if(EndpointType == null) {
				Response.Send(HttpStatusCode.NotFound);
				return;
			}

			//Create a new instance of the endpoint
			APIEndpoint Endpoint = (APIEndpoint)Activator.CreateInstance(EndpointType);
			Endpoint.Context = Context;

			//TODO: Set headers for CORS support
			/* List<string> AllowedMethods = (from MethodInfo M in EPType.GetMethods() where M.DeclaringType == EPType select M.Name).ToList();
			 * AllowedMethods.ForEach(Console.WriteLine);
			 */

			//Get the required endpoint method
			MethodInfo M = EndpointType.GetMethod(Request.HttpMethod.ToString());

			//TODO: Permission check

			//Check content type if necessary
			ContentTypeAttribute Attr = M.GetCustomAttribute<ContentTypeAttribute>();
			if(Attr != null) {
				//If the content type doesn't match, send an Unsupported Media Type status code and cancel.
				if(Attr.Type != Request.ContentType) {
					Response.Send(HttpStatusCode.UnsupportedMediaType);
					return;
				}

				//Additional parsing for content types, if necessary.
				switch(Attr.Type) {
					case "application/json":
						string RawJSON = new StreamReader(Request.InputStream, Request.ContentEncoding).ReadToEnd();
						try {
							Endpoint.Data = JObject.Parse(RawJSON);
						} catch(JsonReaderException) {
							Console.WriteLine("Received invalid request for endpoint {0}.{1}. Could not parse JSON", EndpointType.Name, M.Name);
							Response.Send(HttpStatusCode.BadRequest);
							return;
						}
						break;
				}
			}

			//Invoke the method. If this fails for whatever reason, return a 500 Internal Server Error.
			try {
				M.Invoke(Endpoint, null);
			} catch(Exception e) {
				Console.WriteLine(e);
				Response.Send(HttpStatusCode.InternalServerError);
			}
		}
	}
}
