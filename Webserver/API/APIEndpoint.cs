using System;
using System.Collections.Generic;
using System.Text;
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
			public string Route { get; }
			public RouteAttribute(string route) => Route = route;
		}

		/// <summary>
		/// TODO: Implement permission system
		/// </summary>
		public class PermissionAttribute : Attribute { }

		public class ContentTypeAttribute : Attribute
		{
			public string Type { get; }
			public ContentTypeAttribute(string type) => Type = type;
		}
		#endregion

		#region Methods
		/// <summary>
		/// The GET method requests a representation of the specified resource. Requests using GET should only retrieve data.
		/// </summary>
		public virtual void GET() => throw new NotImplementedException();
		/// <summary>
		/// The HEAD method asks for a response identical to that of a GET request, but without the response body.
		/// </summary>
		public virtual void HEAD() => throw new NotImplementedException();
		/// <summary>
		/// The POST method is used to submit an entity to the specified resource, often causing a change in state or side effects on the server.
		/// </summary>
		public virtual void POST() => throw new NotImplementedException();
		/// <summary>
		/// The PUT method replaces all current representations of the target resource with the request payload.
		/// </summary>
		public virtual void PUT() => throw new NotImplementedException();
		/// <summary>
		/// The DELETE method deletes the specified resource.
		/// </summary>
		public virtual void DELETE() => throw new NotImplementedException();
		/// <summary>
		/// The CONNECT method establishes a tunnel to the server identified by the target resource.
		/// </summary>
		public virtual void CONNECT() => throw new NotImplementedException();
		/// <summary>
		/// The TRACE method performs a message loop-back test along the path to the target resource.
		/// </summary>
		public virtual void TRACE() => throw new NotImplementedException();
		/// <summary>
		/// The PATCH method is used to apply partial modifications to a resource.
		/// </summary>
		public virtual void PATCH() => throw new NotImplementedException();
		/// <summary>
		/// The OPTIONS method is used to describe the communication options for the target resource.
		/// </summary>
		public void OPTIONS()
		{

		}
		#endregion

		public ContextProvider Context { get; set; }
		/// <inheritdoc cref="ContextProvider.Request"/>
		public RequestProvider Request { get { return Context.Request; } }
		/// <inheritdoc cref="ContextProvider.Response"/>
		public ResponseProvider Response { get { return Context.Response; } }
		/// <inheritdoc cref="RequestProvider.Params"/>
		public Dictionary<string, List<string>> Params { get { return Context.Request.Params; } }
		/// <summary>
		/// The data from the request body. If necessary, this data will have already been parsed into a usable type, as configured by the ContentType attribute.
		/// Eg, if the ContentType is application/json, this property will contain the request body in the form of a JObject.
		/// </summary>
		public dynamic Data { get; set; }
	}
}
