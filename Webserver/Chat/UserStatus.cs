using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using Webserver.LoadBalancer;
using Webserver.Models;

namespace Webserver.Chat.Commands
{
	public static class UserStatus
	{

		// TODO Name change?
		/// <summary>
		/// Increment the connection counter for this user.
		/// </summary>
		/// <param name="user"></param>
		/// <param name="slave">The slave this user is connected to. Only used if this function is called by master. Use null if the user is connected to master.</param>
		public static void UserConnect(User user, IPAddress slave = null)
		{
			//If this isn't master, just send a message to the master and call it a day.
			if (!Balancer.IsMaster)
			{
				Balancer.MasterServer.Send(new ServerMessage(MessageType.WebSocketConnect, user));
				return;
			}

			if (slave == null)
				slave = Balancer.LocalAddress;

			//Add an entry for this server and user if they're not there already
			if (!Chat.UserConnectionCounts.ContainsKey(slave))
				Chat.UserConnectionCounts.TryAdd(slave, new ConcurrentDictionary<Guid, int>());
			if (!Chat.UserConnectionCounts[slave].ContainsKey(user.ID))
				Chat.UserConnectionCounts[slave].TryAdd(user.ID, 0);

			//Increment the counter for this user
			Chat.UserConnectionCounts[slave][user.ID] += 1;

			//If the counter is now 1, announce to all relevant users that this user is now online.
			if (Chat.UserConnectionCounts[slave][user.ID] == 1)
			{
				IEnumerable<Guid> users = (from C in Chatroom.GetAccessableByUser(Chat.Database, user) from U in C.GetUsers() select U).Distinct();
				JObject userInfo = user.GetJson();
				userInfo.Add("Status", (int)UserStatuses.Online);
				ChatCommand.BroadcastChatMessage(TargetType.Users, users, new ChatMessage(MessageType.UserStatusChanged, userInfo));
			}
		}

		/// <summary>
		/// Event handler for WebSocketConnect events
		/// </summary>
		/// <param name="connection"></param>
		[EventMessageType(MessageType.WebSocketConnect)]
		public static void UserConnectionHandler(ServerMessage message) => UserConnect(message.Data, message.Connection.Address);

		/// <summary>
		/// Decrement the connection counter for this user. If the counter is 0, a notification is sent to all relevant clients that this user has logged out.
		/// </summary>
		/// <param name="user"></param>
		/// <param name="slave"></param>
		public static void UserDisconnect(User user, IPAddress slave = null)
		{
			//If this isn't master, just send a message to the master and call it a day.
			if (!Balancer.IsMaster)
			{
				Balancer.MasterServer.Send(new ServerMessage(MessageType.WebSocketDisconnect, user));
				return;
			}

			if (slave == null)
				slave = Balancer.LocalAddress;

			//Check if the counter isn't already 0. If it is, we probably have a bug so throw an exception.
			if (Chat.UserConnectionCounts[slave][user.ID] <= 0)
				throw new ArgumentOutOfRangeException("Connection counter already 0");

			//Decrement the counter for this user
			Chat.UserConnectionCounts[slave][user.ID] -= 1;

			//If the counter is now 0, announce to all relevant users that this user is now offline.
			if (Chat.UserConnectionCounts[slave][user.ID] == 0)
			{
				IEnumerable<Guid> users = (from C in Chatroom.GetAccessableByUser(Chat.Database, user) from U in C.GetUsers() select U).Distinct();
				JObject userInfo = user.GetJson();
				userInfo.Add("Status", (int)UserStatuses.Offline);
				ChatCommand.BroadcastChatMessage(TargetType.Users, users, new ChatMessage(MessageType.UserStatusChanged, userInfo));
			}
		}

		[EventMessageType(MessageType.WebSocketDisconnect)]
		public static void UserDisconnectionHandler(ServerMessage message) => UserDisconnect(message.Data, message.Connection.Address);
	}

	public enum UserStatuses
	{
		Online,
		Busy,
		Away,
		Offline,
	}
}
