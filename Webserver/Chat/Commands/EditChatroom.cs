using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Webserver.Config;

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
				!Guid.TryParse(rawChatroomID, out Guid chatroomID)
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData, "Missing keys");
				return;
			}

			//Check if the specified chatroom exists
			Chatroom chatroom = Chat.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Change name if necessary
			if (json.TryGetValue("Name", out string name) && Regex.IsMatch(name, ChatConfig.ChatroomNameRegex))
				chatroom.Name = name;

			//Set optional fields
			foreach (KeyValuePair<string, JToken> x in json)
			{
				if (x.Key == "ID" || x.Key == "Name")
				{
					continue;
				}
				PropertyInfo prop = chatroom.GetType().GetProperty(x.Key);
				if (prop == null)
				{
					continue;
				}
				dynamic Value = x.Value.ToObject(prop.PropertyType);
				prop.SetValue(chatroom, Value);
			}

			//Save the changes to the database and inform all relevant clients about it.
			Chat.Database.Update(chatroom);
			BroadcastChatMessage(TargetType.Users, chatroom.GetUsers(), new ChatMessage(MessageType.ChatroomUpdated, chatroom.GetJson()));
		}
	}
}
