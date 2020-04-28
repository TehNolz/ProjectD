using Database.SQLite;
using Database.SQLite.Modeling;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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
		public bool CanUserAccess(SQLiteAdapter database, User user) => !Private || database.Select<ChatroomMembership>("Chatroom = @chatroomid AND User = @userid", new { chatroomid = ID, userid = user.ID }).Any();

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
	}

	public class ChatroomMembership
	{
		public User User;
		public Chatroom Chatroom;
	}
}
