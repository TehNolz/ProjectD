using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	public static class Slave {
		private static bool Running = false;
		private static Timer HeartbeatTimer;
		private static DateTime LastHeartbeat = new DateTime();

		public static void Receive(JObject Response, IPEndPoint EP) {

			switch ((string)Response["Type"]) {
				//Confirm a heartbeat request.
				case "HEARTBEAT":
					Networking.SendData(ConnectionMsg.Confirm("HEARTBEAT", EP));
					LastHeartbeat = DateTime.Now;
					break;

				//Master informs slaves that a slave has timed out
				case "TIMEOUT":
					if (!Response.TryGetValue<string>("Slave", out JToken Address)) return;
					if (!Response.TryGetValue<int>("Port", out JToken Port)) return;
					IPEndPoint Slave = new IPEndPoint(IPAddress.Parse((string)Address), (int)Port);
					if(Balancer.Servers.ContainsKey(Slave)){
						Balancer.Servers.Remove(Slave, out _);
						Console.WriteLine("TIMEOUT received for slave " + Slave.Address);
					} else {
						Console.WriteLine("TIMEOUT received for unknown slave " + Slave.Address);
					}

					break;

				default: return;
			}
		}

		public static void Init() {
			Networking.Callback = Receive;
			Running = true;
			HeartbeatTimer = new Timer((object _) => HeartbeatCheck(), null, 0, 100);
		}

		public static void Stop(){
			if (!Running) return;
			HeartbeatTimer.Dispose();
		}

		private static void HeartbeatCheck(){
			if (LastHeartbeat.Ticks == 0) return;
			if (LastHeartbeat < DateTime.Now.AddSeconds(-2)) {
				Console.WriteLine("Lost connection to master");

				IPEndPoint EP = null;
				int Min = int.MaxValue;
				foreach(IPEndPoint Entry in Balancer.Servers.Keys){
					int HostNum = Entry.Address.GetAddressBytes()[3];
					if(HostNum < Min){
						Min = HostNum;
						EP = Entry;
					}
				}

				Console.Title = string.Format("Local - {0} | Master - {1}", Networking.LocalEndPoint, EP);
				Balancer.Servers.Remove(Balancer.MasterEndpoint, out _);
				if (EP?.Address.ToString() == Networking.LocalEndPoint.Address.ToString()){
					Console.WriteLine("Elected this server as new master");
					Balancer.IsMaster = true;
				} else {
					Console.WriteLine("Eelected " + EP + " as new master");
					Balancer.MasterEndpoint = EP;
				}
			}
		}
	}
}
