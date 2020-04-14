using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Webserver.LoadBalancer
{
	public static class Master
	{
		/// <summary>
		/// Promotes this server to master.
		/// </summary>
		/// <returns></returns>
		public static void Init()
		{
			Console.WriteLine("Server is running as master");
			ServerProfile.KnownServers = new ConcurrentDictionary<IPAddress, ServerProfile>();

			//Bind events
			ServerConnection.ServerTimeout += OnServerTimeout;

			//Create TcpListener using either the first available IP address in the config, or the address that was supplied.
			var listener = new TcpListener(Balancer.LocalAddress, BalancerConfig.BalancerPort);

			//Starts all necessary threads.
			DiscoveryThread = new Thread(() => DiscoveryHandler());
			DiscoveryThread.Start();
			RegistryThread = new Thread(() => RegistrationHandler(listener));
			RegistryThread.Start();
			Listener.ListenerThread = new Thread(() => Listener.Listen(((IPEndPoint)listener.LocalEndpoint).Address, BalancerConfig.HttpPort));
			Listener.ListenerThread.Start();

			Console.WriteLine("Running interserver communication system on " + ((IPEndPoint)listener.LocalEndpoint));
		}

		/// <summary>
		/// Processes connection timeouts. If a master detects a timeout, it removes the server from its list of known servers and informs all other slaves about it.
		/// </summary>
		/// <param name="server">The server that timed out.</param>
		public static void OnServerTimeout(ServerProfile server, string message)
		{
			ServerProfile.KnownServers.TryRemove(server.Address, out _);
			Console.WriteLine($"Lost connection to slave at {server.Address}: {message}");
			ServerConnection.Broadcast(new Message(InternalMessageType.Timeout, server.Address));
		}

		/// <summary>
		/// Server registration thread. Waits for incoming registration requests from new slaves.
		/// </summary>
		public static Thread RegistryThread;
		///<inheritdoc cref="RegistryThread"/>
		public static void RegistrationHandler(TcpListener listener)
		{
			listener.Start();
			while (true)
			{
				TcpClient client = listener.AcceptTcpClient();
				try
				{
					//Wait for the server's registration request.
					//Get the length of the incoming message
					int messageLength = BitConverter.ToInt32(Utils.ReadBytes(sizeof(int), client.GetStream()));
					//Read the incoming message and convert it into a Message object.
					var message = new Message(Utils.ReadBytes(messageLength, client.GetStream()));

					//Check if the client sent a registration request. Drop the connection if it didn't.
					if (message.Type != InternalMessageType.Register.ToString())
					{
						Console.WriteLine("Dropped connection to server {0} during registration: invalid registration request", client.Client.RemoteEndPoint);
						client.Close();
						continue;
					}

					//Register the server and answer its request.
					var connection = new ServerConnection(client);
					connection.Send(new Message(InternalMessageType.RegisterResponse, (from SP in ServerProfile.KnownServers.Values where !SP.Equals(connection) select SP.Address).ToList()));

					ServerConnection.Broadcast(new Message(InternalMessageType.NewServer, connection.Address));
					Console.WriteLine("Successfully registered the server at {0}. Informed other slaves.", connection.Address);
				}
				catch (SocketException e)
				{
					Console.WriteLine($"Lost connection to server {client.Client.RemoteEndPoint} during registration: {e.Message}");
					continue;
				}
			}
		}

		/// <summary>
		/// Discovery response thread. Waits for- and responds to incoming discovery requests.
		/// </summary>
		public static Thread DiscoveryThread;
		///<inheritdoc cref="DiscoveryThread"/>
		public static void DiscoveryHandler()
		{
			//Dispose the old UdpClient and create a new one, to make sure everything is reset.
			Balancer.Client.Dispose();
			Balancer.Client = new UdpClient(new IPEndPoint(Balancer.LocalAddress, BalancerConfig.DiscoveryPort));

			//Create the standard response message, so that we don't have to constantly recreate it. It's always the same message anyway.
			byte[] response = new Message(InternalMessageType.DiscoverResponse, Balancer.LocalAddress).GetBytes();

			//Main loop
			while (true)
			{
				try
				{
					//Wait for a discovery message
					var clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
					string ClientRequest = Encoding.UTF8.GetString(Balancer.Client.Receive(ref clientEndPoint));

					//Answer it.
					Balancer.Client.Send(response, response.Length, clientEndPoint);
					Console.WriteLine($"Recived discovery message from {clientEndPoint.Address}.");
				}
				catch (SocketException e)
				{
					//If an error occured during receiving/sending, then there's not much we can do about it. Slave's just gotta retry.
					Console.WriteLine($"Failed to receive or answer discovery message: {e.Message}");
				}
			}
		}
	}
}