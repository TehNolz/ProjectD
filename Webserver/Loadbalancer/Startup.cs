using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	public static class Balancer {
		/// <summary>
		/// The IPEndPoint of the master load balancer
		/// </summary>
		public static IPEndPoint MasterEndpoint { get; set; }
		/// <summary>
		/// All currently known servers.
		/// </summary>
		public static readonly ConcurrentDictionary<IPEndPoint, ServerProfile> Servers = new ConcurrentDictionary<IPEndPoint, ServerProfile>();

		private static bool _isMaster;
		/// <summary>
		/// Gets or sets master mode. If true, this instance of the server will act as the server group's master.
		/// Only one master exists at any given time.
		/// </summary>
		public static bool IsMaster {
			get => _isMaster;
			set {
				_isMaster = value;
				if(value) {
					Slave.Stop();
					Master.Init();
				} else {
					Slave.Init();
				}
			}
		}

		/// <summary>
		/// Starts the load balancer.
		/// </summary>
		/// <param name="Addresses">A list of IP 
		/// es the listener will bind to. Only the first available address is used, which is also returned.</param>
		/// <param name="MulticastAddress">The Multicast address that the balancer will use. Must match all other servers. Multicast addresses are in the range 224.0.0.0 - 239.255.255.255</param>
		/// <param name="BalancerPort">The port the balancer will be using for internal communication</param>
		/// <param name="HttpRelayPort">The port the balancer will be using for relaying HTTP requests to the slaves</param>
		/// <returns></returns>
		public static IPAddress Init(IPAddress MulticastAddress, List<IPAddress> Addresses) {
			//Create a client and send a discover message to try and find an existing master service.
			UdpClient Client = Networking.GetClient(MulticastAddress, Addresses);
			Client.Client.ReceiveTimeout = 1000;
			byte[] Msg = Encoding.UTF8.GetBytes(ConnectionMsg.Discover.ToString());
			IPEndPoint Endpoint = new IPEndPoint(MulticastAddress, BalancerConfig.BalancerPort);
			Client.Send(Msg, Endpoint);

			//Wait for an answer to the discover message. If the socket times out (after 1s), the server will assume that no master exists.
			//In this case, it will assume the role of master itself.
			try {
				for(int i = 0; i < 100; i++) {
					//Accept a response.
					byte[] RawResponse = Client.Receive(ref Endpoint);
					JObject Response = null;

					//If the response is not a valid JSON message, ignore it.
					try {
						Response = JObject.Parse(Encoding.UTF8.GetString(RawResponse));
					} catch(JsonReaderException) {
						continue;
					}

					//If the response doesn't have the Type key, ignore it.
					if(!Response.ContainsKey("Type")) {
						continue;
					}

					//If this is a response to our discover message, acknowledge the sender as a master.
					if((string)Response["Type"] == "MASTER") {
						IsMaster = false;
						break;
					}
				}
			} catch(SocketException e) {
				//If a SocketException was thrown due to a timeout, assume that no master exist.
				//If it wasn't, rethrow the exception because we'll need to figure out what happened.
				if(e.SocketErrorCode == SocketError.TimedOut) {
					MasterEndpoint = Endpoint;
					IsMaster = true;
				} else {
					throw new SocketException();
				}
			}
			Client.Close();

			//Initialise the networking system
			MasterEndpoint = Endpoint;
			Networking.Init(Addresses, MulticastAddress);

			Console.WriteLine("Local endpoint is {0}, Master endpoint is {1}", Networking.LocalEndPoint, Endpoint);
			Console.Title = string.Format("Local - {0} | Master - {1}", Networking.LocalEndPoint, Endpoint);
			return Networking.LocalEndPoint.Address;
		}
	}
}
