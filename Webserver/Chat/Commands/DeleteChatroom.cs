using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Text.RegularExpressions;

using Webserver.Config;
using Webserver.LoadBalancer;
using Webserver.Models;

namespace Webserver.Chat.Commands
{
	[CommandName("DeleteChatroom")]
	[Permission(Models.PermissionLevel.Admin)]
	public class DeleteChatroom : ChatCommand
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
			Chatroom chatroom = ChatManagement.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Delete the chatroom from the database and inform all relevant clients about it.
			var result = chatroom.GetJson();
			result.Add("Users", new JArray(chatroom.GetUsers()));
			ChatManagement.Database.Delete(chatroom);
			var serverMessage = new ServerMessage(MessageType.ChatroomUpdate, result);
			ServerConnection.Broadcast(serverMessage);
			Chatroom.ChatroomUpdateHandler(serverMessage);
		}
	}
}
