using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Database.SQLite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer
{
	public static class Balancer
	{
		public static int Port { get; set; }
		public static IPEndPoint MasterEndpoint { get; set; }

		public static ConcurrentDictionary<IPEndPoint, ServerProfile> Servers { get; } = new ConcurrentDictionary<IPEndPoint, ServerProfile>();

		public static SQLiteAdapter Database { get; private set; }

		private static bool isMaster;
		/// <summary>
		/// Gets or sets master mode. If true, this instance of the server will act as the server group's master.
		/// </summary>
		/// <remarks>
		/// Only one master exists at any given time.
		/// </remarks>
		public static bool IsMaster
		{
			get { return isMaster; }
			set
			{
				isMaster = value;
				if (value)
				{
					Slave.Stop();
					Master.Init();
				}
				else
				{
					Slave.Init();
				}
			}
		}

		/// <summary>
		/// Starts the load balancer.
		/// </summary>
		/// <param name="addresses">A list of IP addresses the listener will bind to. Only the first available address is used, which is also returned.</param>
		/// <param name="multicastAddress">The Multicast address that the balancer will use. Must match all other servers. Multicast addresses are in the range 224.0.0.0 - 239.255.255.255</param>
		/// <param name="balancerPort">The port the balancer will be using for internal communication</param>
		/// <param name="port">The port the balancer will be using for relaying HTTP requests to the slaves</param>
		public static IPAddress Init(List<IPAddress> addresses, IPAddress multicastAddress = null, int balancerPort = 12000, int port = 12001)
		{
			if (multicastAddress == null) multicastAddress = IPAddress.Parse("224.0.0.1"); //Default multicast address;
			Port = port;

			Database = new SQLiteAdapter("Database.db");

			//Create a client and send a discover message.
			var client = Networking.GetClient(addresses, multicastAddress, balancerPort);
			client.Client.ReceiveTimeout = 1000;

			byte[] messageBytes = Encoding.UTF8.GetBytes(ConnectionMessage.Discover.ToString());
			var address = new IPEndPoint(multicastAddress, balancerPort);
			client.Send(messageBytes, address);

			//Wait for an answer to the discover message. If the socket times out, the server will assume
			//that no master exists, and will promote itself.
			try
			{
				for (int i = 0; i < 100; i++)
				{
					byte[] RawResponse = client.Receive(ref address);
					JObject response = null;
					try
					{
						response = JObject.Parse(Encoding.UTF8.GetString(RawResponse));
						if (response == null || !response.ContainsKey("$type"))
							continue;
					}
					catch (JsonReaderException)
					{
						continue;
					}
					if ((string)response["$type"] == "MASTER")
					{
						IsMaster = false;
						break;
					}
				}
			}
			catch (SocketException e)
			{
				if (e.SocketErrorCode == SocketError.TimedOut)
				{
					MasterEndpoint = address;
					IsMaster = true;
				}
				else
				{
					throw e;
				}
			}
			client.Close();

			// Set up database query events
			Program.Database.Inserting += OnDatabaseInsert;

			//Initialise the networking system
			MasterEndpoint = address;
			Networking.Init(addresses, multicastAddress, balancerPort);
			Console.WriteLine("Local endpoint is {0}, Master endpoint is {1}", Networking.LocalEndPoint, address);
			Console.Title = $"Local - {Networking.LocalEndPoint} | Master - {address}";
			return Networking.LocalEndPoint.Address;
		}

		/// <summary>
		/// Gets the lock that prevents a slave server from modifying items in the database before
		/// receiving acknowledgement from the master server.
		/// </summary>
		public static Semaphore DatabaseUpdateLock { get; } = new Semaphore(1, 1);

		private static void OnDatabaseInsert(SQLiteAdapter sender, InsertEventArgs args)
		{
			if (IsMaster)
			{
				// TODO implement this but only at the end of the query
			}
			else
			{
				var items = new JArray();
				var json = new JObject()
				{
					{ "$destination", MasterEndpoint.ToString() },
					{ "$type", "QUERY_INSERT" },
					{ "type", args.ModelType.FullName },
					{ "items", items }
				};

				foreach (var item in args.Collection)
					items.Add(JObject.FromObject(item));

				// Register event for the master's ACK response
				void swapCollection(object sender, Slave.QUERY_INSERT_ACK_EventArgs e)
				{
					// Swaps the elements from these event args with the insert event args
					for (int i = 0; i < args.Collection.Count; i++)
						args.Collection[i] = e.Collection[i];

					// Continue the execution of the other event
					DatabaseUpdateLock.Release();
				}
				Slave.QUERY_INSERT_ACK += swapCollection;

				DatabaseUpdateLock.WaitOne();
				// Send the items to the master in order to have it insert the items and broadcast the results
				Networking.SendData(json);
				
				// Initiate "deadlock" (unlocked later by the QUERY_INSERT ACK from the master server)
				DatabaseUpdateLock.WaitOne(5000);
				
				// Immediately unschedule the local function after reacquiring the lock
				Slave.QUERY_INSERT_ACK -= swapCollection;
				DatabaseUpdateLock.Release();
			}
		}
	}
}
