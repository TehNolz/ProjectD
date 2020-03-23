using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	/// <summary>
	/// Load balancer slave. Slave servers act as extra RequestWorkers for the master server.
	/// Should the master server fail (or otherwise become unresponsive), the slaves will elect a new master amongst themselves.
	/// </summary>
	public static class Slave {
		/// <summary>
		/// Whether this slave is running. There's probably a better way to keep track of this.
		/// </summary>
		private static bool Running = false;
		/// <inheritdoc cref="Slave.HeartbeatCheck"/>
		private static Timer HeartbeatTimer;
		/// <summary>
		/// The time at which the last heartbeat from the master server was received.
		/// </summary>
		private static DateTime LastHeartbeat = new DateTime();

		/// <summary>
		/// Receiver callback for the slave thread.
		/// </summary>
		/// <param name="Message">The JSON message that was received.</param>
		/// <param name="Endpoint">The IPEndPoint of the server that sent the message.</param>
		public static void Receive(JObject Message, IPEndPoint Endpoint) {
			switch((string)Message["Type"]) {

				//Confirm a heartbeat request.
				case "HEARTBEAT":
					Networking.SendData(ConnectionMsg.Confirm("HEARTBEAT", Endpoint));
					LastHeartbeat = DateTime.Now;
					break;

				//Master informs slaves that a slave has timed out
				case "TIMEOUT":
					//Get the slave address and port. If either of them are missing, consider the message to be invalid and ignore it.
					if(!Message.TryGetValue<string>("Slave", out JToken Address))
						return;
					if(!Message.TryGetValue<int>("Port", out JToken Port))
						return;

					//Remove the server from the list.
					IPEndPoint Slave = new IPEndPoint(IPAddress.Parse((string)Address), (int)Port);
					if(Balancer.Servers.ContainsKey(Slave)) {
						Balancer.Servers.Remove(Slave, out _);
						Console.WriteLine("TIMEOUT received for slave " + Slave.Address);
					} else {
						Console.WriteLine("TIMEOUT received for unknown slave " + Slave.Address);
					}

					break;

				//Ignore messages that we don't have any special handling for. They probably weren't meant for us anyway.
				default:
					return;
			}
		}

		/// <summary>
		/// Initialises this slave, setting the Networking callback to this slave's Receive function.
		/// </summary>
		public static void Init() {
			Networking.Callback = Receive;
			Running = true;
			HeartbeatTimer = new Timer((object _) => HeartbeatCheck(), null, 0, 100);
		}

		/// <summary>
		/// Stops this slave, disposing of its heartbeat timer.
		/// </summary>
		public static void Stop() {
			if(!Running)
				return;
			HeartbeatTimer.Dispose();
		}

		/// <summary>
		/// Checks whether or not the master server has sent a heartbeat in the last 2 seconds.
		/// If no heartbeat was received, a new master will be automatically elected amongst the existing slaves.
		/// Note: Must keep a reference to this check's timer object at all times.
		/// </summary>
		private static void HeartbeatCheck() {
			//Skip this check if Ticks is 0, because in that case the master hasn't even had a chance to send a heartbeat yet.
			if(LastHeartbeat.Ticks == 0)
				return;

			///If the last heartbeat was received more than 2 seconds ago, elect a new master.
			if(LastHeartbeat < DateTime.Now.AddSeconds(-2)) {
				Console.WriteLine("Lost connection to master");

				//Elect a new master.
				IPEndPoint Endpoint = null;
				int Min = int.MaxValue;
				foreach(IPEndPoint Entry in Balancer.Servers.Keys) {
					//TODO: Support subnets larger than /24
					int HostNum = Entry.Address.GetAddressBytes()[3];
					if(HostNum < Min) {
						Min = HostNum;
						Endpoint = Entry;
					}
				}

				Console.Title = string.Format("Local - {0} | Master - {1}", Networking.LocalEndPoint, Endpoint);

				//Remove the old master from the list
				Balancer.Servers.Remove(Balancer.MasterEndpoint, out _);

				//Check if this server was elected
				if(Endpoint?.Address.ToString() == Networking.LocalEndPoint.Address.ToString()) {
					//This server was elected. Stop this slave thread and start a master thread.
					Console.WriteLine("Elected this server as new master");
					Balancer.IsMaster = true;
				} else {
					//Another server was elected. Switch the MasterEndPoint to the newly elected master.
					Console.WriteLine("Eelected " + Endpoint + " as new master");
					Balancer.MasterEndpoint = Endpoint;
				}
			}
		}
	}
}
