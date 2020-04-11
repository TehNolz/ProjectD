using System;
using System.Collections.Generic;
using System.Net;

using Webserver.Webserver;

namespace Webserver.API
{
	public abstract partial class APIEndpoint
	{
		#region Attributes
		/// <summary>
		/// Defines this endpoint's URL.
		/// </summary>
		[AttributeUsage(AttributeTargets.Class)]
		public class RouteAttribute : Attribute
		{
			public string Route { get; private set; }
			public RouteAttribute(string Route)
			{
				this.Route = Route;
			}
		}

		/// <summary>
		/// TODO: Implement permission system
		/// </summary>
		public class PermissionAttribute : Attribute { }

		/// <summary>
		/// Defines the content type that this endpoint method is expecting. Depending on the content type, the message body
		/// will have been automatically converted into a usable format before the method is called.
		/// Eg, if the ContentType is application/json, this property will contain the request body in the form of a JObject.
		/// </summary>
		[AttributeUsage(AttributeTargets.Method)]
		public class ContentTypeAttribute : Attribute
		{
			public string Type { get; private set; }
			public ContentTypeAttribute(string Type)
			{
				this.Type = Type;
			}
		}
		#endregion

		#region Methods
		/// <summary>
		/// The GET method requests a representation of the specified resource. Requests using GET should only retrieve data.
		/// </summary>
		public virtual void GET() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The HEAD method asks for a response identical to that of a GET request, but without the response body.
		/// </summary>
		public virtual void HEAD() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The POST method is used to submit an entity to the specified resource, often causing a change in state or side effects on the server.
		/// </summary>
		public virtual void POST() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The PUT method replaces all current representations of the target resource with the request payload.
		/// </summary>
		public virtual void PUT() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The DELETE method deletes the specified resource.
		/// </summary>
		public virtual void DELETE() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The CONNECT method establishes a tunnel to the server identified by the target resource.
		/// </summary>
		public virtual void CONNECT() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The TRACE method performs a message loop-back test along the path to the target resource.
		/// </summary>
		public virtual void TRACE() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The PATCH method is used to apply partial modifications to a resource.
		/// </summary>
		public virtual void PATCH() => Response.Send(HttpStatusCode.MethodNotAllowed);
		/// <summary>
		/// The OPTIONS method is used to describe the communication options for the target resource.
		/// </summary>
		public void OPTIONS() =>
			//TODO: Implement CORS support
			Response.Send(HttpStatusCode.NoContent);
		#endregion

		/// <summary>
		/// The ContextProvider that represents the communication between client and server.
		/// </summary>
		public ContextProvider Context { get; set; }
		/// <inheritdoc cref="ContextProvider.Request"/>
		public RequestProvider Request => Context.Request;
		/// <inheritdoc cref="ContextProvider.Response"/>
		public ResponseProvider Response => Context.Response;
		/// <inheritdoc cref="RequestProvider.Params"/>
		public Dictionary<string, List<string>> Params => Context.Request.Params;
		/// <summary>
		/// The data from the request body. If necessary, this data will have already been parsed into a usable type, as configured by the ContentType attribute.
		/// Eg, if the ContentType is application/json, this property will contain the request body in the form of a JObject.
		/// </summary>
		public dynamic Data { get; set; }
	}
}
