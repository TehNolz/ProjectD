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
		private static bool _isMaster;
		public static int HttpPort;
		public static IPEndPoint MasterEndpoint { get; set; }
		public static readonly ConcurrentDictionary<IPEndPoint, ServerProfile> Servers = new ConcurrentDictionary<IPEndPoint, ServerProfile>();

		/// <summary>
		/// Gets or sets master mode. If true, this instance of the server will act as the server group's master.
		/// Only one master exists at any given time.
		/// </summary>
		public static bool IsMaster {
			get { return _isMaster; } 
			set {
				_isMaster = value;	
				if(value == true){
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
		/// <param name="Addresses">A list of IP addresses the listener will bind to. Only the first available address is used, which is also returned.</param>
		/// <param name="MulticastAddress">The Multicast address that the balancer will use. Must match all other servers. Multicast addresses are in the range 224.0.0.0 - 239.255.255.255</param>
		/// <param name="BalancerPort">The port the balancer will be using for internal communication</param>
		/// <param name="HttpPort">The port the balancer will be using for relaying HTTP requests to the slaves</param>
		/// <returns></returns>
		public static IPAddress Init(List<IPAddress> Addresses, IPAddress MulticastAddress = null, int BalancerPort = 12000, int HttpPort = 12001) {
			if (MulticastAddress == null) MulticastAddress = IPAddress.Parse("224.0.0.1"); //Default multicast address;
			Balancer.HttpPort = HttpPort;

			//Create a client and send a discover message.
			UdpClient Client = Networking.GetClient(Addresses, MulticastAddress, BalancerPort);
			Client.Client.ReceiveTimeout = 1000;
			byte[] Msg = Encoding.UTF8.GetBytes(ConnectionMsg.Discover.ToString());
			IPEndPoint EP = new IPEndPoint(MulticastAddress, BalancerPort);
			Client.Send(Msg, EP);

			//Wait for an answer to the discover message. If the socket times out, the server will assume
			//that no master exists, and will promote itself.
			try {
				for(int i = 0; i < 100; i++) {
					byte[] RawResponse = Client.Receive(ref EP);
					JObject Response = null;
					try{
						Response = JObject.Parse(Encoding.UTF8.GetString(RawResponse));
					} catch(JsonReaderException){
						continue;
					}
					if(!Response.ContainsKey("Type")){
						continue;
					}
					if((string)Response["Type"] == "MASTER"){
						IsMaster = false;
						break;
					}
				}
			} catch (SocketException e) {
				if(e.SocketErrorCode == SocketError.TimedOut){
					MasterEndpoint = EP;
					IsMaster = true;
				} else {
					throw new SocketException();
				}
			}
			Client.Close();

			//Initialise the networking system
			MasterEndpoint = EP;
			Networking.Init(Addresses, MulticastAddress, BalancerPort);
			Console.WriteLine("Local endpoint is {0}, Master endpoint is {1}", Networking.LocalEndPoint, EP);
			Console.Title = string.Format("Local - {0} | Master - {1}", Networking.LocalEndPoint, EP);
			return Networking.LocalEndPoint.Address;
		}
	}
}
