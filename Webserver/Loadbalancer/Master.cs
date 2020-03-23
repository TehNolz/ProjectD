using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer
{
	public static class Master
	{
		public static Timer HeartbeatTimer;
		public static DateTime LastHeartbeat;

		public static void Init()
		{
			Networking.Callback = Receive;
			HeartbeatTimer = new Timer((object _) => HeartbeatCheck(), null, 0, 100);
			string URL = Balancer.MasterEndpoint.Address.ToString();
			new Thread(() => Listener.Listen(URL, BalancerConfig.HttpPort)).Start();
		}

		public static void Receive(JObject response, IPEndPoint address)
		{
			switch ((string)response["Type"])
			{
				//Connects a new server to the network
				case "DISCOVER":
					Balancer.Servers.GetOrAdd(address, new ServerProfile(address, DateTime.Now));
					Networking.SendData(ConnectionMessage.Master);
					Console.WriteLine("Registered new slave " + address.Address);
					break;

				case "OK":
					switch ((string)response["Operation"])
					{
						case "HEARTBEAT":
							Balancer.Servers[address].LastHeartbeat = DateTime.Now;
							break;
					}
					break;
			}
		}

		public static void HeartbeatCheck()
		{
			Networking.SendData(ConnectionMessage.Heartbeat);
			Thread.Sleep(50);
			LastHeartbeat = DateTime.Now;

			List<IPEndPoint> ToRemove = new List<IPEndPoint>();
			foreach (KeyValuePair<IPEndPoint, ServerProfile> Entry in Balancer.Servers)
			{
				if (Entry.Key.ToString() == Networking.LocalEndPoint.ToString()) continue;
				if (Entry.Value.RegisteredAt > LastHeartbeat.AddMilliseconds(-500)) continue;
				if (Entry.Value.LastHeartbeat < LastHeartbeat.AddMilliseconds(-500))
				{
					Networking.SendData(ConnectionMessage.Timeout(Entry.Key));
					ToRemove.Add(Entry.Key);
				}
			}

			foreach (IPEndPoint EP in ToRemove)
			{
				Balancer.Servers.Remove(EP, out _);
			}
		}
	}
}
