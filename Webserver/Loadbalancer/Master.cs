using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	public static class Master {
		/// <inheritdoc cref="Master.HeartbeatCheck"/>
		public static Timer HeartbeatTimer;

		/// <summary>
		/// The last time a heartbeat was sent out.
		/// </summary>
		public static DateTime LastHeartbeat;

		/// <summary>
		/// Initialises the master, setting the networking callback to this master's Receive method.
		/// Also creates and starts the Heartbeat timer and Listener thread.
		/// </summary>
		public static void Init() {
			Networking.Callback = Receive;
			HeartbeatTimer = new Timer((object _) => HeartbeatCheck(), null, 0, 100);
			string URL = Balancer.MasterEndpoint.Address.ToString();
			new Thread(() => Listener.Listen()).Start();
		}

		/// <summary>
		/// Receiver callback for the master thread.
		/// </summary>
		/// <param name="Message">The JSON message that was received.</param>
		/// <param name="Endpoint">The IPEndPoint of the server that sent the message.</param>
		public static void Receive(JObject Response, IPEndPoint Endpoint) {

			switch((string)Response["Type"]) {

				//Connect a new server to the network
				case "DISCOVER":
					Balancer.Servers.GetOrAdd(Endpoint, new ServerProfile(Endpoint, DateTime.Now));
					Networking.SendData(ConnectionMsg.Master);
					Console.WriteLine("Registered new slave " + Endpoint.Address);
					break;

				//Confirmation message
				case "OK":
					//Switch to the message type that was confirmed.
					switch((string)Response["Operation"]) {
						case "HEARTBEAT":
							//A server answered a heartbeat. Update its timestamp.
							Balancer.Servers[Endpoint].LastHeartbeat = DateTime.Now;
							break;

						default:
							return;
					}
					break;

				default:
					return;
			}
		}

		/// <summary>
		/// Sends a heartbeat to all slaves 100ms. Slaves that do not respond to this within 500ms are considered to have failed, and will be removed from the system.
		/// </summary>
		public static void HeartbeatCheck() {
			//Send the heartbeat
			Networking.SendData(ConnectionMsg.Heartbeat);

			//Wait for 50ms, giving the slaves a chance to respond.
			Thread.Sleep(50);
			LastHeartbeat = DateTime.Now;

			//Check which slaves have timed out, if any.
			List<IPEndPoint> ToRemove = new List<IPEndPoint>();
			foreach(KeyValuePair<IPEndPoint, ServerProfile> Entry in Balancer.Servers) {
				//We don't need to worry about our own heartbeat
				if(Entry.Key.ToString() == Networking.LocalEndPoint.ToString())
					continue;

				//If the server was registered within the last 500ms, ignore it. It likely hasn't had the chance to send a message yet.
				if(Entry.Value.RegisteredAt > LastHeartbeat.AddMilliseconds(-500))
					continue;

				//If the server didn't respond in the last 500ms, mark it for removal and notify the other servers about it. 
				if(Entry.Value.LastHeartbeat < LastHeartbeat.AddMilliseconds(-500)) {
					Networking.SendData(ConnectionMsg.Timeout(Entry.Key));
					ToRemove.Add(Entry.Key);
				}
			}

			//Remove all timed out servers.
			foreach(IPEndPoint Endpoint in ToRemove) {
				Balancer.Servers.Remove(Endpoint, out _);
			}
		}
	}
}
