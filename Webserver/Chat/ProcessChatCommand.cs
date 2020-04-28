using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using static Webserver.Program;

namespace Webserver.Chat
{
	public abstract partial class ChatCommand
	{
		public static List<Type> Commands;
		/// <summary>
		/// Discover all existing chat commands.
		/// </summary>
		public static void DiscoverCommands() => Commands = (from T in Assembly.GetExecutingAssembly().GetTypes() where typeof(ChatCommand).IsAssignableFrom(T) && !T.IsAbstract select T).ToList();
		/// <summary>
		/// Process an incoming chat message.
		/// </summary>
		/// <param name="message">The message that was sent.</param>
		public static void ProcessChatCommand(ChatMessage message)
		{
			//Check if the requested command exists. If it doesn't, send a BadMessageType error message.
			Type commandType = (
				from Type c in Commands
				from CommandNameAttribute attr in c.GetCustomAttributes<CommandNameAttribute>()
				where attr.Name == message.Command
				select c
			).FirstOrDefault();
			if (commandType == null)
			{
				message.Reply(ChatStatusCode.BadMessageType);
				return;
			}

			//Create new instance of this command
			var command = (ChatCommand)Activator.CreateInstance(commandType);
			command.Message = message;

			//Get the executor endpoint
			MethodInfo executor = commandType.GetMethod("Execute");

			//If this command requires a specific permission, check if the user is allowed to use this command.
			PermissionAttribute attribute = executor.GetCustomAttribute<PermissionAttribute>();
			if (attribute != null)
			{
				if (message.User.PermissionLevel < attribute.PermissionLevel)
				{
					message.Reply(ChatStatusCode.CommandAccessDenied);
				}
			}

			//Invoke the executor. If this fails somehow, send an InternalServerError to the client.
			try
			{
				executor.Invoke(command, null);
			}
			catch (Exception e)
			{
				Log.Error($"{e.GetType().Name}: {e.Message}", e);
				message.Reply(ChatStatusCode.InternalServerError);
			}
		}
	}
}
