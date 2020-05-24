using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;

using Webserver.LoadBalancer;
using Webserver.Models;

namespace Webserver.Chat
{
	public abstract partial class ChatCommand
	{
		#region Attributes
		/// <summary>
		/// Specifies the command name of this message.
		/// </summary>
		[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
		public class CommandNameAttribute : Attribute
		{
			/// <summary>
			/// The command name.
			/// </summary>
			public string Name { get; }
			public CommandNameAttribute(string name)
			{
				Name = name;
			}
		}

		/// <summary>
		/// Specifies the minimum permission level the user needs to use this command.
		/// </summary>
		/// TODO Merge with API's PermissionAttribute?
		[AttributeUsage(AttributeTargets.Class)]
		public class PermissionAttribute : Attribute
		{
			public PermissionLevel PermissionLevel { get; }
			public PermissionAttribute(PermissionLevel level)
			{
				PermissionLevel = level;
			}
		}
		#endregion

		/// <summary>
		/// The message that was sent to trigger this command.
		/// </summary>
		public ChatMessage Message { get; set; }

		/// <summary>
		/// The data attached to the received message.
		/// </summary>
		public dynamic Data => Message.Data;

		/// <summary>
		/// The command's exeggutor function.
		/// </summary>
		public abstract void Execute();

		/// <summary>
		/// Send a ChatMessage to the specified targets, including targets on remote servers. Targets can be users or chatrooms.
		/// </summary>
		/// <param name="targetType">The type of the target</param>
		/// <param name="targets">A list of targets that will receive this ChatMessage</param>
		/// <param name="message">The ChatMessage to send</param>
		public static void BroadcastChatMessage(TargetType targetType, IEnumerable<Guid> targets, ChatMessage message)
		{
			var serverMessage = new ServerMessage(MessageType.Chat, new JObject()
			{
				{"TargetType",  targetType.ToString()},
				{"Targets", new JArray(targets) },
				{"Message", message.GetJson() }
			});
			ServerConnection.Broadcast(serverMessage);
			BroadcastHandler(serverMessage);
		}

		/// <summary>
		/// Executes commands on the specified targets, including targets on remote servers. Targets can be users or chatrooms.
		/// </summary>
		/// <param name="targetType">The type of the target</param>
		/// <param name="targets">A list of targets that will receive this ChatMessage</param>
		/// <param name="command">The command to execute</param>
		public static void BroadcastCommand(TargetType targetType, IEnumerable<Guid> targets, CommandType command)
		{
			var serverMessage = new ServerMessage(MessageType.Chat, new JObject()
			{
				{"TargetType",  targetType.ToString()},
				{"Targets", new JArray(targets) },
				{"Command", command.ToString() }
			});
			ServerConnection.Broadcast(serverMessage);
			BroadcastHandler(serverMessage);
		}

		/// <summary>
		/// Event handler for BroadcastChatMessage. Receives ChatMessages and broadcasts them accordingly
		/// </summary>
		[EventMessageType(MessageType.Chat)]
		public static void BroadcastHandler(ServerMessage message)
		{
			var data = (JObject)message.Data;

			//Get target connections
			List<string> targets = data["Targets"].ToObject<List<string>>();
			IEnumerable<ChatConnection> matchingConnections = Enum.Parse<TargetType>((string)data["TargetType"]) == TargetType.Users
				? (from AC in ChatConnection.ActiveConnections where targets.Contains(AC.User.ID.ToString()) select AC)
				: (from ChatConnection AC in ChatConnection.ActiveConnections from Chatroom C in AC.Chatrooms where targets.Contains(C.ID.ToString()) select AC);

			//Run the appropriate action on each connection
			if (data.ContainsKey("Message"))
			{
				//Relay the ChatMessage to reach connection
				foreach (ChatConnection connection in matchingConnections)
				{
					connection.Send(ChatMessage.FromJson((JObject)data["Message"]));
				}
			}
			else if (data.ContainsKey("Command"))
			{
				//Execute the specified command on each connection
				foreach (ChatConnection connection in matchingConnections)
				{
					switch (Enum.Parse<CommandType>((string)data["Command"]))
					{
						//Will put commands here at some point.
						default:
							return;
					}
				}
			}
		}
	}

	public enum TargetType
	{
		Chatrooms,
		Users,
	}

	public enum CommandType
	{
		UpdateChatroomInfo
	}
}
