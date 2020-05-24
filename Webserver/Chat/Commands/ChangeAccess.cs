using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Webserver.Models;
using static Webserver.Chat.ChatCommand;

namespace Webserver.Chat.Commands
{
	[CommandName("ChangeAccess")]
	[Permission(Models.PermissionLevel.Admin)]
	class ChangeAccess : ChatCommand
	{
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (!json.TryGetValue("ChatroomID", out string rawChatroomID) ||
				!Guid.TryParse(rawChatroomID, out Guid chatroomID) ||
				!json.TryGetValue("UserID", out string rawUserID) ||
				!Guid.TryParse(rawUserID, out Guid userID) ||
				!json.TryGetValue("AllowAccess", out bool canAccess)
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData);
				return;
			}

			//Check if the specified chatroom exists
			Chatroom chatroom = Chat.Database.Select<Chatroom>("ID = @chatroomID", new {chatroomID }).FirstOrDefault();
			if (chatroom == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Check if the specified user exists
			User user = Chat.Database.Select<User>("ID = @userID", new { userID }).FirstOrDefault();
			if(user == null)
			{
				Message.Reply(ChatStatusCode.NoSuchChatroom);
				return;
			}

			//Get the ChatroomMembership for this chatroom + user combo if it exists
			ChatroomMembership membership = Chat.Database.Select<ChatroomMembership>("UserID = @userID AND ChatroomID = @chatroomID", new { userID, chatroomID }).FirstOrDefault();

			if (canAccess)
			{
				//User should have acces
				if(membership == null)
				{
					//User doesn't have access yet, so give it.
					membership = new ChatroomMembership() { UserID = userID, ChatroomID = chatroomID };
					Chat.Database.Insert(membership);
					BroadcastChatMessage(TargetType.Users, new List<Guid>() { userID }, (new ChatMessage(MessageType.ChatroomUpdated, chatroom.GetJson())));
				} else
				{
					//User already has access, so do nothing.
					return;
				}
			} 
			else
			{
				//User should be denied access
				if(membership == null)
				{
					//User never had access to begin with, so do nothing.
					return;
				} else
				{
					//User has access, so take it away.
					Chat.Database.Delete(membership);
					BroadcastChatMessage(TargetType.Users, new List<Guid>() { userID }, (new ChatMessage(MessageType.ChatroomDeleted, chatroomID)));
				}
			}
		}
	}
}
