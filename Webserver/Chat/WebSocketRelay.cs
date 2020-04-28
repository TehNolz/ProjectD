using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Webserver.Config;
using Webserver.LoadBalancer;
using Webserver.Webserver;

namespace Webserver.Chat
{
	/// <summary>
	/// Relay thread for websocket connections. Used by the load balancer to relay incoming websocket connections to slaves.
	/// </summary>
	class WebSocketRelay : IDisposable
	{
		/// <summary>
		/// All active websocket relays.
		/// </summary>
		public static List<WebSocketRelay> activeRelays = new List<WebSocketRelay>();

		/// <summary>
		/// The slave server this relay is connected to.
		/// </summary>
		public ServerProfile Slave;

		/// <summary>
		/// The ContextProvider representing the request.
		/// </summary>
		public ContextProvider Context;

		/// <summary>
		/// The WebSocketContext representing our connection to the client.
		/// </summary>
		public HttpListenerWebSocketContext Client;
		/// <summary>
		/// The WebSocketContext representing our connection to the slave.
		/// </summary>
		public ClientWebSocket SlaveConnection = new ClientWebSocket();

		/// <summary>
		/// Cancellation token for async requests.
		/// </summary>
		private CancellationTokenSource TokenSource { get; set; } = new CancellationTokenSource();

		/// <summary>
		/// Create a new relay thread for this request.
		/// </summary>
		/// <param name="Context">The HttpListenerContext represting the request.</param>
		public WebSocketRelay(HttpListenerContext context)
		{
			Context = new ContextProvider(context);
		}

		/// <summary>
		/// Start relaying this request to the right slave.
		/// </summary>
		public async void Start()
		{
			//Find the slave with the least amount of active connections.
			//TODO: Replace with single linq query? ¯\_(ツ)_/¯
			var connectionCounts = ServerProfile.KnownServers.Values.ToDictionary(t => t, t => 0);
			foreach (WebSocketRelay relay in activeRelays)
			{
				connectionCounts[relay.Slave]++;
			}
			Slave = connectionCounts.First((x) => x.Value == connectionCounts.Values.Min()).Key;


			IPAddress targetSlaveAddress = Slave.Address;

			//Connect to the slave.
			SlaveConnection = new ClientWebSocket();

			//Transfer cookies
			SlaveConnection.Options.Cookies = new CookieContainer();
			foreach (Cookie cookie in Context.Request.Cookies)
			{
				if (cookie.Domain == string.Empty)
				{
					cookie.Domain = $"{targetSlaveAddress}";
					SlaveConnection.Options.Cookies.Add(cookie);
				}
			}

			try
			{
				await SlaveConnection.ConnectAsync(new Uri($"ws://{targetSlaveAddress}:{BalancerConfig.HttpRelayPort}/chat?roomid=aaaaaa"), CancellationToken.None);
			}
			catch (WebSocketException)
			{
				Context.Response.Send("Connection failed (did you authenticate?)", HttpStatusCode.Unauthorized);
				return;
			}

			//Start relaying the connection
			Client = await Context.AcceptWebSocketAsync(null);
			SenderThread = new Thread(() => Send());
			ReceiverThread = new Thread(() => Receive());
			KeepAliveThread = new Thread(() => KeepAlive());
			SenderThread.Start();
			ReceiverThread.Start();
			KeepAliveThread.Start();

			activeRelays.Add(this);
		}

		/// <summary>
		/// Checks connection status.
		/// </summary>
		public Thread KeepAliveThread;
		/// <inheritdoc cref="KeepAliveThread"/>
		public void KeepAlive()
		{
			while (!Disposed)
			{
				if (Client.WebSocket.State != WebSocketState.Open)
				{
					Program.Log.Debug("Lost websocket connection to client.");
					return;
				}
				else if (SlaveConnection.State != WebSocketState.Open)
				{
					Program.Log.Debug("Lost websocket connection to slave.");
					return;
				}
			}
		}

		/// <summary>
		/// Receives data from the client and relays it to the slave.
		/// </summary>
		public Thread SenderThread;
		///<inheritdoc cref="SenderThread"/>
		public async void Send()
		{
			try
			{
				while (!Disposed)
				{
					byte[] receiveBuffer = new byte[1024];
					WebSocketReceiveResult receivedData = await Client.WebSocket.ReceiveAsync(receiveBuffer, TokenSource.Token);
					await SlaveConnection.SendAsync(receiveBuffer, WebSocketMessageType.Text, true, TokenSource.Token);
				}
			}
			catch (Exception e) when (e is WebSocketException || e is TaskCanceledException)
			{
				Dispose();
			}
		}

		/// <summary>
		/// Receives data from the slave and relays it to the client.
		/// </summary>
		public Thread ReceiverThread;
		///<inheritdoc cref="ReceiverThread"/>
		public async void Receive()
		{
			try
			{
				while (!Disposed)
				{
					byte[] receiveBuffer = new byte[1024];
					WebSocketReceiveResult receivedData = await SlaveConnection.ReceiveAsync(receiveBuffer, TokenSource.Token);
					await Client.WebSocket.SendAsync(receiveBuffer, WebSocketMessageType.Text, true, TokenSource.Token);
				}
			}
			catch (Exception e) when (e is WebSocketException || e is TaskCanceledException)
			{
				Dispose();
			}
		}

		/// <summary>
		/// Whether this connection was disposed or not.
		/// </summary>
		private bool Disposed = false;
		/// <summary>
		/// Dispose this connection. Automatically called if this connection was was closed by the client or slave.
		/// </summary>
		public async void Dispose()
		{
			//Ignore multiple calls. This method is sometimes called twice because Send and Receive often both call it when a connection issue appears.
			if (Disposed)
				return;

			Disposed = true;
			try
			{
				await Client.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye!", TokenSource.Token);
				await SlaveConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye!", TokenSource.Token);
			} catch (WebSocketException) { }
			
			TokenSource.Cancel();

			activeRelays.Remove(this);
		}
	}
}
