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
		/// <inheritdoc cref="HttpListenerContext.Request"/>
		public RequestProvider Request;
		/// <inheritdoc cref="HttpListenerContext.Response"/>
		public ResponseProvider Response;
		/// <summary>
		/// Manually create a new ContextProvider by combining a Request- and ResponseProvider.
		/// Used to create mock requests for unit testing.
		/// </summary>
		/// <param name="Request">The RequestProvider that will represent the incoming request.</param>
		/// <param name="Response">The ResponseProvider that will represent the outgoing response.</param>
		public ContextProvider(RequestProvider Request, ResponseProvider Response) {
			this.Request = Request;
			this.Response = Response;
		}

		/// <summary>
		/// Convert a HttpListenerContext into a ContextProvider.
		/// </summary>
		/// <param name="Context"></param>
		public ContextProvider(HttpListenerContext Context) {
			this.Request = new RequestProvider(Context.Request);
			this.Response = new ResponseProvider(Context.Response);
		}
	}

	/// <summary>
	/// Class for mocking HttpListenerRequests. Used for unit testing.
	/// </summary>
	public class RequestProvider {
		/// <summary>
		/// The HttpListenerRequest that represents the incoming request. Null when the RequestProvider was created as part of a unit test.
		/// </summary>
		private readonly HttpListenerRequest Request;

		#region Uri
		private Uri _Url;
		/// <inheritdoc cref="HttpListenerRequest.Url"/>
		public Uri Url {
			get => this.Request == null ? this._Url : this.Request.Url;
			private set => this._Url = value;
		}
		#endregion

		#region RemoteEndPoint
		private IPEndPoint _RemoteEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.RemoteEndPoint"/>
		public IPEndPoint RemoteEndPoint {
			get => this.Request == null ? this._RemoteEndPoint : this.Request.RemoteEndPoint;
			private set => this._RemoteEndPoint = value;
		}
		#endregion

		#region LocalEndPoint
		private IPEndPoint _LocalEndPoint;
		/// <inheritdoc cref="HttpListenerRequest.LocalEndPoint"/>
		public IPEndPoint LocalEndPoint {
			get => this.Request == null ? this._LocalEndPoint : this.Request.LocalEndPoint;
			private set => this._LocalEndPoint = value;
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
		public HttpMethod HttpMethod {
			get => this.Request == null ? this._HttpMethod : Enum.Parse<HttpMethod>(this.Request.HttpMethod);
			private set => this._HttpMethod = value;
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <inheritdoc cref="HttpListenerRequest.ContentType"/>
		public string ContentType {
			get => this.Request == null ? this._ContentType : this.Request.ContentType;
			private set => this._ContentType = value;
		}
		#endregion

		#region InputStream
		private Stream _InputStream;
		/// <inheritdoc cref="HttpListenerRequest.InputStream"/>
		public Stream InputStream {
			get => this.Request == null ? this._InputStream : this.Request.InputStream;
			private set => this._InputStream = value;
		}
		#endregion

		#region ContentEncoding
		private Encoding _ContentEncoding;
		/// <inheritdoc cref="HttpListenerRequest.ContentEncoding"/>
		public Encoding ContentEncoding {
			get => this.Request == null ? this._ContentEncoding : this.Request.ContentEncoding;
			private set => this._ContentEncoding = value;
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
		/// <inheritdoc cref="HttpListenerResponse.StatusCode"/>
		public HttpStatusCode StatusCode {
			get => this.Response == null ? this._StatusCode : (HttpStatusCode)this.Response.StatusCode;
			set {
				if(this.Response != null) {
					this.Response.StatusCode = (int)value;
				}
				this._StatusCode = value;
			}
		}
		#endregion

		#region ContentType
		private string _ContentType;
		/// <inheritdoc cref="HttpListenerResponse.ContentType"/>
		public string ContentType {
			get => this.Response == null ? this._ContentType : this.Response.ContentType;
			set {
				if(this.Response != null) {
					this.Response.ContentType = value;
				}
				this._ContentType = value;
			}
		}
		#endregion

		#region Redirect
		private string _Redirect;
		/// <summary>
		/// Gets or sets the URL that the client will be redirected to
		/// </summary>
		public string Redirect {
			get => this._Redirect;
			set {
				this.Response?.Redirect(value);
				this._Redirect = value;
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
			if(Data == null)
				Data = Array.Empty<byte>();

			this.Data = Data;
			this.StatusCode = StatusCode;
			this.ContentType = ContentType;

			if(this.Response != null) {
				try {
					this.Response.OutputStream.Write(Data, 0, Data.Length);
					this.Response.OutputStream.Close();
				} catch(HttpListenerException e) {
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
