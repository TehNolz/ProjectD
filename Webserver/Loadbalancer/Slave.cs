using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Webserver.LoadBalancer
{
	public static class Slave
	{
		private static Timer HeartbeatTimer;
		private static DateTime LastHeartbeat = new DateTime();
		private static bool running = false;

		public static void Receive(JObject response, IPEndPoint EP)
		{
			switch (response["Type"].ToString())
			{
				//Confirm a heartbeat request.
				case "HEARTBEAT":
					Networking.SendData(ConnectionMessage.Confirm("HEARTBEAT", EP));
					LastHeartbeat = DateTime.Now;
					break;

				//Master informs slaves that a slave has timed out
				case "TIMEOUT":
					if (!response.TryGetValue<string>("Slave", out JToken address)) return;
					if (!response.TryGetValue<int>("Port", out JToken aort)) return;
					IPEndPoint Slave = new IPEndPoint(IPAddress.Parse((string)address), (int)aort);

					if (Balancer.Servers.ContainsKey(Slave))
					{
						Balancer.Servers.Remove(Slave, out _);
						Console.WriteLine("TIMEOUT received for slave " + Slave.Address);
					}
					else
					{
						Console.WriteLine("TIMEOUT received for unknown slave " + Slave.Address);
					}
					break;
			}
		}

		public static void Init()
		{
			Networking.Callback = Receive;
			running = true;
			HeartbeatTimer = new Timer((object _) => HeartbeatCheck(), null, 0, 100);
		}

		public static void Stop()
		{
			if (!running) return;
			HeartbeatTimer.Dispose();
		}

		private static void HeartbeatCheck()
		{
			if (LastHeartbeat.Ticks == 0) return;
			if (LastHeartbeat < DateTime.Now.AddSeconds(-2))
			{
				Console.WriteLine("Lost connection to master");

				IPEndPoint masterAddress = null;
				int minAddress = int.MaxValue;
				foreach (IPEndPoint serverAddress in Balancer.Servers.Keys)
				{
					int serverIpValue = serverAddress.Address.GetAddressBytes()[3];
					if (serverIpValue < minAddress)
					{
						minAddress = serverIpValue;
						masterAddress = serverAddress;
					}
				}

				Console.Title = $"Local - {Networking.LocalEndPoint} | Master - {masterAddress}";
				Balancer.Servers.Remove(Balancer.MasterEndpoint, out _);
				if (masterAddress?.Address.ToString() == Networking.LocalEndPoint.Address.ToString())
				{
					Console.WriteLine("Elected this server as new master");
					Balancer.IsMaster = true;
				}
				else
				{
					Console.WriteLine("Eelected " + masterAddress + " as new master");
					Balancer.MasterEndpoint = masterAddress;
				}
			}
		}
	}
}
