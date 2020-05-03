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
			var result = chatroom.GetJson();
			result.Add("Users", new JArray(chatroom.GetUsers()));
			ChatManagement.Database.Update(chatroom);
			var serverMessage = new ServerMessage(MessageType.ChatroomUpdate, result);
			ServerConnection.Broadcast(serverMessage);
			Chatroom.ChatroomUpdateHandler(serverMessage);
		}
	}
}
