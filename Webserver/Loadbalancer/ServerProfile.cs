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
		/// Track whether this connection is still active.
		/// </summary>
		private bool Disposed = false;

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
		/// Delegate for the OnMessageReceived event
		/// </summary>
		/// <param name="sender">The server who sent the message</param>
		/// <param name="message">The message that was received</param>
		public delegate void ReceiveEventHandler(ServerConnection sender, Message message);
		/// <summary>
		/// Triggers whenever a connected server sends us a message.
		/// </summary>
		public static event ReceiveEventHandler OnMessageReceived;
		/// <summary>
		/// Delegate for the OnServerTimeout event
		/// </summary>
		/// <param name="server"></param>
		public delegate void TimeoutEventHandler(ServerProfile server, string message);
		/// <summary>
		/// Triggers whenever a connected server times out.
		/// Note: NOT triggered when the Master server informs us that it has lost connection.
		/// </summary>
		public static event TimeoutEventHandler OnServerTimeout;

		/// <summary>
		/// Unsubscribe all event handlers from the ServerConnection events.
		/// </summary>
		public static void ResetEvents()
		{
			OnServerTimeout = null;
			OnMessageReceived = null;
		}

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
			SenderThread = new Thread(() => Sender());
			SenderThread.Start();
			ReceiverThread = new Thread(() => Receiver());
			ReceiverThread.Start();
		}

		/// <summary>
		/// Sends data to this server.
		/// </summary>
		/// <param name="message">The message to send.</param>
		public void Send(Message message)
		{
			//Throw an exception if this connection is no longer active.
			if (Disposed)
				throw new ObjectDisposedException("Connection closed.");

			//Add message to the queue
			TransmitQueue.Add(message);
		}

		/// <summary>
		/// Send data to multiple servers
		/// </summary>
		/// <param name="servers">A list of servers to send the message to.</param>
		/// <param name="message">The message to send to each server.</param>
		public static void Send(List<ServerConnection> servers, Message message)
		{
			foreach (ServerConnection connection in servers)
			{
				connection.Send(message);
			}
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
		/// Sender thread, which sents the data received by SendData to this server.
		/// </summary>
		private Thread SenderThread;
		/// <inheritdoc cref="SenderThread"/>
		private void Sender()
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
					OnServerTimeout(this, e.Message);
					Dispose();
				}
			}
		}

		/// <summary>
		/// Receiver thread, which receives data sent to us by this server.
		/// </summary>
		private Thread ReceiverThread;
		///<inheritdoc cref="ReceiverThread"/>
		private void Receiver()
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
					int messageLength = BitConverter.ToInt32(Utils.ReadBytes(sizeof(int), Stream));
					byte[] rawMessage = Utils.ReadBytes(messageLength, Stream);

					//Read the incoming message and convert it into a Message object.
					message = new Message(rawMessage);
				}
				catch (SocketException e)
				{
					//A connection issue occured. Trigger the OnServerTimeout event and drop this connection.
					OnServerTimeout(this, e.Message);
					Dispose();
					continue;
				}

				OnMessageReceived(this, message);
			}
		}

		/// <summary>
		/// Disposes this server connection, stopping all its threads and disposing the underlying TcpClient.
		/// </summary>
		public void Dispose() => Client.Dispose();
	}
}
