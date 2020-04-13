using Newtonsoft.Json.Linq;
using System.Net;

namespace Webserver.LoadBalancer
{
	public static class ConnectionMessage {
		/// <summary>
		/// The message that is sent periodically to monitor uptime.
		/// </summary>
		public static readonly JObject Heartbeat = new JObject() { { "$type", "HEARTBEAT" } };
		/// <summary>
		/// The message that is sent when trying to discover the current master instance.
		/// </summary>
		/// <remarks>
		/// The server will automatically be registered with whatever slave answers.
		/// </remarks>
		public static readonly JObject Discover = new JObject() { { "$type", "DISCOVER" } };
		/// <summary>
		/// The message sent by the master in response to a <see cref="Discover"/> message.
		/// </summary>
		public static readonly JObject Master = new JObject() { { "$type", "MASTER" } };

		/// <summary>
		/// Builds and returns a message that is sent to timed out slave instances.
		/// </summary>
		/// <remarks>
		/// Sent by the master when a slave doesn't respond to a <see cref="Heartbeat"/> in time
		/// </remarks>
		/// <param name="Slave">The slave that timed out</param>
		public static JObject Timeout(IPEndPoint Slave) => new JObject() { { "$type", "TIMEOUT" }, { "Slave", Slave.Address.ToString() }, { "Port", Slave.Port } };

		/// <summary>
		/// Builds and returns a confirmation message, which is sent in response to a request by another server.
		/// </summary>
		/// <param name="$ack_type">The operation to confirm</param>
		/// <param name="EP">The endpoint that sent the request. Note: If the wrong endpoint is used, the message will be ignored by the target server.</param>
		public static JObject Confirm(string Operation, IPEndPoint EP) => new JObject() { 
			{ "$type", "ACK" }, 
			{ "$ack_type", Operation },
			{ "$destination", EP.ToString() }
		};
	}
}
