using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Text.RegularExpressions;

using Webserver.Config;
using Webserver.LoadBalancer;

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
			ChatManagement.Database.Insert(chatroom);
			var result = chatroom.GetJson();
			result.Add("Users", new JArray(chatroom.GetUsers()));
			var serverMessage = new ServerMessage(MessageType.ChatroomUpdate, result);
			ServerConnection.Broadcast(serverMessage);
			Chatroom.ChatroomUpdateHandler(serverMessage);
		}
	}
}
