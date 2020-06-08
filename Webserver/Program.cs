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

			try
			{
				if (ConfigFile.Load(ConfigName) is int missing && missing > 0)
				{
					ConfigFile.Write(ConfigName);
					Log.Warning($"{missing} configuration setting(s) are missing. The missing settings have been inserted.");
				}
			}
			catch (JsonReaderException e)
			{
				// Exit if the config file could not be read
				Log.Error(string.Concat($"Could not load configuration file", ": ", e.Message), e);
				Log.Error($"Please check '{ConfigName}' for problems or delete it to generate a new configuration file.");
				Console.ReadLine();
				return;
			}

			// Initialize the remaining components of the logger. (Stuff about the logger that uses the config)
			InitLogger();

			// Create progressbar for server config
			var progress = new ProgressBar()
			{
				Prefix = "Configuring   [{3,-4:P0}]",
				MaxProgress = 14
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
			progress.Increment(1);

			//If the VerifyIntegrity config option is enabled, check all files in wwwroot for corruption.
			//If at least one checksum mismatch is found, pause startup and show a warning.
			if (MiscConfig.VerifyIntegrity)
			{
				Log.Config("Checking file integrity...");
				int Diff = Integrity.VerifyIntegrity(WebserverConfig.WWWRoot);
				progress.Increment(0.5);
				if (Diff > 0)
				{
					progress.Clear();
					Log.Error($"Integrity check failed. Validation failed for {Diff} file(s).");
					Log.Error("Some files may be corrupted. If you continue, all checksums will be recalculated.");
					Log.Error("Press enter to continue.");
					Console.ReadLine();
					Integrity.VerifyIntegrity(WebserverConfig.WWWRoot, true);
				}
				else
					Log.Config("No integrity issues found.");
				progress.Increment(0.5);
			}
			else
				progress.Increment(1);

			//Crawl through the wwwroot folder to find all resources.
			Resource.Crawl(WebserverConfig.WWWRoot);
			progress.Increment(2);

			//Parse Redirects.config to register all HTTP redirections.
			Redirects.LoadRedirects("Redirects.config");
			Log.Config($"Registered {Redirects.RedirectDict.Count} redirections");
			progress.Increment(1);

			// Initialize database
			Database = ServerDatabase.CreateConnection(DatabaseName);
			Database.BroadcastChanges = false;
			progress.Increment(1);
			InitDatabase(Database);
			progress.Increment(1);

			//Register all API endpoints, chat commands
			APIEndpoint.DiscoverEndpoints();
			progress.Increment(0.5);
			ChatCommand.DiscoverCommands();
			progress.Increment(0.5);

			//Start load balancer
			IPAddress localAddress;
			try
			{
				localAddress = Balancer.Init();
				Log.Config("Started load balancer.");
				progress.Increment(1);
			}
			catch (Exception e)
			{
				progress.Clear();
				e = e.InnerException ?? e;
				Log.Error("The server could not start: " + e.Message, e);
				Console.ReadLine();
				return;
			}

			InitDatabaseContents(Database);
			progress.Increment(1);

			//Start distributor and worker threads
			RequestWorker.Queue = new BlockingCollection<ContextProvider>();
			var workers = new List<RequestWorker>();
			for (int i = 0; i < WebserverConfig.WorkerThreadCount; i++)
			{
				var worker = new RequestWorker(Database.NewConnection());
				workers.Add(worker);
				worker.Start();
				progress.Increment(1 / (double)WebserverConfig.WorkerThreadCount);
			}
			progress.Increment(1 / (double)WebserverConfig.WorkerThreadCount);

			var distributor = new Thread(() => Distributor.Run(localAddress, BalancerConfig.HttpRelayPort));
			distributor.Start();
			progress.Increment(1);

			if (!Balancer.IsMaster)
			{
				// Ready the server
				Balancer.Ready();
				Log.Config($"Server is ready to accept connections");
			}
			progress.Increment(1);

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
			})
			{ Name = "Program Exiter" }.Start();

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
			configWatcher.Changed += (sender, eventArgs) =>
			{
				configWatcher.EnableRaisingEvents = false;
				fileChanged(sender, eventArgs);
				configWatcher.EnableRaisingEvents = true;
			};

			/// Creates JsonDiffs and passes them to OnConfigChange
			static void fileChanged(object sender, FileSystemEventArgs eventArgs)
			{
				// Only continue if the changed file is actually the config file (may always be true due to the filter but whatever)
				if (eventArgs.FullPath != Path.GetFullPath(ConfigName))
					return;

				JObject newConfig;
				try
				{
					// Try to read the file to also test if if it is no longer in use.
					using (var reader = new StreamReader(File.Open(ConfigName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
						newConfig = (JObject)JsonConvert.DeserializeObject(reader.ReadToEnd());

					// If the file is empty it is probably still being written, so return and hope for another invocation.
					if (newConfig == null)
						return;

					// Validate the new file
					if (ConfigFile.Load(newConfig) is int missing && missing > 0)
					{
						ConfigFile.Write(ConfigName);
						Log.Warning($"{missing} configuration setting(s) are missing. The missing settings have been inserted.");

						// Read the file again
						using (var reader = new StreamReader(File.Open(ConfigName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
							newConfig = (JObject)JsonConvert.DeserializeObject(reader.ReadToEnd());

						// Additional null check since each read may return an empty file
						if (newConfig == null)
							return;
					}
				}
				catch (JsonReaderException e)
				{
					// If the config could not be loaded, rebuild it and return
					Log.Error($"Could not load configuration file: {e.Message}");
					Log.Error($"Repairing '{ConfigName}'...");
					ConfigFile.Write(ConfigName);
					return;
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
		}

		/// <summary>
		/// Inserts the default data into the database.
		/// </summary>
		/// <remarks>
		/// If <paramref name="database"/> is a <see cref="ServerDatabase"/>, then this should be called
		/// after <paramref name="database"/> has synchronized.
		/// </remarks>
		/// <param name="database">The database to fill with default data.</param>
		public static void InitDatabaseContents(SQLiteAdapter database)
		{
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
		/// <param name="diff">The changes between the old and new config files.</param>
		private static void OnConfigChange(JsonDiff diff)
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

			// If the webserver config was changed
			if (diff.Changed.TryGetValue(nameof(WebserverConfig), out JObject webserverConfig))
			{
				// Recalculate checksums if WWWRoot was changed
				if (webserverConfig.ContainsKey(nameof(WebserverConfig.WWWRoot)))
				{
					// Lock to prevent issues like InvalidOperationExceptions when the collection was changed
					lock (Resource.WebPages)
					{
						Log.Fine($"WWWRoot changed, recalculating checksums and rebuilding the page list...");
						Log.Debug(WebserverConfig.WWWRoot);
						Log.Debug(webserverConfig["WWWRoot"].ToString());

						Integrity.VerifyIntegrity(WebserverConfig.WWWRoot, true);
						// Crawl creates the directory if it doesn't exist already
						Resource.WebPages.Clear();
						Resource.WebPages.AddRange(Resource.Crawl(WebserverConfig.WWWRoot));
					}
				}
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
