using Config;

using Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

using Webserver.API;
using Webserver.Chat;
using Webserver.Config;
using Webserver.LoadBalancer;
using Webserver.Models;
using Webserver.Replication;
using Webserver.Webserver;

namespace Webserver
{
	public class Program
	{
		public const string DatabaseName = "Database.db";
		public static ServerDatabase Database;

		public static Logger Log { get; private set; }

		public static void Main()
		{
			Console.SetOut(new CustomWriter(Console.OutputEncoding, Console.Out));

			Log = new Logger(Level.ALL, Console.Out)
			{
				Format = "{asctime:HH:mm:ss} {classname,-15} {levelname,6}: {message}"
			};

			//Load config file
			if (!File.Exists("Config.json"))
				ConfigFile.Write("Config.json");

			if (ConfigFile.Load("Config.json") is int missing && missing > 0)
			{
				ConfigFile.Write("Config.json");
				Log.Error($"{missing} configuration setting(s) are missing. The missing settings have been inserted.");
			}


			//Check for duplicate network ports. Each port setting needs to be unique as we can't bind to one port multiple times.
			var ports = new List<int>() { BalancerConfig.BalancerPort, BalancerConfig.DiscoveryPort, BalancerConfig.HttpRelayPort, BalancerConfig.HttpPort };
			if (ports.Distinct().Count() != ports.Count)
			{
				Log.Error("One or more duplicate network port settings have been detected. The server cannot start.");
				Log.Error("Press any key to exit.");
				Console.ReadKey();
				return;
			}

			//If the VerifyIntegrity config option is enabled, check all files in wwwroot for corruption.
			//If at least one checksum mismatch is found, pause startup and show a warning.
			if (MiscConfig.VerifyIntegrity)
			{
				Log.Config("Checking file integrity...");
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.WWWRoot);
				if (Diff > 0)
				{
					Log.Error($"Integrity check failed. Validation failed for {Diff} file(s).");
					Log.Error("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Log.Error("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.WWWRoot, true);
				}
				Log.Config("No integrity issues found.");
			}

			//Crawl through the wwwroot folder to find all resources.
			Resource.Crawl(WebserverConfig.WWWRoot);

			//Parse Redirects.config to register all HTTP redirections.
			Redirects.LoadRedirects("Redirects.config");
			Log.Config($"Registered {Redirects.RedirectDict.Count} redirections");

			// Initialize database
			InitDatabase(DatabaseName);

			//Register all API endpoints, chat commands
			APIEndpoint.DiscoverEndpoints();
			ChatCommand.DiscoverCommands();

			//Start load balancer
			IPAddress localAddress;
			try
			{
				localAddress = Balancer.Init();
				Log.Config("Started load balancer.");
			}
			catch (Exception e)
			{
				e = e.InnerException ?? e;
				Log.Error("The server could not start: " + e.Message, e);
				Console.ReadLine();
				return;
			}

			//Start distributor and worker threads
			RequestWorker.Queue = new BlockingCollection<ContextProvider>();
			var workers = new List<RequestWorker>();
			for (int i = 0; i < WebserverConfig.WorkerThreadCount; i++)
			{
				var worker = new RequestWorker(Database.NewConnection());
				workers.Add(worker);
				worker.Start();
			}

			var distributor = new Thread(() => Distributor.Run(localAddress, BalancerConfig.HttpRelayPort));
			distributor.Start();

			foreach (RequestWorker worker in workers)
				worker.Join();

			//TODO: Implement proper shutdown
			Distributor.Dispose();
			distributor.Join();

		}

		/// <summary>
		/// Initializes the database.
		/// </summary>
		/// Note: Split into its own function to allow for unit testing to use it as well.
		public static void InitDatabase(string datasource)
		{
			Database = ServerDatabase.CreateConnection(datasource);
			Database.BroadcastChanges = false;

			//Create tables if they don't already exist.
			Database.TryCreateTable<ExampleModel>();
			Database.TryCreateTable<User>();
			Database.TryCreateTable<Session>();
			Database.TryCreateTable<Chatroom>();
			Database.TryCreateTable<Chatlog>();
			Database.TryCreateTable<ChatroomMembership>();

			//Create Admin account if it doesn't exist already;
			if (Database.Select<User>("Email = 'Administrator'").FirstOrDefault() == null)
			{
				var admin = new User(Database, "Administrator", AuthenticationConfig.AdministratorPassword)
				{
					PermissionLevel = PermissionLevel.Admin
				};
				Database.Update(admin);
			}

			//Create default channel if none exist
			if (Database.Select<Chatroom>().Count() == 0)
			{
				Database.Insert<Chatroom>(new Chatroom()
				{
					Name = "General",
					Private = false,
				});
			}
		}
	}
}
