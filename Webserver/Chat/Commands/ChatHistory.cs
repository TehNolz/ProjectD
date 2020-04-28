using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Webserver.LoadBalancer;

namespace Webserver.Chat.Commands
{
	[CommandName("ChatHistory")]
	class ChatHistory : ChatCommand
	{
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (!json.TryGetValue("ChatroomID", out string rawChatroomID) ||
				!Guid.TryParse(rawChatroomID, out Guid chatroomID) ||
				!json.TryGetValue("Start", out int start) ||
				!json.TryGetValue("Amount", out int amount)
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData);
				return;
			}

			//Check if the specified chatroom exists
			Chatroom chatroom = ChatManagement.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Check if the Amount and Start fields are correct
			if (amount < 0 || amount > 150)
				Message.Reply(ChatStatusCode.BadMessageData, "Amount out of range");
			if (start < 1)
				Message.Reply(ChatStatusCode.BadMessageData, "Start out of range");

			//Check if the user is allowed to access this chatroom
			if (!chatroom.CanUserAccess(ChatManagement.Database, Message.User))
			{
				Message.Reply(ChatStatusCode.ChatroomAccessDenied);
				return;
			}

			//Send the chat history the user asked for.
			var history = new JArray();
			foreach (Chatlog entry in chatroom.GetChatHistory(start, amount))
				history.Add(new JObject()
				{
					{"ID", entry.ID },
					{"User", entry.User },
					{"Chatroom", entry.Chatroom },
					{"Date", entry.Date },
					{"Text", entry.Text }
				});

			Message.Reply(ChatStatusCode.OK, history);
		}
	}
}
