using Newtonsoft.Json.Linq;

using System;
using System.Linq;

using Webserver.LoadBalancer;

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
			Chatroom chatroom = ChatManagement.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Check if the user is allowed to access this chatroom.
			if (!chatroom.CanUserAccess(ChatManagement.Database, Message.User))
			{
				Message.Reply(ChatStatusCode.ChatroomAccessDenied);
				return;
			}

			//At this point we're 100% certain the user is allowed to send a message to this channel. So let's get on with it;
			//Convert the message into a Chatlog object and store it in the database
			var logMessage = new Chatlog(Message.User, chatroom, messageText);
			ChatManagement.Database.Insert(logMessage);

			var serverMessage = new ServerMessage(MessageType.Chat, logMessage.GetJson());
			ServerConnection.Broadcast(serverMessage);
			UserMessageHandler(serverMessage);
		}

		/// <summary>
		/// Event handler for ChatMessage events. Sends 
		/// </summary>
		/// <param name="message"></param>
		public static void UserMessageHandler(ServerMessage message)
		{
			//Ignore everything other than messages with type ChatMessage
			if (message.Type != MessageType.Chat)
				return;

			//Broadcast the data to all connected clients.
			foreach (ChatConnection connection in ChatConnection.ActiveConnections)
			{
				var chatMessage = new ChatMessage(MessageType.Chat, message.Data)
				{
					StatusCode = ChatStatusCode.OK
				};
				connection.Send(chatMessage);
			}
		}
	}
}
