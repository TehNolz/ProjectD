using Config;

using Database.SQLite;

using Logging;
using Logging.Highlighting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
			AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) => Cleanup();
			AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => Cleanup();
			
			Console.SetOut(new CustomWriter(Console.OutputEncoding, Console.Out));

			Log = new Logger(Level.ALL)
			{
				Format = "{asctime:HH:mm:ss} {classname,-15} {levelname,6}: {message}",
				Name = "Global Logger"
			};
			// Add new child logger that only writes to the console.
			Log.Attach(new Logger(Level.ALL, Console.Out) { Name = "Logger (Console)", Format = Log.Format });

			//Load config file
			if (!File.Exists("Config.json"))
				ConfigFile.Write("Config.json");

			if (ConfigFile.Load("Config.json") is int missing && missing > 0)
			{
				ConfigFile.Write("Config.json");
				Log.Error($"{missing} configuration setting(s) are missing. The missing settings have been inserted.");
			}

			// Initialize the remaining components of the logger. (Stuff about the logger that uses the config)
			InitLogger();

			// Create progressbar for server config
			var progress = new ProgressBar()
			{
				Prefix = "Configuring   [{3,-4:P0}]",
				MaxProgress = 13
			};

			//Check for duplicate network ports. Each port setting needs to be unique as we can't bind to one port multiple times.
			var ports = new List<int>() { BalancerConfig.BalancerPort, BalancerConfig.DiscoveryPort, BalancerConfig.HttpRelayPort, BalancerConfig.HttpPort };
			if (ports.Distinct().Count() != ports.Count)
			{
				Log.Error("One or more duplicate network port settings have been detected. The server cannot start.");
				Log.Error("Press any key to exit.");
				Console.ReadKey();
				return;
			}
			progress.Draw(1);

			//If the VerifyIntegrity config option is enabled, check all files in wwwroot for corruption.
			//If at least one checksum mismatch is found, pause startup and show a warning.
			if (MiscConfig.VerifyIntegrity)
			{
				Log.Config("Checking file integrity...");
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.WWWRoot);
				progress.Draw(1.5);
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
			progress.Draw(2);

			//Crawl through the wwwroot folder to find all resources.
			Resource.Crawl(WebserverConfig.WWWRoot);
			progress.Draw(4);

			//Parse Redirects.config to register all HTTP redirections.
			Redirects.LoadRedirects("Redirects.config");
			Log.Config($"Registered {Redirects.RedirectDict.Count} redirections");
			progress.Draw(5);

			// Initialize database
			Database = ServerDatabase.CreateConnection(DatabaseName);
			Database.BroadcastChanges = false;
			progress.Draw(6);
			InitDatabase(Database);
			progress.Draw(7);

			//Register all API endpoints, chat commands
			APIEndpoint.DiscoverEndpoints();
			progress.Draw(7.5);
			ChatCommand.DiscoverCommands();
			progress.Draw(8);

			//Start load balancer
			IPAddress localAddress;
			try
			{
				localAddress = Balancer.Init();
				Log.Config("Started load balancer.");
				progress.Draw(9);
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
				progress.Draw(10 + (1 / (i + 1)));
			}
			progress.Draw(11);

			var distributor = new Thread(() => Distributor.Run(localAddress, BalancerConfig.HttpRelayPort));
			distributor.Start();
			progress.Draw(12);

			if (!Balancer.IsMaster)
			{
				// Ready the server
				Balancer.Ready();
				Log.Config($"Server is ready to accept connections");
			}
			progress.Draw(13);

			// Exiter thread. Responds to KeyboardInterrupt
			new Thread(() =>
			{
				Console.TreatControlCAsInput = true;
				while (true)
				{
					// Wait for ctrl+c and then shutdown the program
					ConsoleKeyInfo key = Console.ReadKey(true);
					if (key.Modifiers.HasFlag(ConsoleModifiers.Control) &&
						key.Key == ConsoleKey.C)
						break;
				}
				Shutdown();
			}) { Name = "Program Exiter" }.Start();

			progress.Clear();

			foreach (RequestWorker worker in workers)
				worker.Join();

			//TODO: Implement proper shutdown
			Distributor.Dispose();
			distributor.Join();
			Shutdown();
		}

		/// <summary>
		/// Sets up the log file and advancing writer for said log files.
		/// </summary>
		private static void InitLogger()
		{
			// Setup some extra colors
			Logger.Highlighters.Add(new Highlighter(
				// HTTP methods
				new[] { "GET", "HEAD", "POST", "PUT", "DELETE", "CONNECT", "TRACE", "PATCH", "OPTIONS" },
				new[] { ConsoleColor.Yellow }
			));
			Logger.Highlighters.Add(new Highlighter(
				// Master and Slave keywords (the (?<!\x00\s) prevents the class names from being colored aswell)
				new Regex(@"(?:\W|^)(?<!\x00\s)(master|slave)(?:\W|$)", RegexOptions.IgnoreCase),
				ConsoleColor.DarkYellow
			));

			// Update the log level of the console logger with the loglevel in the config
			Log.Children.ElementAt(0).LogLevel = Level.GetLevel(LoggingConfig.ConsoleLogLevel);
			Log.Children.ElementAt(0).UseConsoleHighlighting = LoggingConfig.ConsoleHighlighting;

#if RELEASE
			// Create the log directory and any parent folders
			Directory.CreateDirectory(LoggingConfig.LogDir);

			// This writer will dispose and compress the log files every day at midnight.
			var logWriter = new AdvancingWriter(Path.Combine(LoggingConfig.LogDir, "server_{2}.log"), LoggingConfig.LogArchivePeriod)
			{
				Compression = LoggingConfig.LogfileCompression,
				Archive = Path.Combine(LoggingConfig.LogDir, "{0:D} ({2}).zip"),
			};

			// Adds a line at the end of the current log mentioning which file continues the log.
			static void OnNextLogFile(object sender, AdvancingWriter.FileAdvancingEventArgs e)
			{
				var writer = sender as AdvancingWriter;

				File.AppendAllText(e.OldFile, $"{writer.NewLine}Continued in another log file");
			}
			logWriter.Advancing += OnNextLogFile;

			// Create new child logger that only writes to the log file
			Log.Attach(new Logger(Level.GetLevel(LoggingConfig.LogLevel), logWriter)
			{
				Name = "Logger (File)",
				Format = Log.Format
			});
#endif
		}

		/// <summary>
		/// Initializes the database.
		/// </summary>
		/// Note: Split into its own function to allow for unit testing to use it as well.
		public static void InitDatabase(SQLiteAdapter database)
		{
			//Create tables if they don't already exist.
			database.CreateTableIfNotExists<ExampleModel>();
			database.CreateTableIfNotExists<User>();
			database.CreateTableIfNotExists<Session>();
			database.CreateTableIfNotExists<Chatroom>();
			database.CreateTableIfNotExists<Chatlog>();
			database.CreateTableIfNotExists<ChatroomMembership>();

			//Create Admin account if it doesn't exist already;
			if (database.Select<User>("Email = 'Administrator'").FirstOrDefault() == null)
			{
				var admin = new User("Administrator", AuthenticationConfig.AdministratorPassword)
				{
					PermissionLevel = PermissionLevel.Admin
				};
				database.Insert(admin);
			}

			//Create default channel if none exist
			if (database.Select<Chatroom>().Count() == 0)
			{
				database.Insert(new Chatroom()
				{
					Name = "General",
					Private = false,
				});
			}
		}

		/// <summary>
		/// Terminates this program with the specified <paramref name="exitCode"/>;
		/// </summary>
		/// <param name="exitCode">Optional exitcode to terminate this program with.</param>
		public static void Shutdown(int exitCode = 0)
		{
			Cleanup();
			Environment.Exit(exitCode);
		}

		public static ProgressBar.Color InitialConsoleColor = new ProgressBar.Color();
		private static void Cleanup()
		{
			Log?.Dispose();
			Database?.Dispose();

			Log = null;
			Database = null;

			// Prevents the console from changing color indefinitely because of a crash
			InitialConsoleColor.Apply();
		}
	}
}
