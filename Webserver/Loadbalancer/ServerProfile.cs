using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Webserver.LoadBalancer
{
	/// <summary>
	/// Represents a server in the load balancer network.
	/// </summary>
	public class ServerProfile
	{
		/// <summary>
		/// The server's IP address and port, represented as an endpoint.
		/// </summary>
		public IPAddress Address { get; private set; }

		/// <summary>
		/// Create a new server profile
		/// </summary>
		/// <param name="endPoint">The endpoint this server is located at.</param>
		public ServerProfile(IPAddress address)
		{
			Address = address;
			if (!KnownServers.TryAdd(address, this))
			{
				throw new ArgumentException("A server already exists for this endpoint");
			}
		}

		/// <summary>
		/// Dictionary containing all known servers.
		/// </summary>
		public static ConcurrentDictionary<IPAddress, ServerProfile> KnownServers { get; set; }
		/// <summary>
		/// Get a list of all connected servers.
		/// </summary>
		public static List<ServerConnection> ConnectedServers => (from ServerProfile server in KnownServers.Values where server is ServerConnection select server).Cast<ServerConnection>().ToList();
	}

	/// <summary>
	/// Represents a connection to a server.
	/// </summary>
	public class ServerConnection : ServerProfile, IDisposable
	{
		private ServerState _state;
		/// <summary>
		/// Gets the current state of this <see cref="ServerConnection"/>.
		/// <para/>
		/// This value may not be accurate.
		/// </summary>
		/// <exception cref="ObjectDisposedException">An attempt was made to set this property
		/// when this <see cref="ServerConnection"/> has been disposed.</exception>
		/// <exception cref="ArgumentException"><see langword="value"/> was <see cref="ServerState.Disposed"/>.</exception>
		public ServerState State
		{
			get => _state;
			set
			{
				if (_state == ServerState.Disposed)
					throw new ObjectDisposedException(null);
				if (value == ServerState.Disposed)
					throw new ArgumentException($"Cannot set this property to {nameof(ServerState.Disposed)} manually.");
				_state = value;
			}
		}

		/// <summary>
		/// The TcpClient representing the connection to this server.
		/// </summary>
		private TcpClient Client { get; set; }

		/// <inheritdoc cref="TcpClient.GetStream"/>
		private NetworkStream Stream => Client.GetStream();

		/// <summary>
		/// Queue of Message objects that need to be transmitted to this server.
		/// </summary>
		private BlockingCollection<ServerMessage> TransmitQueue = new BlockingCollection<ServerMessage>();

		/// <summary>
		/// Sender thread, which sents the data received by SendData to this server.
		/// </summary>
		private Thread SenderThread;
		/// <summary>
		/// Receiver thread, which receives data sent to us by this server.
		/// </summary>
		private Thread ReceiverThread;

		#region Delegates
		/// <summary>
		/// Delegate for the <see cref="MessageReceived"/> and <see cref="ReplyReceived"/> events.
		/// </summary>
		/// <param name="message">The message that was received</param>
		public delegate void ReceiveEventHandler(ServerMessage message);
		/// <summary>
		/// Delegate for the <see cref="ServerTimeout"/> event.
		/// </summary>
		/// <param name="server">The <see cref="ServerProfile"/> that has reported a timeout.</param>
		public delegate void TimeoutEventHandler(ServerProfile sender, string message);
		#endregion

		#region Events
		/// <summary>
		/// Triggers whenever a connected server sends us a message.
		/// </summary>
		public static event ReceiveEventHandler MessageReceived;
		/// <summary>
		/// Triggers whenever a connected server times out.
		/// </summary>
		/// <remarks>
		/// NOT triggered when the Master server informs us that it has lost connection.
		/// </remarks>
		public static event TimeoutEventHandler ServerTimeout;

		/// <summary>
		/// Invoked when a <see cref="ServerProfile"/> has received a response to it's message.
		/// </summary>
		protected static event ReceiveEventHandler ReplyReceived;
		#endregion

		/// <summary>
		/// Converts a TcpClient into a server connection.
		/// </summary>
		/// <param name="client">A TcpClient object that is connected to a server instance.</param>
		public ServerConnection(TcpClient client) : base(((IPEndPoint)client.Client.RemoteEndPoint).Address)
		{
			//If this client isn't connected to anything, throw a SocketException with the NotConnected error code.
			//We can't continue unless the client is connected to a server instance.
			if (!client.Connected)
				throw new SocketException((int)SocketError.NotConnected);

			Client = client;

			//Start this connection's send and receive thread.
			SenderThread = new Thread(() => Sender_Run());
			SenderThread.Start();
			ReceiverThread = new Thread(() => Receiver_Run());
			ReceiverThread.Start();

			State = ServerState.Open;
		}

		/// <summary>
		/// Sends data to this server.
		/// </summary>
		/// <param name="message">The message to send.</param>
		public void Send(ServerMessage message)
		{
			// Throw an exception if this connection is no longer active.
			if (isDisposed)
				throw new ObjectDisposedException(GetType().Name);

			// Add message to the queue
			TransmitQueue.Add(message);
		}
		/// <summary>
		/// Send a message and wait for a response.
		/// </summary>
		/// <param name="message">The message to send.</param>
		/// <param name="timeout">The amount of milliseconds to wait for the reply to arrive. If no message is received within this time, a SocketException is thrown with the TimedOut status code.</param>
		public ServerMessage SendAndWait(ServerMessage message)
		{
			// Use the Message.SendAndWait to set the ID if it is null
			if (message.ID == Guid.Empty)
				return message.SendAndWait(this);

			// Create a semaphore to block this function untill a reply is received
			var responseLock = new SemaphoreSlim(0, 1);

			// Unlocker function that unblocks SendAndAwait
			ServerMessage reply = null;
			void unlocker(ServerMessage _reply)
			{
				// Check if the _reply is in response to the sent message
				if (_reply != null && message.ID != _reply.ID)
					return;

				reply = _reply;
				responseLock.Release();
			}

			// Subscribe the unlocker and send the message
			ReplyReceived += unlocker;
			Send(message);

			// Block until the reply event handler unlocks this semaphore
			responseLock.Wait();
			ReplyReceived -= unlocker;

			// If reply is null, the events have been reset.
			return reply ?? throw new SocketException((int)SocketError.ConnectionReset);
		}

		/// <summary>
		/// Send data to multiple servers
		/// </summary>
		/// <param name="servers">A list of servers to send the message to.</param>
		/// <param name="message">The message to send to each server.</param>
		public static void Send(IEnumerable<ServerConnection> servers, ServerMessage message)
		{
			foreach (ServerConnection connection in servers)
				connection.Send(message);
		}

		/// <summary>
		/// Send data to all known servers
		/// </summary>
		/// <param name="message">The message that will be sent.</param>
		public static void Broadcast(ServerMessage message)
		{
			message.isBroadcast = true;
			if (Balancer.IsMaster)
				Send(ConnectedServers, message);
			else
				message.Send(Balancer.MasterServer);
		}

		/// <summary>
		/// Send data to multiple servers and wait for them to respond.
		/// </summary>
		/// <param name="message">The message to send</param>
		/// <param name="servers">The ServerConnection to send the data to. If null, the connections in ConnectedServers will be used. Not very useful for slaves as they only have one connection.</param>
		/// <returns></returns>
		public static List<ServerMessage> BroadcastAndWait(ServerMessage message, List<ServerConnection> servers = null)
		{
			if (servers == null)
				servers = ConnectedServers;

			message.isBroadcast = true;
			if (Balancer.IsMaster)
			{
				//If the server is master, send the message to all connected servers and wait for them to respond.
				var tasks = new Task<ServerMessage>[servers.Count];
				for (int i = 0; i < servers.Count; i++)
				{
					tasks[i] = new Task<ServerMessage>(() => servers[i].SendAndWait(message));
					tasks[i].Start();
				}

				//Wait for all servers to respond, then return this data.
				Task.WaitAll(tasks);
				return (from T in tasks select T.Result).ToList();
			}
			else
			{
				//Get the master to do our work.
				return message.SendAndWait(Balancer.MasterServer).Data;
			}
		}

		/// <summary>
		/// Main loop of the <see cref="SenderThread"/>.
		/// </summary>
		/// <seealso cref="SenderThread"/>
		private void Sender_Run()
		{
			while (Client.Connected)
			{
				//Get message + message length.
				byte[] message = TransmitQueue.Take().GetBytes();
				byte[] messageLength = BitConverter.GetBytes(message.Length);

				//Send message + message length
				try
				{
					Stream.Write(messageLength, 0, messageLength.Length);
					Stream.Write(message, 0, message.Length);
				}
				catch (Exception e) when (e is SocketException || e is IOException)
				{
					ServerTimeout(this, e.Message);
					Dispose();
					return;
				}
			}
		}
		/// <summary>
		/// Main loop of the <see cref="ReceiverThread"/>.
		/// </summary>
		/// <seealso cref="ReceiverThread"/>
		private void Receiver_Run()
		{
			//Keep receiving data as long as the client is connected.
			while (Client.Connected)
			{
				ServerMessage message;
				try
				{
					//Send a 0-byte package to test the connection
					Client.Client.Send(new byte[1], 0, 0);

					//If no request was received, sleep for 10ms before checking again.
					if (!Stream.DataAvailable)
					{
						Thread.Sleep(10);
						continue;
					}

					//Get the length of the incoming message
					int messageLength = BitConverter.ToInt32(Stream.Read(sizeof(int)));
					byte[] rawMessage = Stream.Read(messageLength);

					//Read the incoming message and convert it into a Message object.
					message = Message.FromBytes<ServerMessage>(rawMessage);
					message.Connection = this;

					if (message.ID != null && message.Flags.HasFlag(MessageFlags.Reply))
					{
						foreach (Delegate d in GetHandlersForType(ReplyReceived, message.Type))
							d.Method.Invoke(d.Target, new[] { message });
					}
					else
					{
						foreach (Delegate d in GetHandlersForType(MessageReceived, message.Type))
							d.Method.Invoke(d.Target, new[] { message });
					}
				}
				catch (Exception e) when (e is SocketException || e is IOException)
				{
					// A connection issue occured. Trigger the OnServerTimeout event and drop this connection.
					ServerTimeout(this, e.Message);
					Dispose();
					return;
				}
			}
		}

		/// <summary>
		/// Unsubscribe all event handlers from the ServerConnection events.
		/// </summary>
		public static void ResetEvents()
		{
			ServerTimeout = null;
			MessageReceived = null;
			ReplyReceived?.Invoke(null);
			ReplyReceived = null;
		}

		#region IDisposable Support
		/// <summary>
		/// Track whether this connection is still active.
		/// </summary>
		private bool isDisposed = false;

		/// <summary>
		/// Disposes this server connection, stopping all its threads and disposing the underlying TcpClient.
		/// </summary>
		public void Dispose()
		{
			// Detect redundant calls
			if (State == ServerState.Disposed)
				return;

			Client.Dispose();
			_state = ServerState.Disposed;
		}
		#endregion

		/// <summary>
		/// Returns all delegates in the <paramref name="event"/>s invocation list
		/// with an <see cref="EventMessageType.Type"/> equal to <paramref name="type"/>.
		/// <para/>
		/// Also returns every delegate in the invocation list without an <see cref="EventMessageType"/>
		/// attribute.
		/// </summary>
		/// <param name="event">The event delegate whose invocation list to use.</param>
		/// <param name="type">The type to check for in the invocation lists <see cref="EventMessageType"/>s.</param>
		private static IEnumerable<Delegate> GetHandlersForType(Delegate @event, MessageType type)
		{
			if (!(@event is null))
			{
				// Loop through the invocation list and invoke every handler with a matching event message type attribute (or no attribute)
				foreach (Delegate m in @event.GetInvocationList())
				{
					EventMessageType attr = m.Method.GetCustomAttribute<EventMessageType>();
					if (attr is null || attr.Type == type)
						yield return m;
				}
			}
		}
	}

	/// <summary>
	/// Describes the various states of a <see cref="ServerConnection"/>.
	/// </summary>
	public enum ServerState
	{
		/// <summary>
		/// The server connection is closed.
		/// </summary>
		Closed,
		/// <summary>
		/// The connection to the server is open, but it is not yet
		/// accepting incoming requests.
		/// </summary>
		Open,
		/// <summary>
		/// The server is currently synchronizing it's database.
		/// </summary>
		Synchronizing,
		/// <summary>
		/// The server is accepting requests.
		/// </summary>
		Ready,
		/// <summary>
		/// The server connection has been disposed.
		/// </summary>
		Disposed
	}

	/// <summary>
	/// Specifies what <see cref="MessageType"/> should invoke a <see cref="ServerConnection.ReceiveEventHandler"/>.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
	public class EventMessageType : Attribute
	{
		/// <summary>
		/// Gets the message type that should invoke the event handler.
		/// </summary>
		public MessageType Type { get; }

		/// <summary>
		/// Initializes a new instance of <see cref="EventMessageType"/> with the given <paramref name="type"/>.
		/// </summary>
		/// <param name="type"></param>
		public EventMessageType(MessageType type) => Type = type;
	}
}
