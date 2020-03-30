using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Net;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Database.SQLite;
using Webserver.Models;
using System.Diagnostics;

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

		/// <summary>
		/// Recieves a message from another server.
		/// </summary>
		/// <param name="message">The <see cref="JObject"/> message sent by the <paramref name="sender"/>.</param>
		/// <param name="sender">The address of the server that sent the request.</param>
		public static void Receive(JObject message, IPEndPoint sender)
		{
			string messageType = (string)message["$type"];
			switch (messageType)
			{
				//Connects a new server to the network
				case "DISCOVER":
					Balancer.Servers.GetOrAdd(sender, new ServerProfile(sender, DateTime.Now));
					Networking.SendData(ConnectionMessage.Master);
					Console.WriteLine("Registered new slave " + sender.Address);
					break;

				case "QUERY_INSERT":
					// Parse the type string into a Type object from this assembly
					var modelType = Assembly.GetExecutingAssembly().GetType(message["type"].Value<string>());

					var transaction = Program.Database.Connection.BeginTransaction();

					// Convert the message item array to an object array and insert it into the database
					var items = message["items"].Select(x => x.ToObject(modelType)).Cast(modelType);
					Utils.InvokeGenericMethod<long>((Func<IList<object>, long>)Program.Database.Insert,
						modelType,
						new[] { items }
					);

					transaction.Rollback();
					transaction.Dispose();
					
					// Send an ACK to the sender
					var json = new JObject()
					{
						{ "$type", "ACK" },
						{ "$ack_type", messageType },
						{ "$destination", sender.ToString() },
						{ "type", message["type"] },
						{ "items", new JArray(items.Select(x => JObject.FromObject(x))) },
					};
					Networking.SendData(json);

					// Send a regular QUERY_INSERT to all servers except the sender
					json.Remove("$ack_type");
					json.Remove("$destination");
					json["$type"] = messageType;
					json["$except"] = sender.ToString();
					Networking.SendData(json);
					break;

				case "ACK":
					switch ((string)message["$ack_type"])
					{
						case "HEARTBEAT":
							Balancer.Servers[sender].LastHeartbeat = DateTime.Now;
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
