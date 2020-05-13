using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Text;

using Webserver.Models;

namespace Webserver.Chat
{
	/// <summary>
	/// Represents a message between this server and the client.
	/// </summary>
	public class ChatMessage : Message
	{
		/// <summary>
		/// Internal constructor. Do not use.
		/// </summary>
		public ChatMessage(MessageType type, object data) : base(type, data) { }

		/// <summary>
		/// Create a new chat message
		/// </summary>
		/// <param name="command">The command this message will call</param>
		/// <param name="data">The data attached to this message</param>
		public ChatMessage(string command, object data) : base(MessageType.ChatMessage, data)
		{
			Command = command;
		}

		/// <summary>
		/// The command this message will call.
		/// </summary>
		public string Command { get; protected set; }

		/// <summary>
		/// The websocket connection that received this message.
		/// </summary>
		[JsonIgnore]
		public ChatConnection Connection { get; set; }

		/// <summary>
		/// The user who sent this message.
		/// </summary>
		public User User { get; set; }

		/// <summary>
		/// The status code this message has.
		/// </summary>
		public ChatStatusCode StatusCode { get; set; }

		/// <summary>
		/// Send this message to the specified server.
		/// </summary>
		/// <param name="Connection">The ServerConnection to send this message to.</param>
		public void Send(ChatConnection connection) => connection.Send(this);
		/// <summary>
		/// Send this message to multiple servers.
		/// </summary>
		/// <param name="servers">A list of ServerConnections that this message will be send to.</param>
		public void Send(List<ChatConnection> servers) => ChatConnection.Send(servers, this);
		/// <summary>
		/// Send this message to all known servers.
		/// </summary>
		public void Broadcast() => ChatConnection.Broadcast(this);

		/// <summary>
		/// Send a message to the specified server and wait for a response.
		/// </summary>
		/// <param name="connection">The server to send the message to.</param>
		/// <param name="timeout">The amount of time in milliseconds to wait for a reply. If no reply is received in time, a SocketException with the TimedOut error code is thrown.</param>
		/// <returns></returns>
		public ChatMessage SendAndWait(ChatConnection connection, int timeout = 500)
		{
			ID = Guid.NewGuid();
			return connection.SendAndWait(this, timeout);
		}

		/// <summary>
		/// Get this chat message's JSON representation.
		/// </summary>
		/// <returns></returns>
		public override JObject GetJson()
		{
			JObject result = base.GetJson();
			result.Add("StatusCode", (int)StatusCode);
			result.Add("Command", Command);
			return result;
		}

		/// <summary>
		/// Converts a message into a ChatMessage object.
		/// </summary>
		/// <param name="buffer">The byte array containing the message.</param>
		public static ChatMessage FromBytes(byte[] buffer) => FromJson(JObject.Parse(Encoding.UTF8.GetString(buffer)));

		/// <summary>
		/// Convert a JOBject into a ChatMessage, if possible.
		/// </summary>
		/// <param name="json"></param>
		/// <returns></returns>
		public static ChatMessage FromJson(JObject json)
		{
			json["Type"] = MessageType.ChatMessage.ToString();
			if (!json.TryGetValue("Command", out string command))
				throw new JsonReaderException("Invalid JSON: missing Command");
			ChatMessage result = FromJson<ChatMessage>(json);
			result.Command = command;
			return result;
		}

		/// <summary>
		/// Send a reply to this message.
		/// </summary>
		/// <param name="data">The data to be transmitted. Can be any serializable object</param>
		public void Reply(ChatStatusCode statusCode = ChatStatusCode.OK, dynamic data = null)
		{
			StatusCode = statusCode;
			Data = data;
			Flags |= MessageFlags.Reply;
			Connection.Send(this);
		}
	}

	/// <summary>
	/// Status codes for messages sent to the client.
	/// </summary>
	/// <remarks>
	/// Creating our own status codes instead of using HTTP ones allows us to give more specific errors without having to send a description.
	/// </remarks>
	public enum ChatStatusCode
	{
		//System announcements (100-199)

		//The client was a good boy. (200-299)
		OK = 200,

		//The client tried to access something it doesn't have permission for (300-399)
		ChatroomAccessDenied = 300,
		CommandAccessDenied = 301,

		//The client fucked up. (400-499)
		BadMessageType = 400,
		BadMessageData = 401,
		NoSuchChatroom = 402,
		AlreadyExists = 403,

		//We fucked up. (500-599)
		InternalServerError = 500,
	}
}