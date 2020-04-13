using Config;

using System.Collections.Generic;

namespace Webserver
{
	/// <summary>
	/// Configuration classes for use with the Config module. These classes are used to create the Config.json,
	/// and to find missing values within it.
	/// <para/>
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
	/// JSON configuration section regarding miscellaneous settings such as integrity verification.
	/// <para/>
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
	/// <para/>
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
}
