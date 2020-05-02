using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Text.RegularExpressions;

using Webserver.Config;
using Webserver.LoadBalancer;

namespace Webserver.Chat.Commands
{
	[CommandName("EditChatroom")]
	[Permission(Models.PermissionLevel.Admin)]
	public class EditChatroom : ChatCommand
	{
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (!json.TryGetValue("ChatroomID", out string rawChatroomID) ||
				!Guid.TryParse(rawChatroomID, out Guid chatroomID) ||
				!json.TryGetValue("Setting", out string setting) ||
				!json.ContainsKey("NewValye")
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

			//Switch to the proper setting
			switch (setting)
			{
				//Change the name
				case "Name":
					if(!json.TryGetValue("newValue", out string newName))
					{
						Message.Reply(ChatStatusCode.BadMessageData);
						return;
					}

					//Check if the name matches the regex in the config.
					if (Regex.IsMatch(newName, ChatConfig.ChatroomNameRegex))
						chatroom.Name = newName;
					else
					{
						Message.Reply(ChatStatusCode.BadMessageData);
						return;
					}
					break;

				//Change privacy mode
				case "Private":
					if (!json.TryGetValue("newValue", out bool privacyval))
					{
						Message.Reply(ChatStatusCode.BadMessageData);
						return;
					}

					chatroom.Private = privacyval;
					break;

				default:
					Message.Reply(ChatStatusCode.BadMessageData);
					return;
			}

			//Save the changes to the database and inform all relevant clients about it.
			ChatManagement.Database.Update(chatroom);
			var serverMessage = new ServerMessage(MessageType.ChatroomUpdate, chatroom.GetJson());
			ServerConnection.Broadcast(serverMessage);
			ChatroomUpdateHandler(serverMessage);
		}

		/// <summary>
		/// Event handler for ChatroomUpdate events. Sends received chatroom updates to all connected clients.
		/// </summary>
		/// <param name="message"></param>
		public static void ChatroomUpdateHandler(ServerMessage message)
		{
			var data = (JObject)message.Data;

			//Ignore everything other than messages with type ChatMessage
			if (message.Type != MessageType.ChatroomUpdate)
				return;

			foreach(ChatConnection connection in ChatConnection.ActiveConnections)
			{
				//Check if the client has access to this chatroom
				if (!(bool)data["Private"] && !(from CR in connection.Chatrooms where CR.ID == Guid.Parse((string)data["ID"]) select CR).Any())
					continue;

				var chatMessage = new ChatMessage(MessageType.ChatroomUpdate, message.Data)
				{
					StatusCode = ChatStatusCode.OK
				};
				connection.Send(chatMessage);
			}
		}
	}
}
