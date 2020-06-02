using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Webserver.Chat.Commands
{
	[CommandName("ChatMessage")]
	public class UserMessage : ChatCommand
	{
		/// <summary>
		/// Accepts incoming chat messages from the client and verifies them, before broadcasting them to all clients (including those on remote servers).
		/// </summary>
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (!json.TryGetValue("ChatroomID", out string rawChatroomID) ||
				!Guid.TryParse(rawChatroomID, out Guid chatroomID) ||
				!json.TryGetValue("MessageText", out string messageText)
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData);
				return;
			}

			//Check if the specified chatroom exists
			Chatroom chatroom = Chat.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Check if the user is allowed to access this chatroom.
			if (!chatroom.CanUserAccess(Chat.Database, Message.User))
			{
				Message.Reply(ChatStatusCode.ChatroomAccessDenied);
				return;
			}

			//At this point we're 100% certain the user is allowed to send a message to this channel. So let's get on with it;
			//Convert the message into a Chatlog object and store it in the database
			var logMessage = new Chatlog(Message.User, chatroom, messageText);
			Chat.Database.Insert(logMessage);

			BroadcastChatMessage(TargetType.Chatrooms, new List<Guid>() { chatroom.ID }, new ChatMessage(MessageType.ChatMessage, logMessage.GetJson()));
		}
	}
}
