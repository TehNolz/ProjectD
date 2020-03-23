using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer
{
	public static class Balancer
	{
		public static int Port { get; set; }
		public static IPEndPoint MasterEndpoint { get; set; }

		public static ConcurrentDictionary<IPEndPoint, ServerProfile> Servers { get; } = new ConcurrentDictionary<IPEndPoint, ServerProfile>();

		private static bool isMaster;
		/// <summary>
		/// Gets or sets master mode. If true, this instance of the server will act as the server group's master.
		/// </summary>
		/// <remarks>
		/// Only one master exists at any given time.
		/// </remarks>
		public static bool IsMaster
		{
			get { return isMaster; }
			set
			{
				isMaster = value;
				if (value)
				{
					Slave.Stop();
					Master.Init();
				}
				else
				{
					Slave.Init();
				}
			}
		}

		/// <summary>
		/// Starts the load balancer.
		/// </summary>
		/// <param name="addresses">A list of IP addresses the listener will bind to. Only the first available address is used, which is also returned.</param>
		/// <param name="multicastAddress">The Multicast address that the balancer will use. Must match all other servers. Multicast addresses are in the range 224.0.0.0 - 239.255.255.255</param>
		/// <param name="balancerPort">The port the balancer will be using for internal communication</param>
		/// <param name="port">The port the balancer will be using for relaying HTTP requests to the slaves</param>
		public static IPAddress Init(List<IPAddress> addresses, IPAddress multicastAddress = null, int balancerPort = 12000, int port = 12001)
		{
			if (multicastAddress == null) multicastAddress = IPAddress.Parse("224.0.0.1"); //Default multicast address;
			Port = port;

			//Create a client and send a discover message.
			var client = Networking.GetClient(addresses, multicastAddress, balancerPort);
			client.Client.ReceiveTimeout = 1000;

			byte[] messageBytes = Encoding.UTF8.GetBytes(ConnectionMessage.Discover.ToString());
			var address = new IPEndPoint(multicastAddress, balancerPort);
			client.Send(messageBytes, address);

			//Wait for an answer to the discover message. If the socket times out, the server will assume
			//that no master exists, and will promote itself.
			try
			{
				for (int i = 0; i < 100; i++)
				{
					byte[] RawResponse = client.Receive(ref address);
					JObject response = null;
					try
					{
						response = JObject.Parse(Encoding.UTF8.GetString(RawResponse));
						if (response == null || !response.ContainsKey("Type"))
							continue;
					}
					catch (JsonReaderException)
					{
						continue;
					}
					if ((string)response["Type"] == "MASTER")
					{
						IsMaster = false;
						break;
					}
				}
			}
			catch (SocketException e)
			{
				if (e.SocketErrorCode == SocketError.TimedOut)
				{
					MasterEndpoint = address;
					IsMaster = true;
				}
				else
				{
					throw e;
				}
			}
			client.Close();

			//Initialise the networking system
			MasterEndpoint = address;
			Networking.Init(addresses, multicastAddress, balancerPort);
			Console.WriteLine("Local endpoint is {0}, Master endpoint is {1}", Networking.LocalEndPoint, address);
			Console.Title = $"Local - {Networking.LocalEndPoint} | Master - {address}";
			return Networking.LocalEndPoint.Address;
		}
	}
}
