
using Newtonsoft.Json;

using System;
using System.Collections.Generic;

namespace Webserver.LoadBalancer
{
	/// <summary>
	/// Represents a message which can be sent to a server.
	/// </summary>
	public class ServerMessage : Message
	{

		/// <summary>
		/// The <see cref="ServerConnection"/> that received this message. Null if this message was constructed locally.
		/// </summary>
		[JsonIgnore]
		public ServerConnection Connection { get; set; }

		/// <summary>
		/// Initializes a new instance of <see cref="ServerMessage"/> with the specified type and data.
		/// </summary>
		/// <param name="type">The type of the message indicating how it should be processed.</param>
		/// <param name="data"></param>
		public ServerMessage(MessageType type, object data) : base(type, data) { }

		/// <summary>
		/// Send this message to the specified server.
		/// </summary>
		/// <param name="Connection">The ServerConnection to send this message to.</param>
		public void Send(ServerConnection connection) => connection.Send(this);
		/// <summary>
		/// Send this message to multiple servers.
		/// </summary>
		/// <param name="servers">A list of ServerConnections that this message will be send to.</param>
		public void Send(List<ServerConnection> servers) => ServerConnection.Send(servers, this);
		/// <summary>
		/// Send this message to all known servers.
		/// </summary>
		public void Broadcast() => ServerConnection.Broadcast(this);

		/// <summary>
		/// Send a message to the specified server and wait for a response.
		/// </summary>
		/// <param name="connection">The server to send the message to.</param>
		/// <returns></returns>
		public ServerMessage SendAndWait(ServerConnection connection)
		{
			ID = Guid.NewGuid();
			return connection.SendAndWait(this);
		}

		/// <summary>
		/// Send a reply to this message. Only works if this message came from another server.
		/// </summary>
		/// <param name="data">The data to be transmitted. Can be any serializable object</param>
		public void Reply(dynamic data)
		{
			if (ID == null)
				throw new InvalidOperationException("Message was either constructed locally or doesn't require a reply.");
			Data = data;
			Connection.Send(this);
		}
	}
}
