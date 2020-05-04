using Database.SQLite;
using Database.SQLite.Modeling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Webserver.LoadBalancer;
using Webserver.Models;

namespace Webserver.Chat
{
	/// <summary>
	/// Management class for chat system.
	/// </summary>
	public static class ChatManagement
	{
		/// <summary>
		/// Database connection for chatroom management. Do not use outside of chatroom system!
		/// </summary>
		public static SQLiteAdapter Database = new SQLiteAdapter(Program.DatabaseName);
	}

	/// <summary>
	/// Represents a chatroom within the system.
	/// </summary>
	public class Chatroom
	{
		/// <summary>
		/// List of connections to this chatroom.
		/// </summary>
		public ConcurrentBag<ChatConnection> Connections = new ConcurrentBag<ChatConnection>();

		/// <summary>
		/// The name of this chatroom.
		/// </summary>
		[NotNull]
		public string Name { get; set; }

		/// <summary>
		/// The unique ID of this chatroom.
		/// </summary>
		[Primary]
		public Guid ID { get; set; } = Guid.NewGuid();

		/// <summary>
		/// Whether this chatroom allows others to join.
		/// </summary>
		[NotNull]
		public bool Private { get; set; } = false;

		/// <summary>
		/// Check if the specified user is allowed to access this chatroom.
		/// </summary>
		/// <param name="user">The user</param>
		/// <returns></returns>
		public bool CanUserAccess(SQLiteAdapter database, User user) => !Private || database.Select<ChatroomMembership>($"ChatroomID = @chatroomid AND UserID = @userid", new { chatroomid = ID, userid = user.ID }).Any();

		/// <summary>
		/// Gets all chatrooms that are accessible by the specified user.
		/// </summary>
		/// <param name="database"></param>
		/// <param name="user"></param>
		/// <returns></returns>
		public static IEnumerable<Chatroom> GetAccessableByUser(SQLiteAdapter database, User user)
		{
			//Get all public rooms
			IEnumerable<Chatroom> result = database.Select<Chatroom>("Private = 0");

			//Get all private rooms this user can access
			IEnumerable<Guid> IDs = from CM in database.Select<ChatroomMembership>("UserID = @ID", new { user.ID }) select CM.ChatroomID;
			//TODO Optimize to use less Select calls
			foreach(Guid ID in IDs)
			{
				result.Append(database.Select<Chatroom>("ID = @ID", new { ID }).First());
			}

			return result;
		}

		/// <summary>
		/// Get the last chat message written in this chatroom.
		/// </summary>
		/// <returns></returns>
		public Chatlog GetLastMessage() => Chatlog.GetLastID(this);

		/// <summary>
		/// Get a chunk of this chatroom's chat history.
		/// </summary>
		/// <param name="start"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public List<Chatlog> GetChatHistory(int start, int amount) => Chatlog.GetChatlog(this, start, amount).ToList();

		/// <summary>
		/// Retrieves the GUID of all users who are part of this chatroom.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<Guid> GetUsers() => Private ?
				from CM in ChatManagement.Database.Select<ChatroomMembership>("ChatroomID = @ID", new { ID }) select CM.UserID :
				from U in ChatManagement.Database.Select<User>() select U.ID;

		/// <summary>
		/// Returns a JSON representation of this chatroom.
		/// </summary>
		/// <returns></returns>
		public JObject GetJson() => new JObject()
			{
				{"Name", Name},
				{"Private", Private },
				{"ID", ID },
				{"LastMessage", GetLastMessage()?.ID }
			};

		/// <summary>
		/// Get the JSON representation of multiple chatrooms.
		/// </summary>
		/// <param name="chatrooms"></param>
		/// <returns></returns>
		public static JArray GetJsonBulk(IEnumerable<Chatroom> chatrooms)
		{
			var result = new JArray();
			foreach (Chatroom room in chatrooms)
				result.Add(room.GetJson());
			return result;
		}

		/// <summary>
		/// Event handler for ChatroomUpdate events. Sends received chatroom updates to all connected clients.
		/// </summary>
		/// <param name="message"></param>
		public static void ChatroomUpdateHandler(ServerMessage message)
		{

			//Ignore everything other than messages with type ChatMessage
			if (message.Type != MessageType.ChatroomUpdate)
				return;

			var data = (JObject)message.Data;
			if (!data.ContainsKey("Users"))
				throw new ArgumentException("Missing Users key");

			//Send updated chatroom info to all relevant clients.
			foreach (ChatConnection connection in from AC in ChatConnection.ActiveConnections where data["Users"].ToList().Contains(AC.User.ID) select AC)
				connection.Send(new ChatMessage(MessageType.ChatroomUpdate, GetJsonBulk(connection.Chatrooms)));
		}
	}

	/// <summary>
	/// Represents a user's presence in a private chatroom.
	/// </summary>
	public class ChatroomMembership
	{
		[Primary]
		[ForeignKey(typeof(User))]
		public Guid UserID { get; set; }
		[Primary]
		[ForeignKey(typeof(Chatroom))]
		public Guid ChatroomID { get; set; }
	}
}