using Newtonsoft.Json.Linq;

namespace Webserver.Chat.Commands
{
	[CommandName("CreateChatroom")]
	[Permission(Models.PermissionLevel.Admin)]
	public class CreateChatroom : ChatCommand
	{
		public override void Execute()
		{
			var json = (JObject)Data;

			//Check if the received message data is valid.
			if (
				!json.TryGetValue("Name", out string roomName) ||
				!json.TryGetValue("Private", out bool roomPrivate)
			)
			{
				Message.Reply(ChatStatusCode.BadMessageData, "Missing keys");
				return;
			}

			//Create the chatroom
			var chatroom = new Chatroom()
			{
				Name = roomName,
				Private = roomPrivate
			};

			//Save the chatroom to the database and inform all relevant clients about it.
			Chat.Database.Insert(chatroom);
			BroadcastCommand(TargetType.Users, chatroom.GetUsers(), CommandType.UpdateChatroomInfo);
		}
	}
}
