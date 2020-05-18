using Config;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Webserver.Config
{
	/// <summary>
	/// Configuration classes for use with the Config module. These classes are used to create the Config.json,
	/// and to find missing values within it.
	/// This class' values will be replaced by the values from Config.json on startup.
	/// </summary>
	[ConfigSection]
	internal static class BalancerConfig
	{
		[Comment("The port the load balancer will use for internal communication.")]
		public static int BalancerPort = 12000;
		[Comment("The port new instances of the server will use for master discovery.")]
		public static int DiscoveryPort = 12001;
		[Comment("The addresses the balancer will bind to. Only the first available address will be used. If empty, the balancer will use the internet-facing address.")]
		public static List<string> IPAddresses = new List<string>();
		[Comment("The port each slave webserver will use to listen for incoming traffic from the load balancer.")]
		public static int HttpRelayPort = 12002;
		[Comment("The port the load balancer will use to listen for incoming traffic from clients.")]
		public static int HttpPort = 80;
	}

	/// <summary>
	/// Configuration settings for user authentication
	/// </summary>
	[ConfigSection]
	internal static class AuthenticationConfig
	{
		[Comment("The time in seconds until a session becomes invalid if left alone for too long while the \"Keep me signed in\" checkbox was enabled during login. Default is 604800, equal to 7 days.")]
		public static int SessionTimeoutLong = 604800;
		[Comment("The time in seconds until a session becomes invalid if left alone for too long. Default is 7200, equal to 2 hours.")]
		public static int SessionTimeoutShort = 7200;
		[Comment("The regex that all user passwords must match. Default is at least 10 characters, containing at least one letter, one number, and one special character.")]
		public static string PasswordRegex = new Regex(@"^(?=.*[A-Za-z])(?=.*\d)(?=.*[@$!%*#?&])[A-Za-z\d@$!%*#?&]{10,}$").ToString();
		[Comment("The password for the Administrator account. MUST be kept confidential.")]
		public static string AdministratorPassword = "W@chtw00rd";
	}

	/// <summary>
	/// JSON configuration section regarding miscellaneous settings such as integrity verification.
	/// This class' values will be replaced by the values from Config.json on startup.
	/// </summary>
	[ConfigSection]
	internal static class MiscConfig
	{
		[Comment("Whether the automatic file integrity check should be enabled. If true, the system will automatically check for file corruption within the wwwroot folder.")]
		public static bool VerifyIntegrity = true;
	}

	/// <summary>
	/// JSON configuration section for the webserver functionality such as thread count.
	/// This class' values will be replaced by the values from Config.json on startup.
	/// </summary>
	[ConfigSection]
	internal static class WebserverConfig
	{
		[Comment("The path to the folder that contains the webpages and other resources. Any file in this folder will be accessible through the webserver.")]
		public static string WWWRoot = "./wwwroot";

		[Comment("The amount of worker threads the server will use. More threads means that more simultaneous requests can be processed, but increases hardware usage.")]
		public static int WorkerThreadCount = 5;
	}

	[ConfigSection]
	internal static class ChatConfig
	{
		[Comment("The regex that all chatroom names must match.")]
		public static string ChatroomNameRegex = @"[A-Za-z0-1 !@#$%^&*()_/+\-=]";
		[Comment("Allow users to change their username")]
		public static bool AllowUsernameChange = true;
	}

	/// <summary>
	/// Handles database settings such as backup frequency and backup file location.
	/// </summary>
	[ConfigSection]
	internal static class DatabaseConfig
	{
		[Comment("Defines the time between database backups.")]
		public static TimeSpan BackupPeriod = TimeSpan.FromDays(2);
		[Comment("The path to the directory for database backups. This folder will be created automatically.")]
		public static string BackupDir = "Backups";
		[Comment("Defines whether the database backups are shrunk and compressed into a .zip file.\n" +
			"Note: This resizes the backup files and may lead to fragmentation on hard drive disks.")]
		public static bool CompressBackups = true;
		[Comment("Sets the chunk size (in bytes) used to transfer database backups between servers. Accepts decimal and binary byte units. (e.g. 2 KiB, 0.5MB, 2E+2 B, 1024)")]
		public static string BackupTransferChunkSize = "2KiB";
		[Comment("Sets the amount of database changes that are sent at once when a server is synchronizing it's database with the master server.\n" +
			"Higher chunk sizes will keep the master server too busy with sending one message, whereas lower chunk sizes will lead to increased I/O time.")]
		public static uint SynchronizeChunkSize = 800;
	}
}
