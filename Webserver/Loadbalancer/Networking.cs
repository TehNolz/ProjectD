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
		private static IPAddress MulticastAddress;
		private static int Port;
		private static UdpClient Client;
		private static readonly BlockingCollection<byte[]> TransmitQueue = new BlockingCollection<byte[]>();
		public static ReceiveDataDelegate Callback;
		public static IPEndPoint LocalEndPoint { get { return (IPEndPoint)Client.Client.LocalEndPoint; } }

		public delegate void ReceiveDataDelegate(JObject Data, IPEndPoint EP);

		/// <summary>
		/// Starts the networking threads
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		public static void Init(List<IPAddress> Addresses, IPAddress MulticastAddress, int Port){
			Networking.MulticastAddress = MulticastAddress;
			Networking.Port = Port;

			Client = GetClient(Addresses, MulticastAddress, Port);
			new Thread(() => ReceiveThread()).Start();
			new Thread(() => SendThread()).Start();
		}

		/// <summary>
		/// Receiver thread
		/// </summary>
		/// <param name="Addresses">A list of IP addresses. The receiver will listen for incoming multicast data from the first suitable address it finds</param>
		private static void ReceiveThread(){
			IPEndPoint EP = new IPEndPoint(MulticastAddress, Port);
			while (true){
				byte[] Data = Client.Receive(ref EP);

				//Check if this is a known server. If it isn't, add it.
				if (!Balancer.Servers.ContainsKey(EP)) {
					Balancer.Servers.TryAdd(EP, new ServerProfile(EP, DateTime.Now));
				}

				//If the message was sent by this server instance, ignore it.
				if (EP.Address == ((IPEndPoint)Client.Client.LocalEndPoint).Address){
					continue;
				}

				//Convert response, if possible
				JObject Response;
				try {
					Response = JObject.Parse(Encoding.UTF8.GetString(Data));
				} catch (JsonReaderException) {
					continue;
				}
				if (Response == null || !Response.ContainsKey("Type")) {
					continue;
				}

				

				//If this message has a Destination key, check if the message was directed at this slave
				if (Response.TryGetValue<string>("Destination", out JToken Value)) {
					if ((string)Value != Networking.LocalEndPoint.ToString()) {
						continue;
					}
				}

				Callback(Response, EP);
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
		private static void SendThread() {
			IPEndPoint EP = new IPEndPoint(MulticastAddress, Port);
			while (true){
				byte[] Msg = TransmitQueue.Take();
				Client.Send(Msg, EP);
			}
		}

		/// <summary>
		/// Retrieve a
		/// </summary>
		/// <param name="Addresses"></param>
		/// <returns></returns>
		public static UdpClient GetClient(List<IPAddress> Addresses, IPAddress MulticastAddress, int Port){
			UdpClient Client = null;
			bool Bound = false;
			foreach (IPAddress Addr in Addresses) {
				try {
					Client = new UdpClient(new IPEndPoint(Addr, Port));
					Bound = true;
					break;
				} catch (SocketException) {
					continue;
				}
			}
			if (!Bound) {
				throw new SocketException();
			}
			Client.JoinMulticastGroup(MulticastAddress);
			Client.MulticastLoopback = true; //Can't set to false; this breaks testing with two instances on one machine for some reason.
			return Client;
		}

		/// <summary>
		/// Sends a UDP diagram to the host at the specified endpoint
		/// </summary>
		/// <param name="Data">The data to send</param>
		/// <param name="Endpoint">The endpoint that will receive the data</param>
		public static int Send(this UdpClient c, byte[] Data, IPEndPoint Endpoint) => c.Send(Data, Data.Length, Endpoint);
	}

	public class ServerProfile {
		public DateTime LastHeartbeat;
		public IPEndPoint Endpoint;
		public DateTime RegisteredAt;
		public ServerProfile(IPEndPoint Endpoint, DateTime LastHeartbeat) {
			this.RegisteredAt = DateTime.Now;
			this.Endpoint = Endpoint;
			this.LastHeartbeat = LastHeartbeat;
		}
	}
}
