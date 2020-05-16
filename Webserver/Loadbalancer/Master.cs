using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Webserver.API.Endpoints;
using Webserver.Chat;
using Webserver.Chat.Commands;
using Webserver.Config;
using Webserver.Replication;

using static Webserver.Program;

namespace Webserver.LoadBalancer
{
	public static class Master
	{
		/// <summary>
		/// Server registration thread. Waits for incoming registration requests from new slaves.
		/// </summary>
		private static Thread RegistryThread;
		/// <summary>
		/// Discovery response thread. Waits for- and responds to incoming discovery requests.
		/// </summary>
		private static Thread DiscoveryThread;

		/// <summary>
		/// Promotes this server to master.
		/// </summary>
		public static void Init()
		{
			Log.Config("Server is running as master");
			ServerProfile.KnownServers = new ConcurrentDictionary<IPAddress, ServerProfile>();
			ServerProfile.KnownServers.TryAdd(Balancer.LocalAddress, new ServerProfile(Balancer.LocalAddress));
			Balancer.MasterServer = null;

			//Bind events
			ServerConnection.ServerTimeout += OnServerTimeout;
			ServerConnection.MessageReceived += OnDbChange;
			ServerConnection.MessageReceived += OnDbSynchronize;
			ServerConnection.MessageReceived += Example.TestHandler;

			//Chat system events
			ServerConnection.MessageReceived += ChatCommand.BroadcastHandler;
			ServerConnection.MessageReceived += UserStatus.UserConnectionHandler;
			ServerConnection.MessageReceived += UserStatus.UserDisconnectionHandler;

			//Create TcpListener using either the first available IP address in the config, or the address that was supplied.
			var listener = new TcpListener(Balancer.LocalAddress, BalancerConfig.BalancerPort);

			//Starts all necessary threads.
			DiscoveryThread = new Thread(() => DiscoveryHandler());
			DiscoveryThread.Start();
			RegistryThread = new Thread(() => RegistrationHandler(listener));
			RegistryThread.Start();
			Listener.ListenerThread = new Thread(() => Listener.Listen(((IPEndPoint)listener.LocalEndpoint).Address, BalancerConfig.HttpPort));
			Listener.ListenerThread.Start();

			Log.Config("Running interserver communication system on " + ((IPEndPoint)listener.LocalEndpoint));
		}

		/// <summary>
		/// Handles message broadcasts from slaves.
		/// </summary>
		/// <param name="message"></param>
		private static void BroadcastHandler(ServerMessage message)
		{
			// If this message is a broadcast, send it to all servers except the one it came from.
			if (message.isBroadcast)
			{
				var destinations = new List<ServerConnection>(ServerProfile.ConnectedServers);
				destinations.Remove(message.Connection);
				if (message.ID == null)
				{
					message.Send(destinations);
				}
				else
				{
					message.Reply(ServerConnection.BroadcastAndWait(message, destinations));
				}
			}
		}

		private static void OnDbSynchronize(ServerMessage message)
		{
			// Check if the type is QueryInsert
			if (message.Type != MessageType.DbSync)
				return;

			if (message.Data is null)
			{
				// Send the current database version and typelist to begin the chunked synchronization
				message.Reply(new { Types = JArray.FromObject(Program.Database.TypeList), Program.Database.Version });
				return;
			}

			// Get `amount` of changes and send them in the reply
			IEnumerable<JObject> changes = Program.Database.GetNewChanges((long)message.Data.Version, (int)message.Data.Amount).Select(x => (JObject)x);
			message.Reply(JArray.FromObject(changes));
		}

		private static void OnDbChange(ServerMessage message)
		{
			// Check if the type is DbChange
			if (message.Type != MessageType.DbChange)
				return;

			var changes = new Changes(message);
			Program.Database.Apply(changes);

			changes.Broadcast();
		}

		/// <summary>
		/// Processes connection timeouts. If a master detects a timeout, it removes the server from its list of known servers and informs all other slaves about it.
		/// </summary>
		/// <param name="server">The server that timed out.</param>
		public static void OnServerTimeout(ServerProfile server, string message)
		{
			ServerProfile.KnownServers.TryRemove(server.Address, out _);
			Log.Warning($"Lost connection to slave at {server.Address}: {message}");
			ServerConnection.Broadcast(new ServerMessage(MessageType.Timeout, server.Address));
		}

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
					int messageLength = BitConverter.ToInt32(client.GetStream().Read(sizeof(int)));
					//Read the incoming message and convert it into a Message object.
					ServerMessage message = Message.FromBytes<ServerMessage>(client.GetStream().Read(messageLength));

					//Check if the client sent a registration request. Drop the connection if it didn't.
					if (message.Type != MessageType.Register)
					{
						Log.Warning($"Dropped connection to server {client.Client.RemoteEndPoint} during registration: invalid registration request");
						client.Close();
						continue;
					}

					//Register the server and answer its request.
					var connection = new ServerConnection(client);
					connection.Send(new ServerMessage(MessageType.RegisterResponse, (from SP in ServerProfile.KnownServers.Values where !SP.Equals(connection) && !SP.Address.Equals(Balancer.LocalAddress) select SP.Address).ToList()));

					ServerConnection.Broadcast(new ServerMessage(MessageType.NewServer, connection.Address));
					Log.Info($"Successfully registered the server at {connection.Address}. Informed other slaves.");
				}
				catch (SocketException e)
				{
					Log.Warning($"Lost connection to server {client.Client.RemoteEndPoint} during registration: {e.Message}");
					continue;
				}
			}
		}

		///<inheritdoc cref="DiscoveryThread"/>
		public static void DiscoveryHandler()
		{
			//Dispose the old UdpClient and create a new one, to make sure everything is reset.
			Balancer.Client.Dispose();
			Balancer.Client = new UdpClient(new IPEndPoint(Balancer.LocalAddress, BalancerConfig.DiscoveryPort));

			//Create the standard response message, so that we don't have to constantly recreate it. It's always the same message anyway.
			byte[] response = new ServerMessage(MessageType.DiscoverResponse, Balancer.LocalAddress).GetBytes();

			//Main loop
			while (true)
			{
				try
				{
					//Wait for a discovery message
					var clientEndPoint = new IPEndPoint(IPAddress.Any, 0);
					string clientRequest = Encoding.UTF8.GetString(Balancer.Client.Receive(ref clientEndPoint));

					//Parse the response. If its not valid JSON, ignore it.
					JObject json;
					try
					{
						json = JObject.Parse(clientRequest);
					}
					catch (JsonReaderException)
					{
						continue;
					}

					//If the message JObject doesn't contain a Type key, ignore it.
					if (!json.TryGetValue("Type", out MessageType value))
					{
						continue;
					}

					//If the Type key isn't set to Discover, ignore this message.
					if (value != MessageType.Discover)
					{
						continue;
					}

					//Answer it.
					Balancer.Client.Send(response, response.Length, clientEndPoint);
					Log.Info($"Recived discovery message from {clientEndPoint.Address}.");
				}
				catch (SocketException e)
				{
					//If an error occured during receiving/sending, then there's not much we can do about it. Slave's just gotta retry.
					Log.Warning($"Failed to receive or answer discovery message: {e.Message}");
				}
			}
		}
	}
}