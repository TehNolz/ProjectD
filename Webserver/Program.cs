using Config;

using Database.SQLite;

using Logging;
using Logging.Highlighting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
		public const string ConfigName = "Config.json";
		public const string DatabaseName = "Database.db";
		public static ServerDatabase Database;

		public static Logger Log { get; private set; }

		private static FileSystemWatcher configWatcher;
		private static JObject prevConfig;

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
			if (!File.Exists(ConfigName))
				ConfigFile.Write(ConfigName);

			if (ConfigFile.Load(ConfigName) is int missing && missing > 0)
			{
				ConfigFile.Write(ConfigName);
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
			progress.Draw(progress.MinProgress);

			//Check for duplicate network ports. Each port setting needs to be unique as we can't bind to one port multiple times.
			var ports = new List<int>() { BalancerConfig.BalancerPort, BalancerConfig.DiscoveryPort, BalancerConfig.HttpRelayPort, BalancerConfig.HttpPort };
			if (ports.Distinct().Count() != ports.Count)
			{
				progress.Clear();
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
					progress.Clear();
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

			// Add file watcher to the config file to allow for live config updates.
			InitConfigWatcher();

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
		/// Sets up the <see cref="FileSystemWatcher"/> that watches the config file for any updates.
		/// </summary>
		private static void InitConfigWatcher()
		{
			// Create a backup of the config in order to create the JsonDiffs later
			using (StreamReader reader = File.OpenText(ConfigName))
				prevConfig = (JObject)JsonConvert.DeserializeObject(reader.ReadToEnd());

			// Initialize the file watcher and subscribe the event handler
			configWatcher = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(ConfigName)), Path.GetFileName(ConfigName))
			{
				EnableRaisingEvents = true
			};
			configWatcher.Changed += fileChanged;

			/// Creates JsonDiffs and passes them to OnConfigChange
			static void fileChanged(object sender, FileSystemEventArgs e)
			{
				// Only continue if the changed file is actually the config file (may always be true due to the filter but whatever)
				if (e.FullPath != Path.GetFullPath(ConfigName))
					return;

				JObject newConfig;
				try
				{
					// Try to read the file to also test if if it is no longer in use.
					using (var reader = new StreamReader(File.Open(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
						newConfig = (JObject)JsonConvert.DeserializeObject(reader.ReadToEnd());
					
					// If the file is empty it is probably still being written, so return and hope for another invocation.
					if (newConfig == null)
						return;

					// Validate the new file
					if (ConfigFile.Load(ConfigName) is int missing && missing > 0)
					{
						ConfigFile.Write(ConfigName);
						Log.Error($"{missing} configuration setting(s) are missing. The missing settings have been inserted.");

						// Read the file again
						using StreamReader reader = File.OpenText(e.FullPath);
						newConfig = (JObject)JsonConvert.DeserializeObject(reader.ReadToEnd());
					}
				}
				catch (IOException)
				{
					// Stop if the file can't be read (and hope for another invocation)
					return;
				}

				// Apply changes
				var diff = new JsonDiff(prevConfig, newConfig);

				// Skipp applying the diff if it is empty
				if (diff.Added.Count == 0 && diff.Changed.Count == 0 && diff.Removed.Count == 0)
					return;

				OnConfigChange(diff);
				
				Log.Info($"Reloaded configuration file. (some changes may require a restart)");
				prevConfig = newConfig;
			}
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

		/// <summary>
		/// Handles changes made in the config file.
		/// </summary>
		/// <param name="_">The changes between the old and new config files.</param>
		private static void OnConfigChange(JsonDiff _)
		{
			// Always re-apply the logger settings
			if (Log.Children.ElementAtOrDefault(0) is Logger consoleLogger)
			{
				consoleLogger.UseConsoleHighlighting = LoggingConfig.ConsoleHighlighting;
				consoleLogger.LogLevel = Level.GetLevel(LoggingConfig.ConsoleLogLevel);
			}
			if (Log.Children.ElementAtOrDefault(1) is Logger fileLogger)
			{
				fileLogger.LogLevel = Level.GetLevel(LoggingConfig.LogLevel);
				(fileLogger.OutputStreams.First() as AdvancingWriter).Compression = LoggingConfig.LogfileCompression;
			}
		}

		public static ProgressBar.Color InitialConsoleColor = new ProgressBar.Color();
		/// <summary>
		/// Cleans up various resources before the application exits.
		/// </summary>
		private static void Cleanup()
		{
			configWatcher?.Dispose();
			Log?.Dispose();
			Database?.Dispose();

			configWatcher = null;
			Log = null;
			Database = null;

			// Prevents the console from changing color indefinitely because of a crash
			InitialConsoleColor.Apply();
		}
	}
}
