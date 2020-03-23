using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	public static class Networking {
		/// <summary>
		/// The UdpClient that will send/receive data.
		/// </summary>
		private static UdpClient Client;
		/// <summary>
		/// The transmission queue, containing messages that have been queued for transmission to the other servers.
		/// </summary>
		private static readonly BlockingCollection<byte[]> TransmitQueue = new BlockingCollection<byte[]>();
		/// <inheritdoc cref="Networking.ReceiveDataDelegate"/>
		public static ReceiveDataDelegate Callback;
		/// <summary>
		/// This server's local endpoint
		/// </summary>
		public static IPEndPoint LocalEndPoint => (IPEndPoint)Client.Client.LocalEndPoint;

		/// <summary>
		/// A callback that will receive incoming messages. Will be set to either the Master's or Slave's Receive methods, depending on this server's role.
		/// </summary>
		/// <param name="Data">The data that was received, converted into a JObject</param>
		/// <param name="Endpoint">The IPEndPoint of the server that sent the message</param>
		public delegate void ReceiveDataDelegate(JObject Data, IPEndPoint Endpoint);

		/// <summary>
		/// Starts the networking threads
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		public static void Init(List<IPAddress> Addresses, IPAddress MulticastAddress) {
			Client = GetClient(MulticastAddress, Addresses);
			new Thread(() => ReceiveThread(new IPEndPoint(MulticastAddress, BalancerConfig.BalancerPort))).Start();
			new Thread(() => SendThread(new IPEndPoint(MulticastAddress, BalancerConfig.BalancerPort))).Start();
		}

		/// <summary>
		/// Receiver thread
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		private static void ReceiveThread(IPEndPoint Endpoint) {
			while(true) {
				byte[] Data = Client.Receive(ref Endpoint);

				//Check if this is a known server. If it isn't, add it.
				if(!Balancer.Servers.ContainsKey(Endpoint)) {
					Balancer.Servers.TryAdd(Endpoint, new ServerProfile(Endpoint, DateTime.Now));
				}

				//If the message was sent by this server instance, ignore it.
				if(Endpoint.Address == ((IPEndPoint)Client.Client.LocalEndPoint).Address) {
					continue;
				}

				//Convert response, if possible
				JObject Response;
				try {
					Response = JObject.Parse(Encoding.UTF8.GetString(Data));
				} catch(JsonReaderException) {
					continue;
				}
				if(Response == null || !Response.ContainsKey("Type")) {
					continue;
				}

				//If this message has a Destination key, check if the message was directed at this slave
				if(Response.TryGetValue<string>("Destination", out JToken Value)) {
					if((string)Value != Networking.LocalEndPoint.ToString()) {
						continue;
					}
				}

				//Call the callback (heh).
				Callback(Response, Endpoint);
			}
		}

		/// <summary>
		/// Transmit a JObject
		/// </summary>
		/// <param name="Data"></param>
		public static void SendData(JObject Data) => TransmitQueue.Add(Encoding.UTF8.GetBytes(Data.ToString()));

		/// <summary>
		/// Sender thread. Takes items from the queue and transmits them to all servers.
		/// </summary>
		private static void SendThread(IPEndPoint Endpoint) {
			while(true) {
				byte[] Msg = TransmitQueue.Take();
				Client.Send(Msg, Endpoint);
			}
		}

		/// <summary>
		/// Create a new UdpClient, which has been preconfigured for communication with other servers.
		/// </summary>
		/// <param name="Addresses">A list of possible addresses. The client will bind to the first available address.</param>
		/// <returns>The created UdpClient</returns>
		public static UdpClient GetClient(IPAddress MulticastAddress, List<IPAddress> Addresses) {
			UdpClient Client = null;
			bool Bound = false;

			//Loop through all the addresses until we find one that binds.
			foreach(IPAddress Address in Addresses) {
				try {
					Client = new UdpClient(new IPEndPoint(Address, BalancerConfig.BalancerPort));
					Bound = true;
					break;
				} catch(SocketException) {
					continue;
				}
			}

			//If we can't find an address to bind to, throw an ArgumentException
			if(!Bound) {
				throw new ArgumentException("No available address");
			}

			//Configure multicasting, and then we're done.
			Client.JoinMulticastGroup(MulticastAddress);
			Client.MulticastLoopback = true; //Can't set to false; this breaks testing with two instances on one machine.
			return Client;
		}

		/// <summary>
		/// Sends a UDP diagram to the host at the specified endpoint
		/// </summary>
		/// <param name="Data">The data to send</param>
		/// <param name="Endpoint">The endpoint that will receive the data</param>
		public static int Send(this UdpClient c, byte[] Data, IPEndPoint Endpoint) => c.Send(Data, Data.Length, Endpoint);
	}

	/// <summary>
	/// Record class representing a server.
	/// </summary>
	public class ServerProfile {
		/// <summary>
		/// The last time a heartbeat was received for this server. Only relevant for Masters.
		/// </summary>
		public DateTime LastHeartbeat;
		/// <summary>
		/// The server's endpoint
		/// </summary>
		public IPEndPoint Endpoint;
		/// <summary>
		/// The time at which this server was registered.
		/// </summary>
		public DateTime RegisteredAt;
		public ServerProfile(IPEndPoint Endpoint, DateTime LastHeartbeat) {
			this.RegisteredAt = DateTime.Now;
			this.Endpoint = Endpoint;
			this.LastHeartbeat = LastHeartbeat;
		}
	}
}
