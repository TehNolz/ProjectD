using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Webserver.Chat;
using Webserver.Chat.Commands;
using Webserver.Config;
using Webserver.Replication;

using static Webserver.Program;
using static Webserver.Config.DatabaseConfig;

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
			Log.Info("Server is running as master");
			ServerProfile.KnownServers = new ConcurrentDictionary<IPAddress, ServerProfile>();
			ServerProfile.KnownServers.TryAdd(Balancer.LocalAddress, new ServerProfile(Balancer.LocalAddress));

			//Bind events
			ServerConnection.ServerTimeout += OnServerTimeout;
			ServerConnection.MessageReceived += OnDbSyncBackupStart;
			ServerConnection.MessageReceived += OnDbSyncBackup;
			ServerConnection.MessageReceived += OnDbSyncStart;
			ServerConnection.MessageReceived += OnDbSync;
			ServerConnection.MessageReceived += OnDbChange;
			ServerConnection.MessageReceived += OnServerStateChange;
			;

			//Chat system events
			ServerConnection.MessageReceived += UserMessage.UserMessageHandler;
			ServerConnection.MessageReceived += Chatroom.ChatroomUpdateHandler;

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
			if (message.isBroadcast && message.ID == null)
			{
				var destinations = new List<ServerConnection>(ServerProfile.ConnectedServers);
				destinations.Remove(message.Connection);
				message.Send(destinations);
			}
		}

		#region Database Events
		/// <summary>
		/// Accepts a single <see cref="long"/> as version number and replies with any of the following:
		/// <list type="bullet">
		/// <item>The filename of the <see cref="DatabaseBackup.LastBackup"/> if the given version is less than the master database version.</item>
		/// <item><see langword="null"/> if the version matches that of the master database.</item>
		/// <item>A error JSON describing that the version is invalid (larger than the master database).</item>
		/// </list>
		/// <para/>
		/// This also sets the <see cref="ServerConnection.State"/> of the <paramref name="message"/>
		/// connection to <see cref="ServerState.Synchronizing"/>.
		/// </summary>
		[EventMessageType(MessageType.DbSyncBackupStart)]
		private static void OnDbSyncBackupStart(ServerMessage message)
		{
			// Get the user_version of the slave's database and this database
			long version = Program.Database.UserVersion;
			long slaveVersion = message.Data;

			switch (slaveVersion.CompareTo(version))
			{
				case -1:
					// Send the name and length of the last backup file
					FileInfo backup = DatabaseBackup.LastBackup;
					message.Reply(new
					{
						backup.Name,
						backup.Length
					});
					break;
				case 0: message.Reply(null); break; // Send nothing if the versions are equal
				default: message.Reply(new JObject() {{ "error", "Database version is larger than the master database version." }}); break;
			}
		}
		/// <summary>
		/// Accepts an offset value as <see cref="long"/> and filename as string. Responds with the following:
		/// <list type="bullet">
		/// <item>A chunk of the database backup file with a length of <see cref="BackupTransferChunkSize"/>.</item>
		/// <item>The string <c>"file not found"</c> if the given filepath is not a backup file.</item>
		/// <item>The string <c>"error"</c> if an error ocurred while trying to read the backup file.</item>
		/// </list>
		/// </summary>
		[EventMessageType(MessageType.DbSyncBackup)]
		private static void OnDbSyncBackup(ServerMessage message)
		{
			string fileName = message.Data.FileName;
			long offset = message.Data.Offset;

			// Get the fileInfo of the backup
			var backup = new FileInfo(Path.Combine(BackupDir, fileName));

			if (!backup.Exists)
			{
				message.Reply("file not found");
				return;
			}

			try
			{
				// Open, seek and read a chunk from the database backup
				using FileStream fs = backup.OpenRead();
				byte[] buffer = new byte[Utils.ParseDataSize(BackupTransferChunkSize)];
				fs.Seek(offset, default);
				fs.Read(buffer);
				// Send the chunk
				message.Reply(buffer);
			}
			catch (IOException e)
			{
				Log.Error(string.Concat(e.GetType().Name, ": ", e.Message), e);
				message.Reply("error");
			}
		}

		/// <summary>
		/// Always replies with an array of <see cref="ModelType"/>s and the current
		/// <see cref="ServerDatabase.ChangelogVersion"/>.
		/// <para/>
		/// This also sets the <see cref="ServerConnection.State"/> of the <paramref name="message"/>
		/// connection to <see cref="ServerState.Synchronizing"/>.
		/// </summary>
		[EventMessageType(MessageType.DbSyncStart)]
		private static void OnDbSyncStart(ServerMessage message)
		{
			message.Reply(new
			{
				Types = JArray.FromObject(Program.Database.TypeList),
				Version = Program.Database.ChangelogVersion
			});
			message.Connection.State = ServerState.Synchronizing;
		}
		/// <summary>
		/// Accepts a version as <see cref="long"/> and amount as <see cref="int"/>.
		/// Always responds with a range of <see cref="Changes"/> objects with an amount
		/// equal or less than the amount parameter.
		/// </summary>
		[EventMessageType(MessageType.DbSync)]
		private static void OnDbSync(ServerMessage message)
		{
			long version = message.Data.Version;
			int amount = message.Data.Amount;

			// Get `amount` of changes and send them in the reply
			IEnumerable<JObject> changes = Program.Database.GetNewChanges((long)message.Data.Version, (int)message.Data.Amount).Select(x => (JObject)x);
			message.Reply(JArray.FromObject(changes));
		}

		/// <summary>
		/// Accepts a <see cref="Changes"/> object from a slave, inserts it and returns it with a new id.
		/// </summary>
		[EventMessageType(MessageType.DbChange)]
		private static void OnDbChange(ServerMessage message)
		{
			// Check if the type is DbChange
			if (message.Type != MessageType.DbChange)
				return;

			var changes = new Changes(message);
			Program.Database.Apply(changes);

			changes.Broadcast();
		}
		#endregion

		/// <summary>
		/// Accepts a state as <see cref="ServerState"/> and updates the state of the
		/// <see cref="ServerMessage.Connection"/> to the given state.
		/// </summary>
		[EventMessageType(MessageType.StateChange)]
		private static void OnServerStateChange(ServerMessage message)
		{
			try
			{
				message.Connection.State = (ServerState)message.Data;
			}
			catch (InvalidCastException e)
			{
				Log.Error(string.Concat($"OnServerStateChange received an invalid state value from server {message.Connection.Address}", ": ", e.Message), e);
			}
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