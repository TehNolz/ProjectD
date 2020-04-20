using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Webserver.Config;

using static Webserver.Program;

namespace Webserver.LoadBalancer
{
	public static class Balancer
	{
		/// <summary>
		/// The ServerConnection representing our connection to the master server.
		/// </summary>
		public static ServerConnection MasterServer { get; set; }
		/// <summary>
		/// A list of all valid IP addresses found in the config file.
		/// </summary>
		public static List<IPAddress> Addresses { get; set; } = new List<IPAddress>();
		/// <summary>
		/// The local IP address that this server is bound to.
		/// </summary>
		public static IPAddress LocalAddress { get; set; }
		/// <summary>
		/// The UdpClient used for server discovery. Needs to be kept alive for IP binding to work.
		/// Also used by masters to answer discovery requests.
		/// </summary>
		public static UdpClient Client { get; set; }
		/// <summary>
		/// Gets whether this server is the master server.
		/// </summary>
		public static bool IsMaster => MasterServer == null;

		/// <summary>
		/// Starts the load balancer system.
		/// </summary>
		/// <returns>This address this instance is bound to.</returns>
		public static IPAddress Init()
		{
			//Get IP address(es).
			if (BalancerConfig.IPAddresses.Count == 0)
			{
				//If no addresses are specified in the configuration file, find the internet-facing interface's IPv4 address by attempting to connect to Google's public DNS.
				var Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
				Socket.Connect("8.8.8.8", 65530);
				Addresses.Add((Socket.LocalEndPoint as IPEndPoint).Address);
				Socket.Dispose();
			}
			else
			{
				//Check if the addresses set in the configuration file are valid IPv4 addresses.
				foreach (string rawAddress in BalancerConfig.IPAddresses)
				{
					if (!IPAddress.TryParse(rawAddress, out IPAddress Address))
						Log.Warning($"Skipping invalid address {Address}");
					else
						Addresses.Add(Address);
				}
			}

			//Show a warning if no addresses are configured even after the above checks. We can't start the server without an IP address to bind to.
			if (Addresses.Count == 0)
				throw new ArgumentException("No valid addresses were found.");

			//Bind to the first available IP address. Also create an UdpClient while we're at it.
			foreach (IPAddress address in Addresses)
			{
				try
				{
					Client = new UdpClient(new IPEndPoint(address, BalancerConfig.DiscoveryPort));
					Client.Client.ReceiveTimeout = 100;
					Client.Client.SendTimeout = 100;
					break;
				}
				catch (SocketException e)
				{
					Log.Warning($"Failed to bind to address {address}: {e.Message}");
					continue;
				}
			}
			if (Client == null)
			{
				throw new ArgumentException("No addresses were availble.");
			}

			//Get our local address
			LocalAddress = ((IPEndPoint)Client.Client.LocalEndPoint).Address;
			Log.Config($"Local address is {LocalAddress}");

			//Use 10 UDP broadcasts to try and find the master server (if one exists).
			byte[] discoveryMessage = new Message(MessageType.Discover, null).GetBytes();
			var serverEndpoint = new IPEndPoint(IPAddress.Any, 0);
			bool foundMaster = false;
			bool preventEcho = false;
			for (int i = 0; i < 10; i++)
			{
				try
				{
					if (!preventEcho)
					{
						//Send a broadcast and wait for a response
						Client.Send(discoveryMessage, discoveryMessage.Length, new IPEndPoint(IPAddress.Broadcast, BalancerConfig.DiscoveryPort));
					}
					preventEcho = false;

					string rawResponse = Encoding.UTF8.GetString(Client.Receive(ref serverEndpoint));
					if (serverEndpoint.Equals(Client.Client.LocalEndPoint))
					{
						i--;
						preventEcho = true;
						continue;
					}

					//Parse the response. If its not valid JSON, ignore it.
					JObject response;
					try
					{
						response = JObject.Parse(rawResponse);
					}
					catch (JsonReaderException)
					{
						continue;
					}

					//If the message JObject doesn't contain a Type key, ignore it.
					if (!response.TryGetValue("Type", out MessageType value))
						continue;

					//If the Type key isn't set to DiscoverResponse, ignore this message.
					if (value != MessageType.DiscoverResponse)
						continue;

					//If we got this far, we found our master.
					foundMaster = true;
					break;
				}
				catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
				{
					continue;
				}
			}

			//Initialise the networking system and return our local address.
			if (foundMaster)
			{
				Slave.Init(serverEndpoint.Address);
				Program.Database.Synchronize();
			}
			else
				Master.Init();

			Console.Title = $"Local address {LocalAddress} | Master address {serverEndpoint.Address}";

			return LocalAddress;
		}
	}
}

