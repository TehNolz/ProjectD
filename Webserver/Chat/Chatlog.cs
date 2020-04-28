using Database.SQLite.Modeling;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using Webserver.Models;

namespace Webserver.Chat
{
	public class Chatlog
	{
		/// <summary>
		/// This message's unique ID.
		/// </summary>
		[Primary]
		public int? ID { get; set; }

		/// <summary>
		/// The ID of the user who wrote this message.
		/// </summary>
		[ForeignKey(typeof(User))]
		public Guid User { get; set; }

		/// <summary>
		/// The ID of the chatroom this message was written in.
		/// </summary>
		[ForeignKey(typeof(Chatroom))]
		public Guid Chatroom { get; set; }

		/// <summary>
		/// The date this message was written on.
		/// </summary>
		public DateTime Date { get; set; }

		/// <summary>
		/// The text this message contains.
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// Create a new log message.
		/// </summary>
		/// <param name="user">The user who wrote this message</param>
		/// <param name="chatroom">The chatroom this message was written in.</param>
		/// <param name="text">The text this message contains.</param>
		public Chatlog(User user, Chatroom chatroom, string text) : this(user, chatroom, text, DateTime.Now) { }
		/// <summary>
		/// Create a new log message.
		/// </summary>
		/// <param name="user">The user who wrote this message</param>
		/// <param name="chatroom">The chatroom this message was written in.</param>
		/// <param name="text">The text this message contains.</param>
		/// <param name="date">The date this message was written on.</param>
		public Chatlog(User user, Chatroom chatroom, string text, DateTime date)
		{
			User = (user ?? throw new ArgumentNullException(nameof(user))).ID;
			Chatroom = (chatroom ?? throw new ArgumentNullException(nameof(chatroom))).ID;
			Text = text ?? throw new ArgumentNullException(nameof(text));
			Date = date;
		}

		/// <summary>
		/// Constructor for SQLiteAdapter. Do not use.
		/// </summary>
		public Chatlog() { }

		/// <summary>
		/// Get a JSON representation of this message.
		/// </summary>
		public JObject GetJson() => new JObject() {
			{ "ID", ID },
			{ "User", User },
			{"Chatroom", Chatroom },
			{"Date", Date },
			{"Text", Text }
		};

		/// <summary>
		/// Get a chunk of a chatroom's chat history.
		/// </summary>
		/// <param name="startID"></param>
		/// <param name="amount"></param>
		/// <returns></returns>
		public static IEnumerable<Chatlog> GetChatlog(Chatroom chatroom, int startID, int amount = 50)
		{
			if (chatroom is null)
				throw new ArgumentNullException(nameof(chatroom));
			if (startID < 1)
				throw new ArgumentException("StartID must be positive");
			if (amount < 0)
				throw new ArgumentException("Amount must be positive.");
			return ChatManagement.Database.Select<Chatlog>("Chatroom = @chatroom AND ID < @startID LIMIT @amount", new { chatroom = chatroom.ID, startID, amount });
		}

		/// <summary>
		/// Get the ID of the last message written in a chatroom. Used as a starting point for GetChatlog
		/// </summary>
		/// <param name="chatroom"></param>
		/// <returns></returns>
		public static Chatlog GetLastID(Chatroom chatroom)
		{
			if (chatroom is null)
				throw new ArgumentNullException(nameof(chatroom));

			//Lol what is "MAX"? Never heard of it /s
			return ChatManagement.Database.Select<Chatlog>("Chatroom = @chatroom ORDER BY ID LIMIT 1", new{ chatroom = chatroom.ID }).FirstOrDefault();
		}
	}
}
