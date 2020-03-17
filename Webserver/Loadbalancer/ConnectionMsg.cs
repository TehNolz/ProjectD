using System.Collections.Generic;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Webserver.LoadBalancer {
	public static class ConnectionMsg {
		/// <summary>
		/// Send periodically to monitor uptime
		/// </summary>
		public static readonly JObject Heartbeat = new JObject() { { "Type", "HEARTBEAT" } };
		/// <summary>
		/// Send when trying to discover the current master. The server will be automatically registered with whatever slave answers.
		/// </summary>
		public static readonly JObject Discover = new JObject() { { "Type", "DISCOVER" } };
		/// <summary>
		/// Send by the master in response to a discover message
		/// </summary>
		public static readonly JObject Master = new JObject() { { "Type", "MASTER" } };
		/// <summary>
		/// Send by the master when a slave doesn't respond to a heartbeat in time
		/// </summary>
		/// <param name="Slave">The slave that timed out</param>
		/// <returns></returns>
		public static JObject Timeout(IPEndPoint Slave) => new JObject() { { "Type", "TIMEOUT" }, { "Slave", Slave.Address.ToString() }, { "Port", Slave.Port } };
		/// <summary>
		/// A confirmation message, which is sent in response to a request by another server.
		/// </summary>
		/// <param name="Operation">The operation to confirm</param>
		/// <param name="EP">The endpoint that sent the request. Note: If the wrong endpoint is used, the message will be ignored by the target server.</param>
		/// <returns></returns>
		public static JObject Confirm(string Operation, IPEndPoint EP) => new JObject() { 
			{ "Type", "OK" }, 
			{ "Destination", EP.ToString() },
			{ "Operation", Operation }
		};
	}
}
