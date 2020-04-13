using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Webserver.LoadBalancer
{
	public static class Slave
	{
		/// <summary>
		/// Sets this server as slave.
		/// </summary>
		/// <param name="masterAddress">The endpoint of the master server to connect to.</param>
		/// <returns>The local IP address.</returns>
		public static void Init(IPAddress masterAddress)
		{
			Console.WriteLine("Server is running as slave. Connecting to the Master server at {0}", masterAddress);
			ServerProfile.KnownServers = new ConcurrentDictionary<IPAddress, ServerProfile>();
			new ServerProfile(Balancer.LocalAddress);

			//Bind events;
			ServerConnection.OnServerTimeout += Timeout;
			ServerConnection.OnMessageReceived += TimeoutMessage;
			ServerConnection.OnMessageReceived += RegistrationResponse;
			ServerConnection.OnMessageReceived += NewServer;

			//Create a TcpClient.
			var client = new TcpClient(new IPEndPoint(Balancer.LocalAddress, BalancerConfig.BalancerPort));
			client.Connect(new IPEndPoint(masterAddress, BalancerConfig.BalancerPort));

			//Convert the client into a ServerConnection
			var connection = new ServerConnection(client);
			Balancer.MasterServer = connection;

			//Send registration request.
			connection.Send(new Message(InternalMessageType.Register, null));

			Console.WriteLine("Connected to master at {0}. Local address is {1}", masterAddress, (IPEndPoint)client.Client.LocalEndPoint);
		}

		/// <summary>
		/// Event handler for registration responses.
		/// </summary>
		/// <param name="server">The master server who sent the response</param>
		/// <param name="message">The response</param>
		public static void RegistrationResponse(ServerConnection server, Message message)
		{
			//If this message isn't a registration response, ignore it.
			if (message.Type != InternalMessageType.RegisterResponse.ToString())
				return;

			//Register all servers the Master has informed us about.
			var receivedAddresses = (List<IPAddress>)message.Data;
			foreach (IPAddress address in receivedAddresses)
			{
				if (address.ToString() == Balancer.MasterServer.Address.ToString())
					continue;
				new ServerProfile(address);
			}
		}

		/// <summary>
		/// Event handler for new server announcements
		/// </summary>
		/// <param name="server">The master server that sent the announcement</param>
		/// <param name="message">The announcement</param>
		public static void NewServer(ServerConnection _, Message message)
		{
			//If this message isn't an announcement, ignore it.
			if (message.Type != InternalMessageType.NewServer.ToString())
				return;

			IPAddress endpoint = IPAddress.Parse(message.Data);

			//Ignore this message if it just announces our own registration
			if (endpoint.ToString() == Balancer.LocalAddress.ToString())
				return;

			Console.WriteLine($"Master announced new server at {endpoint}");
			new ServerProfile(endpoint);
		}

		/// <summary>
		/// Processes timeout announcements from the master.
		/// </summary>
		/// <param name="server"></param>
		/// <param name="message"></param>
		public static void TimeoutMessage(ServerConnection _, Message message)
		{
			if (message.Type != InternalMessageType.Timeout.ToString())
				return;

			Console.WriteLine($"Master lost connection with slave at {message.Data}");
			ServerProfile.KnownServers.TryRemove(IPAddress.Parse(message.Data), out ServerProfile _);
		}
		/// <summary>
		/// Handles a connection timeout with the master server, electing a new master as replacement.
		/// </summary>
		/// <param name="server"></param>
		public static void Timeout(ServerProfile server, string message)
		{
			Console.WriteLine($"Connection lost to master: {message}");
			ServerProfile.KnownServers.Remove(server.Address, out _);

			Console.WriteLine("Electing a new master.");

			//Elect a new master by finding the slave with the lowest IPv4 address. This is guaranteed to give the same result on every slave.
			//TODO: Maybe find a better algorithm to elect a master?
			ServerProfile newMaster = null;
			int minAddress = int.MaxValue;
			foreach (IPAddress adress in ServerProfile.KnownServers.Keys)
			{
				int num = BitConverter.ToInt32(adress.GetAddressBytes(), 0);
				if (num < minAddress)
				{
					newMaster = ServerProfile.KnownServers[adress];
					minAddress = num;
				}
			}

			//Check if this server was chosen as the new master. If it is, start promotion. If it isn't, connect to the new master.
			Console.Title = $"Local address {Balancer.LocalAddress} | Master address {newMaster.Address}";

			//Dispose the connection and reset all event bindings.
			Balancer.MasterServer.Dispose();
			ServerConnection.ResetEvents();

			//If this slave was selected, promote to Master. Otherwise, restart the slave using the new master's address.
			if (newMaster.Address.ToString() == Balancer.LocalAddress.ToString())
			{
				Console.WriteLine("Elected this slave as new master. Promoting.");
				Master.Init();
			}
			else
			{
				Console.WriteLine("Elected {0} as new master. Connecting.", newMaster.Address);
				Slave.Init(newMaster.Address);
			}
		}
	}
}