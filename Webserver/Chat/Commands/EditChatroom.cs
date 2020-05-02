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
				!json.ContainsKey("NewValue")
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData, "Missing keys");
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
					if(!json.TryGetValue("NewValue", out string newName))
					{
						Message.Reply(ChatStatusCode.BadMessageData, "Invalid newValue (must be string)");
						return;
					}

					//Check if the name matches the regex in the config.
					if (Regex.IsMatch(newName, ChatConfig.ChatroomNameRegex))
						chatroom.Name = newName;
					else
					{
						Message.Reply(ChatStatusCode.BadMessageData, "Invalid newValue (name regex)");
						return;
					}
					break;

				//Change privacy mode
				case "Private":
					if (!json.TryGetValue("NewValue", out bool privacyval))
					{
						Message.Reply(ChatStatusCode.BadMessageData, "Invalid newValue (must be bool)");
						return;
					}

					chatroom.Private = privacyval;
					break;

				default:
					Message.Reply(ChatStatusCode.BadMessageData, "No such setting");
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

			//Ignore everything other than messages with type ChatMessage
			if (message.Type != MessageType.ChatroomUpdate)
				return;

			var data = (JObject)message.Data;

			foreach(ChatConnection connection in ChatConnection.ActiveConnections)
			{
				//Check if the client has access to this chatroom
				Chatroom room = ChatManagement.Database.Select<Chatroom>("ID = @ID", new { ID = Guid.Parse((string)data["ID"])}).First();
				if(!room.CanUserAccess(ChatManagement.Database, connection.User))
					continue;

				connection.Send(new ChatMessage(MessageType.ChatroomUpdate, Chatroom.GetJsonBulk(connection.Chatrooms)));
			}
		}
	}
}
