using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
	}

	/// <summary>
	/// Represents a connection to a server.
	/// </summary>
	public class ServerConnection : ServerProfile, IDisposable
	{
		/// <summary>
		/// The TcpClient representing the connection to this server.
		/// </summary>
		private TcpClient Client { get; set; }

		/// <inheritdoc cref="TcpClient.GetStream"/>
		private NetworkStream Stream => Client.GetStream();

		/// <summary>
		/// Queue of Message objects that need to be transmitted to this server.
		/// </summary>
		private BlockingCollection<Message> TransmitQueue = new BlockingCollection<Message>();

		/// <summary>
		/// Sender thread, which sents the data received by SendData to this server.
		/// </summary>
		private Thread SenderThread;
		/// <summary>
		/// Receiver thread, which receives data sent to us by this server.
		/// </summary>
		private Thread ReceiverThread;

		/// <summary>
		/// Track whether this connection is still active.
		/// </summary>
		private bool disposed = false;

		#region Delegates
		/// <summary>
		/// Delegate for the <see cref="MessageReceived"/> and <see cref="ReplyReceived"/> events.
		/// </summary>
		/// <param name="message">The message that was received</param>
		public delegate void ReceiveEventHandler(Message message);
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
		}

		/// <summary>
		/// Sends data to this server.
		/// </summary>
		/// <param name="message">The message to send.</param>
		public void Send(Message message)
		{
			// Throw an exception if this connection is no longer active.
			if (disposed)
				throw new ObjectDisposedException(GetType().Name);

			// Add message to the queue
			TransmitQueue.Add(message);
		}

		/// <summary>
		/// Send data to multiple servers
		/// </summary>
		/// <param name="servers">A list of servers to send the message to.</param>
		/// <param name="message">The message to send to each server.</param>
		public static void Send(IEnumerable<ServerConnection> servers, Message message)
		{
			foreach (ServerConnection connection in servers)
			{
				connection.Send(message);
			}
		}

		/// <summary>
		/// Send a message and wait for a response.
		/// </summary>
		/// <param name="message">The message to send.</param>
		/// <param name="timeout">The amount of milliseconds to wait for the reply to arrive. If no message is received within this time, a SocketException is thrown with the TimedOut status code.</param>
		public Message SendAndWait(Message message, int timeout = 500)
		{
			// Create a semaphore to block this function untill a reply is received
			var responseLock = new SemaphoreSlim(0, 1);

			// Unlocker function that unblocks SendAndAwait
			Message reply = null;
			void unlocker(Message _reply)
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

			return reply;
		}

		/// <summary>
		/// Send data to all known servers
		/// </summary>
		/// <param name="message">The message that will be sent.</param>
		public static void Broadcast(Message message)
		{
			var servers = (from ServerProfile server in KnownServers.Values where server is ServerConnection select server).Cast<ServerConnection>().ToList();
			Send(servers, message);
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
				catch (IOException e)
				{
					ServerTimeout(this, e.Message);
					Dispose();
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
				Message message;
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
					message = new Message(rawMessage, this);

					if (message.ID != null)
						ReplyReceived(message);
					else
						MessageReceived(message);
				}
				catch (SocketException e)
				{
					// A connection issue occured. Trigger the OnServerTimeout event and drop this connection.
					ServerTimeout(this, e.Message);
					Dispose();
					continue;
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
			ReplyReceived = null;
		}

		/// <summary>
		/// Disposes this server connection, stopping all its threads and disposing the underlying TcpClient.
		/// </summary>
		public void Dispose()
		{
			// Detect redundant calls
			if (disposed)
				return;

			Client.Dispose();
			disposed = true;
		}
	}
}
