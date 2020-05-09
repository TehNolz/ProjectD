using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Webserver.Webserver
{
	/// <summary>
	/// Class for mocking <see cref="HttpListenerContext"/>s. Used for unit testing.
	/// </summary>
	public class ContextProvider
	{
		/// <inheritdoc cref="HttpListenerContext.Request"/>
		public RequestProvider Request;
		/// <inheritdoc cref="HttpListenerContext.Response"/>
		public ResponseProvider Response;
		/// <summary>
		/// The HttpListenerContext that was used to construct this ContextProvider. Null if this ContextProvider is a mock.
		/// </summary>
		private HttpListenerContext Context;

		/// <summary>
		/// Manually create a new ContextProvider by combining a Request- and ResponseProvider.
		/// Used to create mock requests for unit testing.
		/// </summary>
		/// <param name="request">The RequestProvider that will represent the incoming request.</param>
		/// <param name="response">The ResponseProvider that will represent the outgoing response.</param>
		public ContextProvider(RequestProvider request)
		{
			Request = request;
			Response = new ResponseProvider();
		}

		/// <summary>
		/// Convert a HttpListenerContext into a ContextProvider.
		/// </summary>
		/// <param name="context"></param>
		public ContextProvider(HttpListenerContext context)
		{
			Context = context;
			Request = new RequestProvider(context.Request);
			Response = new ResponseProvider(context.Response);
		}

		/// <inheritdoc cref="HttpListenerContext.AcceptWebSocketAsync(string)"/>
		public async Task<HttpListenerWebSocketContext> AcceptWebSocketAsync(string subProtocol) => await Context.AcceptWebSocketAsync(subProtocol);

	}

	/// <summary>
	/// Class for mocking <see cref="HttpListenerRequest"/>s. Used for unit testing.
	/// </summary>
	public class RequestProvider
	{

		/// <summary>
		/// The HttpListenerRequest that represents the incoming request. Null when the RequestProvider was created as part of a unit test.
		/// </summary>
		private readonly HttpListenerRequest Request;

		#region Uri
		private Uri _url;
		/// <inheritdoc cref="HttpListenerRequest.Url"/>
		public Uri Url
		{
			get => Request == null ? _url : Request.Url;
			set => _url = value;
		}
		#endregion

		#region RemoteEndPoint
		private IPEndPoint _remoteEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.RemoteEndPoint"/>
		public IPEndPoint RemoteEndPoint
		{
			get => Request == null ? _remoteEndPoint : Request.RemoteEndPoint;
			set => _remoteEndPoint = value;
		}
		#endregion

		#region LocalEndPoint
		private IPEndPoint _localEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.LocalEndPoint"/>
		public IPEndPoint LocalEndPoint
		{
			get => Request == null ? _localEndPoint : Request.LocalEndPoint;
			set => _localEndPoint = value;
		}
		#endregion

		#region Params
		/// <summary>
		/// The QueryString of the request, converted into a dictionary for ease of access.
		/// </summary>
		public Dictionary<string, List<string>> Params;
		#endregion

		#region HttpMethod
		private HttpMethod _httpMethod;
		/// <inheritdoc cref="HttpListenerRequest.HttpMethod"/>
		public HttpMethod HttpMethod
		{
			get => Request == null ? _httpMethod : Enum.Parse<HttpMethod>(Request.HttpMethod);
			set => _httpMethod = value;
		}
		#endregion

		#region ContentType
		private string _contentType;
		/// <inheritdoc cref="HttpListenerRequest.ContentType"/>
		public string ContentType
		{
			get => Request == null ? _contentType : Request.ContentType;
			set => _contentType = value;
		}
		#endregion

		#region InputStream
		private Stream _inputStream;
		/// <inheritdoc cref="HttpListenerRequest.InputStream"/>
		public Stream InputStream
		{
			get => Request == null ? _inputStream : Request.InputStream;
			set => _inputStream = value;
		}
		#endregion

		public bool _IsWebSocketRequest;
		/// <inheritdoc cref="HttpListenerRequest.IsWebSocketRequest"/>
		public bool IsWebSocketRequest
		{
			get => Request == null ? _IsWebSocketRequest : Request.IsWebSocketRequest;
			set => _IsWebSocketRequest = true;
		}

		#region ContentEncoding
		private Encoding _contentEncoding;
		/// <inheritdoc cref="HttpListenerRequest.ContentEncoding"/>
		public Encoding ContentEncoding
		{
			get => Request == null ? _contentEncoding : Request.ContentEncoding;
			set => _contentEncoding = value;
		}
		#endregion

		#region Cookies
		private CookieCollection _cookies;
		public CookieCollection Cookies
		{
			get => Request == null ? _cookies : Request.Cookies;
			set => _cookies = value;
		}
		#endregion Cookies

		/// <summary>
		/// Convert a HttpListenerRequest into a RequestProvider
		/// </summary>
		/// <param name="request"></param>
		public RequestProvider(HttpListenerRequest request)
		{
			Request = request;
			Params = Utils.NameValueToDict(request.QueryString);
		}

		/// <summary>
		/// Create 
		/// </summary>
		/// <param name="Url"></param>
		/// <param name="HttpMethod"></param>
		public RequestProvider(Uri Url, HttpMethod HttpMethod)
		{
			this.Url = Url;
			Params = Utils.NameValueToDict(HttpUtility.ParseQueryString(Url.Query));
			this.HttpMethod = HttpMethod;
			ContentEncoding = Encoding.UTF8;
			Cookies = new CookieCollection();
		}
	}

	/// <summary>
	/// Class for mocking <see cref="HttpListenerResponse"/>s. Used for unit testing.
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

		/// <summary>
		/// Create a new blank ResponseProvider for unit testing purposes.
		/// </summary>
		public ResponseProvider() { }

		#region StatusCode
		public HttpStatusCode _statusCode;
		/// <inheritdoc cref="HttpListenerResponse.StatusCode"/>
		public HttpStatusCode StatusCode
		{
			get => Response == null ? _statusCode : (HttpStatusCode)Response.StatusCode;
			set
			{
				if (Response != null)
				{
					Response.StatusCode = (int)value;
				}
				_statusCode = value;
			}
		}
		#endregion

		#region ContentType
		private string _contentType;
		/// <inheritdoc cref="HttpListenerResponse.ContentType"/>
		public string ContentType
		{
			get => Response == null ? _contentType : Response.ContentType;
			set
			{
				if (Response != null)
				{
					Response.ContentType = value;
				}
				_contentType = value;
			}
		}
		#endregion

		#region Redirect
		private string _redirect;
		/// <summary>
		/// Gets or sets the URL that the client will be redirected to
		/// </summary>
		public string Redirect
		{
			get => _redirect;
			set
			{
				Response?.Redirect(value);
				_redirect = value;
			}
		}
		#endregion

		/// <summary>
		/// The headers sent to the client.
		/// </summary>
		public readonly WebHeaderCollection Headers = new WebHeaderCollection();

		/// <summary>
		/// Append a new header.
		/// </summary>
		/// <param name="name">The name of the header</param>
		/// <param name="value">The value of the header</param>
		public void AppendHeader(string name, string value)
		{
			Response?.AppendHeader(name, value);
			Headers.Add(name, value);
		}

		/// <summary>
		/// The data that was sent to the client.
		/// </summary>
		public string Data { get; private set; }

		#region Send
		/// <summary>
		/// Sends a status code to the client.
		/// </summary>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(HttpStatusCode statusCode = HttpStatusCode.OK) => Send(Array.Empty<byte>(), statusCode, "text/plain");
		/// <summary>
		/// Sends JSON data to the client.
		/// </summary>
		/// <param name="json">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(JToken json, HttpStatusCode statusCode = HttpStatusCode.OK)
		{
			Data = json.ToString();
			Send(json.ToString(), statusCode, "application/json");
		}

		/// <summary>
		/// Sends a string to the client.
		/// </summary>
		/// <param name="text">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="contentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(string text, HttpStatusCode statusCode, string contentType = "text/plain")
		{
			Data = text;
			Send(Encoding.UTF8.GetBytes(text), statusCode, contentType);
		}

		/// <summary>
		/// Sends a byte array to the client.
		/// </summary>
		/// <param name="data">The data to be sent to the client.</param>
		/// <param name="statusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="contentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(byte[] data, HttpStatusCode statusCode = HttpStatusCode.OK, string contentType = "text/plain")
		{
			if (data == null)
				data = Array.Empty<byte>();

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
					Program.Log.Warning("Failed to send data to host: " + e.Message);
				}
			}
		}
		#endregion

		/// <summary>
		/// Send a cookie to the client.
		/// </summary>
		/// <param name="cookie">The Cookie object to send. Only the Name and Value fields will be used.</param>
		public void AddCookie(Cookie cookie) => AddCookie(cookie.Name, cookie.Value, (int)cookie.Expires.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds);

		/// <summary>
		/// Send a cookie to the client. Always use this function to add cookies, as the built-in functions don't work properly.
		/// </summary>
		/// <param name="name">The cookie's name</param>
		/// <param name="value">The cookie's value</param>
		public void AddCookie(string name, string value, long expire)
		{
			string cookieVal = name + "=" + value;

			if (expire < 0)
			{
				throw new ArgumentOutOfRangeException("Negative cookie expiration");
			}
			cookieVal += "; Max-Age=" + expire;

			//We manually set the cookie header instead of setting Response.Cookies because some twat decided that HTTPListener should use folded cookies, which every
			//major browser has no support for. Using folded cookies, we would be limited to only 1 cookie per response, because browsers would otherwise incorrectly
			//interpret the 2nd cookie's key and value to be part of the 1st cookie's value.
			AppendHeader("Set-Cookie", cookieVal);
		}
	}

	/// <summary>
	/// Enum of HTTP Methods
	/// </summary>
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
