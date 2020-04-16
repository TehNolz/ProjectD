using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Text;

namespace Webserver.LoadBalancer
{
	/// <summary>
	/// Represents a message which can be sent to a server.
	/// </summary>
	public class Message
	{
		/// <summary>
		/// The unique ID associated with this message. Used for replying to messages that require an answer. Null if no answer is required.
		/// </summary>
		public string ID { get; private set; } = null;
		/// <summary>
		/// The ServerConnection that received this message. Null if this message was constructed locally.
		/// </summary>
		public readonly ServerConnection Connection;
		/// <summary>
		/// The message type. Used to determine how this message should be processed.
		/// </summary>
		public readonly MessageType Type;
		/// <summary>
		/// The data that this message contains.
		/// </summary>
		public dynamic Data { get; private set; }
		
		/// <summary>
		/// Initializes a new instance of <see cref="Message"/> with the specified type and data.
		/// </summary>
		/// <param name="type">The type of the message indicating how it should be processed.</param>
		/// <param name="data"></param>
		public Message(MessageType type, object data)
		{
			Type = type;
			Data = data;
		}

		/// <summary>
		/// Converts a received server communication message into a Message object.
		/// </summary>
		/// <param name="buffer">The byte array containing the message.</param>
		public Message(byte[] buffer, ServerConnection connection = null)
		{
			Connection = connection;

			//Convert the buffer to JObject
			var json = JObject.Parse(Encoding.UTF8.GetString(buffer));

			//Check if all necessary keys are present.
			if (!json.TryGetValue<string>("Type", out JToken typeValue) ||
				!json.TryGetValue<string>("MessageID", out JToken IDValue) ||
				!json.TryGetValue<JToken>("Data", out JToken dataValue))
			{
				throw new JsonReaderException("Invalid server JSON: missing/invalid keys");
			}

			//Assign values
			Type = Enum.Parse<MessageType>((string)typeValue);
			ID = (string)IDValue;

			//Deserialize data if necessary
			if (dataValue.Type != JTokenType.Null)
			{
				Data = JsonConvert.DeserializeObject(dataValue.ToString(), NetworkUtils.JsonSettings);
			}
		}

		/// <summary>
		/// Get a byte representation of this message.
		/// </summary>
		/// <returns></returns>
		public byte[] GetBytes()
		{
			return Encoding.UTF8.GetBytes(new JObject() {
				{ "Type", Type.ToString() },
				{ "Data", Data == null? null : JsonConvert.SerializeObject(Data, NetworkUtils.JsonSettings) },
				{ "MessageID", ID }
			}.ToString(Formatting.None));
		}

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
		/// <param name="timeout">The amount of time in milliseconds to wait for a reply. If no reply is received in time, a SocketException with the TimedOut error code is thrown.</param>
		/// <returns></returns>
		public Message SendAndWait(ServerConnection connection, int timeout = 500)
		{
			ID = Guid.NewGuid().ToString();
			return connection.SendAndWait(this, timeout);
		}

		/// <summary>
		/// Send a reply to this message. Only works if this message came from another server.
		/// </summary>
		/// <param name="data">The data to be transmitted. Can be any serializable object</param>
		public void Reply(dynamic data)
		{
			if(ID == null)
				throw new InvalidOperationException("Message was either constructed locally or doesn't require a reply.");
			Data = data;
			Connection.Send(this);
		}
	}

	/// <summary>
	/// Enum of message types used for internal server communication.
	/// </summary>
	public enum MessageType
	{
		// Load balancer message types
		Timeout,
		Discover,
		DiscoverResponse,
		Register,
		RegisterResponse,
		NewServer,

		// Database replication message types
		QueryInsert,
		QueryUpdate,
		QueryDelete
	}
}
