using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Webserver.LoadBalancer
{
	public static class Master
	{
		/// <summary>
		/// Server registration thread. Waits for incoming registration requests from new slaves.
		/// </summary>
		public static Thread RegistryThread;
		/// <summary>
		/// Discovery response thread. Waits for- and responds to incoming discovery requests.
		/// </summary>
		public static Thread DiscoveryThread;

		/// <summary>
		/// Promotes this server to master.
		/// </summary>
		public static void Init()
		{
			Console.WriteLine("Server is running as master");
			ServerProfile.KnownServers = new ConcurrentDictionary<IPAddress, ServerProfile>();
			ServerProfile.KnownServers.TryAdd(Balancer.LocalAddress, new ServerProfile(Balancer.LocalAddress));

			//Bind events
			ServerConnection.ServerTimeout += OnServerTimeout;
			ServerConnection.MessageReceived += OnQueryInsert;

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

		private static void OnQueryInsert(Message message)
		{
			// Check if the type is QueryInsert
			if (message.Type != MessageType.QueryInsert)
				return;

			Console.WriteLine("Got QueryInsert from slave");
			Console.WriteLine(((JObject)message.Data).ToString());

			// Parse the type string into a Type object from this assembly
			Type modelType = Assembly.GetExecutingAssembly().GetType(message.Data.Type);

			var transaction = Program.Database.Connection.BeginTransaction();

			Console.WriteLine("Inserting items");
			// Convert the message item array to an object array and insert it into the database
			dynamic[] items = ((JArray)message.Data.Items).Select(x => x.ToObject(modelType)).Cast(modelType);
			Utils.InvokeGenericMethod<long>((Func<IList<object>, long>)Program.Database.Insert,
				modelType,
				new[] { items }
			);

			transaction.Rollback();
			transaction.Dispose();

			// Create the reply message body
			var outItems = new JArray();
			var json = new JObject() {
				{ "Items", outItems }
			};

			// Fill the items JArray
			foreach (dynamic item in items)
				outItems.Add(JObject.FromObject(item));

			// Send the new items as a reply
			Console.WriteLine("Sending updated batch back to slave");
			message.Reply(json);

			// Broadcast the message to all remaining servers
			json.Add("Type", message.Data.Type);
			Console.WriteLine("Sending updated batch to the remaining slaves");
			ServerConnection.Send(ServerProfile.KnownServers.Values
					.Where(x => x is ServerConnection && x != message.Connection)
					.Cast<ServerConnection>(),
				new Message(MessageType.QueryInsert, json)
			);
		}

		/// <summary>
		/// Processes connection timeouts. If a master detects a timeout, it removes the server from its list of known servers and informs all other slaves about it.
		/// </summary>
		/// <param name="server">The server that timed out.</param>
		public static void OnServerTimeout(ServerProfile server, string message)
		{
			ServerProfile.KnownServers.TryRemove(server.Address, out _);
			Console.WriteLine($"Lost connection to slave at {server.Address}: {message}");
			ServerConnection.Broadcast(new Message(MessageType.Timeout, server.Address));
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
					var message = new Message(client.GetStream().Read(messageLength));

					//Check if the client sent a registration request. Drop the connection if it didn't.
					if (message.Type != MessageType.Register)
					{
						Console.WriteLine("Dropped connection to server {0} during registration: invalid registration request", client.Client.RemoteEndPoint);
						client.Close();
						continue;
					}

					//Register the server and answer its request.
					var connection = new ServerConnection(client);
					connection.Send(new Message(MessageType.RegisterResponse, (from SP in ServerProfile.KnownServers.Values where !SP.Equals(connection) && !SP.Address.Equals(Balancer.LocalAddress) select SP.Address).ToList()));

					ServerConnection.Broadcast(new Message(MessageType.NewServer, connection.Address));
					Console.WriteLine("Successfully registered the server at {0}. Informed other slaves.", connection.Address);
				}
				catch (SocketException e)
				{
					Console.WriteLine($"Lost connection to server {client.Client.RemoteEndPoint} during registration: {e.Message}");
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
			byte[] response = new Message(MessageType.DiscoverResponse, Balancer.LocalAddress).GetBytes();

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
					if (!json.TryGetValue<string>("Type", out JToken value))
					{
						continue;
					}

					//If the Type key isn't set to Discover, ignore this message.
					if ((string)value != MessageType.Discover.ToString())
					{
						continue;
					}

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