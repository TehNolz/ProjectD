using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Webserver.Webserver
{
	/// <summary>
	/// Class for mocking HttpListenerContexts. Used for unit testing.
	/// </summary>
	public class ContextProvider
	{
		/// <inheritdoc cref="HttpListenerContext.Request"/>
		public RequestProvider Request;
		/// <inheritdoc cref="HttpListenerContext.Response"/>
		public ResponseProvider Response;
		/// <summary>
		/// Manually create a new ContextProvider by combining a Request- and ResponseProvider.
		/// Used to create mock requests for unit testing.
		/// </summary>
		/// <param name="request">The RequestProvider that will represent the incoming request.</param>
		/// <param name="response">The ResponseProvider that will represent the outgoing response.</param>
		public ContextProvider(RequestProvider request, ResponseProvider response)
		{
			Request = request;
			Response = response;
		}

		/// <summary>
		/// Convert a HttpListenerContext into a ContextProvider.
		/// </summary>
		/// <param name="context"></param>
		public ContextProvider(HttpListenerContext context)
		{
			Request = new RequestProvider(context.Request);
			Response = new ResponseProvider(context.Response);
		}
	}

	/// <summary>
	/// Class for mocking HttpListenerRequests. Used for unit testing.
	/// </summary>
	public class RequestProvider
	{
		/// <summary>
		/// The HttpListenerRequest that represents the incoming request. Null when the RequestProvider was created as part of a unit test.
		/// </summary>
		private readonly HttpListenerRequest Request;

		#region Uri
		private Uri _Url;
		/// <inheritdoc cref="HttpListenerRequest.Url"/>
		public Uri Url
		{
			get => Request == null ? _Url : Request.Url;
			private set => _Url = value;
		}
		#endregion

		#region RemoteEndPoint
		private IPEndPoint _RemoteEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.RemoteEndPoint"/>
		public IPEndPoint RemoteEndPoint
		{
			get => Request == null ? _RemoteEndPoint : Request.RemoteEndPoint;
			private set => _RemoteEndPoint = value;
		}
		#endregion

		#region LocalEndPoint
		private IPEndPoint _LocalEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.LocalEndPoint"/>
		public IPEndPoint LocalEndPoint
		{
			get => Request == null ? _LocalEndPoint : Request.LocalEndPoint;
			private set => _LocalEndPoint = value;
		}
		#endregion

		#region Params
		/// <summary>
		/// The QueryString of the request, converted into a dictionary for ease of access.
		/// </summary>
		public Dictionary<string, List<string>> Params;
		#endregion

		#region HttpMethod
		private HttpMethod _HttpMethod;
		/// <inheritdoc cref="HttpListenerRequest.HttpMethod"/>
		public HttpMethod HttpMethod
		{
			get => Request == null ? _HttpMethod : Enum.Parse<HttpMethod>(Request.HttpMethod);
			private set => _HttpMethod = value;
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <inheritdoc cref="HttpListenerRequest.ContentType"/>
		public string ContentType
		{
			get => Request == null ? _ContentType : Request.ContentType;
			private set => _ContentType = value;
		}
		#endregion

		#region InputStream
		private Stream _InputStream;
		/// <inheritdoc cref="HttpListenerRequest.InputStream"/>
		public Stream InputStream
		{
			get => Request == null ? _InputStream : Request.InputStream;
			private set => _InputStream = value;
		}
		#endregion

		#region ContentEncoding
		private Encoding _ContentEncoding;
		/// <inheritdoc cref="HttpListenerRequest.ContentEncoding"/>
		public Encoding ContentEncoding
		{
			get => Request == null ? _ContentEncoding : Request.ContentEncoding;
			private set => _ContentEncoding = value;
		}
		#endregion

		/// <summary>
		/// Convert a HttpListenerRequest into a RequestProvider
		/// </summary>
		/// <param name="request"></param>
		public RequestProvider(HttpListenerRequest request)
		{
			Request = request;
			Params = Utils.NameValueToDict(request.QueryString);
		}
	}

	/// <summary>
	/// Class for mocking HttpListenerResponses. Used for unit testing.
	/// </summary>
	public class ResponseProvider
	{
		private readonly HttpListenerResponse Response;
		/// <summary>
		/// Convert a HttpListenerResponse into a ResponseProvider
		/// </summary>
		/// <param name="response"></param>
		public ResponseProvider(HttpListenerResponse response)
		{
			Response = response;
		}

		#region StatusCode
		public HttpStatusCode _StatusCode;
		/// <inheritdoc cref="HttpListenerResponse.StatusCode"/>
		public HttpStatusCode StatusCode
		{
			get => Response == null ? _StatusCode : (HttpStatusCode)Response.StatusCode;
			set
			{
				if (Response != null)
				{
					Response.StatusCode = (int)value;
				}
				_StatusCode = value;
			}
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <inheritdoc cref="HttpListenerResponse.ContentType"/>
		public string ContentType
		{
			get => Response == null ? _ContentType : Response.ContentType;
			set
			{
				if (Response != null)
				{
					Response.ContentType = value;
				}
				_ContentType = value;
			}
		}
		#endregion

		#region Redirect
		private string _Redirect;
		/// <summary>
		/// Gets or sets the URL that the client will be redirected to
		/// </summary>
		public string Redirect
		{
			get => _Redirect;
			set
			{
				Response?.Redirect(value);
				_Redirect = value;
			}
		}
		#endregion

		/// <summary>
		/// The data sent to the client.
		/// </summary>
		public byte[] Data { get; private set; }

		#region Send
		/// <summary>
		/// Sends a status code to the client.
		/// </summary>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(HttpStatusCode statusCode = HttpStatusCode.OK) => Send(Array.Empty<byte>(), statusCode, "text/plain");
		/// <summary>
		/// Sends JSON data to the client.
		/// </summary>
		/// <param name="data">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(JObject data, HttpStatusCode statusCode = HttpStatusCode.OK) => Send(data.ToString(), statusCode, "application/json");

		/// <summary>
		/// Sends a string to the client.
		/// </summary>
		/// <param name="data">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="contentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(string data, HttpStatusCode statusCode, string contentType = "text/html") => Send(Encoding.UTF8.GetBytes(data), statusCode, contentType);

		/// <summary>
		/// Sends a byte array to the client.
		/// </summary>
		/// <param name="data">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="contentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(byte[] data, HttpStatusCode statusCode = HttpStatusCode.OK, string contentType = "text/html")
		{
			if (data == null)
				data = Array.Empty<byte>();

			Data = data;
			StatusCode = statusCode;
			ContentType = contentType;

			if (Response != null)
			{
				try
				{
					Response.OutputStream.Write(data, 0, data.Length);
					Response.OutputStream.Close();
				}
				catch (HttpListenerException e)
				{
					Console.WriteLine("Failed to send data to host: " + e.Message);
				}
			}
		}
		#endregion
	}

	/// <summary>
	/// Enum of HTTP Methods
	/// </summary>
	// Whose idea was it to return these as a string? Enums are way more convinient.
	public enum HttpMethod
	{
		GET,
		HEAD,
		POST,
		PUT,
		DELETE,
		CONNECT,
		OPTIONS,
		TRACE,
		PATCH
	}
}
