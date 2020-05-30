using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

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
			Chatroom chatroom = Chat.Database.Select<Chatroom>("ID = @id", new { id = chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Get all users who were part of this chatroom so that we can inform them about the deletion later;
			IEnumerable<Guid> users = chatroom.GetUsers();

			//Delete the chatroom and all associated data
			Chat.Database.Delete<ChatroomMembership>("ChatroomID = @ID", new { chatroom.ID });
			Chat.Database.Delete<Chatlog>("Chatroom = @ID", new { chatroom.ID });
			Chat.Database.Delete(chatroom);

			//Inform all relevant clients about the deletion.
			BroadcastChatMessage(TargetType.Users, users, new ChatMessage(MessageType.ChatroomDeleted, chatroom.ID.ToString()));
		}
	}
}
