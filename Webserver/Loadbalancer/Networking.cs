using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer
{
	public static class Networking
	{
		public static IPEndPoint LocalEndPoint => (IPEndPoint)Client.Client.LocalEndPoint;
		public static BlockingCollection<byte[]> TransmitQueue { get; } = new BlockingCollection<byte[]>();

		public static IPAddress MulticastAddress { get; set; }
		public static int Port { get; set; }
		public static UdpClient Client { get; set; }
		public static ReceiveDataDelegate Callback { get; set; }

		public delegate void ReceiveDataDelegate(JObject Data, IPEndPoint EP);

		/// <summary>
		/// Starts the networking threads
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		public static void Init(List<IPAddress> Addresses, IPAddress multicastAddress, int port)
		{
			MulticastAddress = multicastAddress;
			Port = port;

			Client = GetClient(Addresses, multicastAddress, port);
			new Thread(() => ReceiveThread()) { Name = "ReceiverThread" }.Start();
			new Thread(() => SenderThread_Run()) { Name = "SenderThread" }.Start();
		}

		/// <summary>
		/// Receiver thread
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		private static void ReceiveThread()
		{
			var address = new IPEndPoint(MulticastAddress, Port);
			while (true)
			{
				byte[] data = Client.Receive(ref address);

				// Add the server if it isn't known already
				Balancer.Servers.TryAdd(address, new ServerProfile(address, DateTime.Now));

				//If the message was sent by this server instance, ignore it.
				if (address.Address == ((IPEndPoint)Client.Client.LocalEndPoint).Address)
					continue;

				//Convert response, if possible
				JObject response;
				try
				{
					response = JObject.Parse(Encoding.UTF8.GetString(data));
					if (response == null || !response.ContainsKey("$type"))
						continue;
				}
				catch (JsonReaderException)
				{
					continue;
				}

				//If this message has a $destination key, check if the message was directed at this slave
				if (response.TryGetValue<string>("$destination", out JToken destination) && destination.ToString() != LocalEndPoint.ToString())
					continue;

				// If this message has an $except key, proceed only if it is not equal to this server's endpoint
				if (response.TryGetValue<string>("$except", out JToken except) && except.ToString() == LocalEndPoint.ToString())
					continue;

				Callback(response, address);
			}
		}

		/// <summary>
		/// Transmit a JObject
		/// </summary>
		/// <param name="data"></param>
		public static void SendData(JObject data) => TransmitQueue.Add(Encoding.UTF8.GetBytes(data.ToString(Formatting.None)));

		/// <summary>
		/// Sender thread. Takes items from the queue and transmits them to all servers.
		/// </summary>
		private static void SenderThread_Run()
		{
			IPEndPoint EP = new IPEndPoint(MulticastAddress, Port);
			while (true)
			{
				byte[] Msg = TransmitQueue.Take();
				Client.Send(Msg, EP);
			}
		}

		/// <summary>
		/// Retrieve a
		/// </summary>
		/// <param name="Addresses"></param>
		/// <returns></returns>
		public static UdpClient GetClient(List<IPAddress> Addresses, IPAddress MulticastAddress, int Port)
		{
			UdpClient client = null;
			bool isBound = false;
			foreach (IPAddress Addr in Addresses)
			{
				try
				{
					client = new UdpClient(new IPEndPoint(Addr, Port));
					isBound = true;
					break;
				}
				catch (SocketException)
				{
					continue;
				}
			}
			if (!isBound)
			{
				throw new SocketException();
			}
			client.JoinMulticastGroup(MulticastAddress);
			client.MulticastLoopback = true; //Can't set to false; this breaks testing with two instances on one machine for some reason.
			return client;
		}

		/// <summary>
		/// Sends a UDP diagram to the host at the specified endpoint
		/// </summary>
		/// <param name="data">The data to send</param>
		/// <param name="address">The <see cref="IPEndPoint"/> that will receive the data</param>
		public static int Send(this UdpClient c, byte[] data, IPEndPoint address)
			=> c.Send(data, data.Length, address);
	}

	public class ServerProfile
	{
		public DateTime LastHeartbeat;
		public IPEndPoint Endpoint;
		public DateTime RegisteredAt;

		public ServerProfile(IPEndPoint address, DateTime lastHeartbeat)
		{
			RegisteredAt = DateTime.Now;
			Endpoint = address;
			LastHeartbeat = lastHeartbeat;
		}
	}
}
