using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

using Webserver.LoadBalancer;
using Webserver.Models;
using Webserver.Webserver;
using static Webserver.Chat.Chat;
using static Webserver.Program;

namespace Webserver.Chat
{
	/// <summary>
	/// Represents a websocket connection from this slave to the load balancer's websocket relay.
	/// </summary>
	public class ChatConnection
	{
		/// <summary>
		/// All currently active connections.
		/// </summary>
		public static List<ChatConnection> ActiveConnections { get; set; } = new List<ChatConnection>();

		/// <summary>
		/// The WebSocketContext representing our connection to the relay..
		/// </summary>
		public HttpListenerWebSocketContext Client;
		/// <summary>
		/// The HttpListenerContext representing the HTTP request that started this websocket connection.
		/// </summary>
		public ContextProvider Context;

		/// <summary>
		/// The chatrooms this user has joined.
		/// </summary>
		public IEnumerable<Chatroom> Chatrooms => Chatroom.GetAccessableByUser(Chat.Database, User);

		/// <summary>
		/// The user this connection belongs to.
		/// </summary>
		public User User;

		/// <summary>
		/// Cancellation token for async requests.
		/// </summary>
		private CancellationTokenSource TokenSource { get; set; } = new CancellationTokenSource();

		#region Delegates
		/// <summary>
		/// Delegate for the <see cref="MessageReceived"/> and <see cref="ReplyReceived"/> events.
		/// </summary>
		/// <param name="message">The message that was received</param>
		public delegate void ReceiveEventHandler(ChatMessage message);
		#endregion

		#region Events
		/// <summary>
		/// Invoked when a <see cref="ChatConnection"/> has received a response to its message.
		/// </summary>
		protected static event ReceiveEventHandler ReplyReceived;
		#endregion

		/// <summary>
		/// Accept a web socket connection.
		/// </summary>
		/// <param name="context">A ContextProvider representing the client's request to open a websocket connection to the server.</param>
		public ChatConnection(ContextProvider context)
		{
			//If this isn't a request to open a websocket, throw an ArgumentException.
			if (!context.Request.IsWebSocketRequest)
				throw new ArgumentException("Not a websocket request.");
			Context = context;

			#region Authentication
			//Authenticate this user.
			//TODO: Move this + API authentication to a function?
			//If the SessionID cookie is missing, the user isn't logged in and therefore can't use this endpoint.
			Cookie cookie = Context.Request.Cookies["SessionID"];
			if (cookie == null)
			{
				Log.Trace("Rejected websocket request; no cookie.");

				Context.Response.Send("No cookie", HttpStatusCode.Unauthorized);
				return;
			}

			//Check if a valid session still exists
			var session = Session.GetSession(Chat.Database, cookie.Value);
			if (session == null)
			{
				Log.Trace("Rejected websocket request; no session.");
				Context.Response.Send("No session", HttpStatusCode.Unauthorized);
				return;
			}

			//The session is valid. Renew the session and retrieve user info.
			session.Renew(Chat.Database);
			User = Chat.Database.Select<User>("Email = @email", new { email = session.UserEmail }).FirstOrDefault();
			#endregion

			//Open the connection.
			OpenConnection();
		}
		/// <summary>
		/// Starts the chat connection. Not included in the constructor because this must be done async.
		/// </summary>
		private async void OpenConnection()
		{
			//Accept the connection
			Client = await Context.AcceptWebSocketAsync(null);
			Log.Trace($"Accepted websocket connection");

			//Start receiver and sender threads
			ReceiverThread = new Thread(() => Receive());
			SenderThread = new Thread(() => Sender());
			KeepAliveThread = new Thread(() => KeepAlive());
			ReceiverThread.Start();
			SenderThread.Start();
			KeepAliveThread.Start();

			ActiveConnections.Add(this);

			UserConnect(User);

			//Get user info
			//TODO Reduce database + master server calls
			var users = new JArray();
			foreach(Guid ID in (from C in Chatrooms from U in C.GetUsers() select U).Distinct())
			{
				User user = Chat.Database.Select<User>("ID = @ID", new { ID }).First();
				JObject json = user.GetJson();
				json.Add("Status", (int)(GetConnectionCount(user) >= 1 ? UserStatus.Online : UserStatus.Offline));
				users.Add(json);
			}

			//Send info to the client.
			Send(new ChatMessage(MessageType.ChatInfo, new JObject()
			{
				{"Chatrooms",  Chatroom.GetJsonBulk(Chatrooms)},
				{"CurrentUser", User.GetJson() },
				{"Users", users }
			}));
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
				if (Client.WebSocket.State == WebSocketState.CloseReceived)
				{
					Log.Debug("Websocket connection closed by relay.");
					Dispose();
					return;
				}
				else if (Client.WebSocket.State == WebSocketState.Aborted)
				{
					Log.Debug("Lost websocket connection to relay.");
					Dispose();
					return;
				}
			}
		}

		/// <summary>
		/// Receives data from the relay and processes it.
		/// </summary>
		private Thread ReceiverThread;
		/// <inheritdoc cref="ReceiverThread"/>
		private async void Receive()
		{
			try
			{
				while (!Disposed)
				{
					byte[] receiveBuffer = new byte[1024];
					await Client.WebSocket.ReceiveAsync(receiveBuffer, TokenSource.Token);
					if (Client.WebSocket.State != WebSocketState.Open)
						return;

					ChatMessage message;
					try
					{
						message = ChatMessage.FromBytes(receiveBuffer);
					}
					catch (JsonReaderException e)
					{
						Send(new ChatMessage(MessageType.InvalidMessage, e.Message));
						continue;
					}

					message.Connection = this;
					message.User = User;

					if (message.ID != null && message.Flags.HasFlag(MessageFlags.Reply))
						ReplyReceived?.Invoke(message);
					else
						ChatCommand.ProcessChatCommand(message);
				}
			}
			catch (Exception e) when (e is WebSocketException || e is TaskCanceledException)
			{
				Dispose();
			}
		}

		/// <summary>
		/// Queue of items that need to be sent to the client.
		/// </summary>
		private BlockingCollection<ChatMessage> TransmitQueue = new BlockingCollection<ChatMessage>();
		/// <summary>
		/// Sends data to the client.
		/// </summary>
		private Thread SenderThread;
		/// <inheritdoc cref="SenderThread"/>
		private async void Sender()
		{
			try
			{
				while (!Disposed)
					await Client.WebSocket.SendAsync(TransmitQueue.Take(TokenSource.Token).GetBytes(), WebSocketMessageType.Text, true, TokenSource.Token);
			}
			catch (Exception e) when (e is WebSocketException || e is TaskCanceledException || e is OperationCanceledException)
			{
				Dispose();
			}
		}

		/// <summary>
		/// Sends data to the client on the other side of this relay connection.
		/// Does nothing if this connection has been closed.
		/// </summary>
		/// <param name="message">The message to send.</param>
		public void Send(ChatMessage message)
		{
			// Throw an exception if this connection is no longer active.
			if (Disposed)
				return;

			// Add message to the queue
			TransmitQueue.Add(message);
		}

		/// <summary>
		/// Send data to the client on the other side of this relay connection.
		/// </summary>
		/// <param name="statusCode">The message status code</param>
		/// <param name="message">The message to send.</param>
		public void Send(ChatStatusCode statusCode, ChatMessage message)
		{
			message.StatusCode = statusCode;
			Send(message);
		}

		/// <summary>
		/// Send data to multiple clients.
		/// </summary>
		/// <param name="servers">A list of servers to send the message to.</param>
		/// <param name="message">The message to send to each server.</param>
		public static void Send(IEnumerable<ChatConnection> servers, ChatMessage message)
		{
			foreach (ChatConnection connection in servers)
				connection.Send(message);
		}

		/// <summary>
		/// Send a message and wait for a response.
		/// </summary>
		/// <param name="message">The message to send.</param>
		/// <param name="timeout">The amount of milliseconds to wait for the reply to arrive. If no message is received within this time, a SocketException is thrown with the TimedOut status code.</param>
		public ChatMessage SendAndWait(ChatMessage message, int timeout = 500)
		{
			// Use the Message.SendAndWait to set the ID if it is null
			if (message.ID == Guid.Empty)
				return message.SendAndWait(this, timeout);

			// Create a semaphore to block this function untill a reply is received
			var responseLock = new SemaphoreSlim(0, 1);

			// Unlocker function that unblocks SendAndAwait
			ChatMessage reply = null;
			void unlocker(ChatMessage _reply)
			{
				// Check if the _reply is in response to the sent message
				if (message.ID != _reply.ID)
					return;

				reply = _reply;
				responseLock.Release();
			}

			// Subscribe the unlocker and send the message
			ReplyReceived += unlocker;
			Send(message);

			// Block until the reply event handler unlocks the semaphore. Otherwise throw an exception
			if (!responseLock.Wait(timeout))
				throw new SocketException((int)SocketError.TimedOut);
			ReplyReceived -= unlocker;

			return reply;
		}

		/// <summary>
		/// Send chat messages to all chat clients connected to the system. This includes those connected to remote servers.
		/// </summary>
		/// <param name="message">The message that will be sent.</param>
		public static void Broadcast(ChatMessage message) => ServerConnection.Broadcast(new ServerMessage(MessageType.Chat, message));

		/// <summary>
		/// Whether this connection was disposed.
		/// </summary>
		private bool Disposed = false;
		/// <summary>
		/// Dispose this connection, stopping all threads and informing the relay.
		/// </summary>
		public void Dispose()
		{
			if (Disposed)
				return;

			Disposed = true;
			Client.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye!", TokenSource.Token);
			TokenSource.Cancel();

			Chat.UserDisconnect(User);

			ActiveConnections.Remove(this);
		}
	}
}
