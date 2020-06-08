using Newtonsoft.Json.Linq;

using System.Linq;

namespace Webserver.Chat.Commands
{
	[CommandName("ChangeUserStatus")]
	internal class ChangeUserStatus : ChatCommand
	{
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (!json.TryGetValue("UserStatus", out UserStatuses status))
			{
				Message.Reply(ChatStatusCode.BadMessageData);
				return;
			}

			//Announce this user's new status.
			JObject userInfo = Message.User.GetJson();
			userInfo.Add("Status", (int)status);
			ChatCommand.BroadcastChatMessage(TargetType.Chatrooms, from C in Chatroom.GetAccessableByUser(Chat.Database, Message.User) select C.ID, new ChatMessage(MessageType.UserStatusChanged, userInfo));
		}
	}
}
