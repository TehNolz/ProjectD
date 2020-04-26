using System;

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
		[AttributeUsage(AttributeTargets.Method)]
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
	}
}
