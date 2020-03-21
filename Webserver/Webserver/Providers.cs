using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Webserver.Webserver {
	/// <summary>
	/// Class for mocking HttpListenerContexts. Used for unit testing.
	/// </summary>
	public class ContextProvider {
		public RequestProvider Request;
		public ResponseProvider Response;
		public ContextProvider(RequestProvider Request, ResponseProvider Response) {
			this.Request = Request;
			this.Response = Response;
		}

		public ContextProvider(HttpListenerContext Context) {
			this.Request = new RequestProvider(Context.Request);
			this.Response = new ResponseProvider(Context.Response);
		}
	}

	/// <summary>
	/// Class for mocking HttpListenerRequests. Used for unit testing.
	/// </summary>
	public class RequestProvider {
		private readonly HttpListenerRequest Request;

		#region Uri
		private Uri _Url;
		/// <inheritdoc cref="HttpListenerRequest.Url"/>
		public Uri Url {
			get {
				return Request == null ? _Url : Request.Url;
			}
			private set {
				_Url = value;
			}
		}
		#endregion

		#region RemoteEndPoint
		private IPEndPoint _RemoteEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.RemoteEndPoint"/>
		public IPEndPoint RemoteEndPoint {
			get {
				return Request == null ? _RemoteEndPoint : Request.RemoteEndPoint;
			}
			private set {
				_RemoteEndPoint = value;
			}
		}
		#endregion

		#region Params
		/// <inheritdoc cref="HttpListenerRequest.QueryString"/>
		public Dictionary<string, List<string>> Params;
		#endregion

		#region HttpMethod
		private HttpMethod _HttpMethod;
		/// <inheritdoc cref="HttpListenerRequest.HttpMethod"/>
		public HttpMethod HttpMethod {
			get {
				return Request == null ? _HttpMethod : Enum.Parse<HttpMethod>(Request.HttpMethod);
			}
			private set {
				_HttpMethod = value;
			}
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <inheritdoc cref="HttpListenerRequest.ContentType"/>
		public string ContentType {
			get {
				return Request == null ? _ContentType : Request.ContentType;
			}
			private set {
				_ContentType = value;
			}
		}
		#endregion

		#region InputStream
		private Stream _InputStream;
		/// <inheritdoc cref="HttpListenerRequest.InputStream"/>
		public Stream InputStream {
			get {
				return Request == null ? _InputStream : Request.InputStream;
			}
			private set {
				_InputStream = value;
			}
		}
		#endregion

		#region ContentEncoding
		private Encoding _ContentEncoding;
		/// <inheritdoc cref="HttpListenerRequest.ContentEncoding"/>
		public Encoding ContentEncoding {
			get{
				return Request == null ? _ContentEncoding : Request.ContentEncoding;
			}
			private set{
				_ContentEncoding = value;
			}
		}
		#endregion

		/// <summary>
		/// Convert a HttpListenerRequest into a RequestProvider
		/// </summary>
		/// <param name="Request"></param>
		public RequestProvider(HttpListenerRequest Request) {
			this.Request = Request;
			this.Params = Utils.NameValueToDict(Request.QueryString);
		}
	}

	/// <summary>
	/// Class for mocking HttpListenerResponses. Used for unit testing.
	/// </summary>
	public class ResponseProvider {
		private readonly HttpListenerResponse Response;
		/// <summary>
		/// Convert a HttpListenerResponse into a ResponseProvider
		/// </summary>
		/// <param name="Response"></param>
		public ResponseProvider(HttpListenerResponse Response) => this.Response = Response;

		
		#region StatusCode
		public HttpStatusCode _StatusCode;
		/// <summary>
		/// Gets or sets the HTTP status code to be returned to the client.
		/// </summary>
		public HttpStatusCode StatusCode {
			get {
				if(Response == null){
					return _StatusCode;
				} else {
					return (HttpStatusCode)Response.StatusCode;
				}
			}
			set {
				if(Response != null){
					Response.StatusCode = (int)value;
				}
				_StatusCode = value;
			}
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <summary>
		/// Gets or sets the MIME type of the content returned.
		/// </summary>
		public string ContentType {
			get {
				return Response == null ? _ContentType : Response.ContentType;
			}
			set {
				if(Response != null){
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
		public string Redirect{
			get {
				return _Redirect;
			}
			set {
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
		/// <param name="StatusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(HttpStatusCode StatusCode = HttpStatusCode.OK) => Send(Array.Empty<byte>(), StatusCode, "text/plain");
		/// <summary>
		/// Sends JSON data to the client.
		/// </summary>
		/// <param name="Data">The data to be sent to the client.</param>
		/// <param name="StatusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		public void Send(JObject Data, HttpStatusCode StatusCode = HttpStatusCode.OK) => Send(Data.ToString(), StatusCode, "application/json");

		/// <summary>
		/// Sends a string to the client.
		/// </summary>
		/// <param name="Data">The data to be sent to the client.</param>
		/// <param name="StatusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="ContentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(string Data, HttpStatusCode StatusCode, string ContentType = "text/html") => Send(Encoding.UTF8.GetBytes(Data), StatusCode, ContentType);

		/// <summary>
		/// Sends a byte array to the client.
		/// </summary>
		/// <param name="Data">The data to be sent to the client.</param>
		/// <param name="StatusCode">The HttpStatusCode. Defaults to HttpStatusCode.OK (200)</param>
		/// <param name="ContentType">The ContentType of the response. Defaults to "text/html"</param>
		public void Send(byte[] Data, HttpStatusCode StatusCode = HttpStatusCode.OK, string ContentType = "text/html") {
			if (Data == null) Data = Array.Empty<byte>();
			this.Data = Data;
			this.StatusCode = StatusCode;
			this.ContentType = ContentType;

			if (Response != null) {
				try {
					Response.OutputStream.Write(Data, 0, Data.Length);
					Response.OutputStream.Close();
				} catch (HttpListenerException e) {
					Console.WriteLine("Failed to send data to host: " + e.Message);
				}
			}
		}
		#endregion
	}
	public enum HttpMethod {
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
