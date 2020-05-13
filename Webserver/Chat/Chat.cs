using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Webserver.LoadBalancer;
using Webserver.Models;

namespace Webserver.Chat
{
	/// <summary>
	/// Management class for chat system.
	/// </summary>
	public static class Chat
	{
		/// <summary>
		/// Database connection for chatroom management. Do not use outside of chatroom system!
		/// </summary>
		// TODO One database connection for the entire chat system is probably not a good idea lol.
		public static SQLiteAdapter Database = new SQLiteAdapter(Program.DatabaseName);

		/// <summary>
		/// ConcurrentDictionary which records how many users have a connection to which server.
		/// </summary>
		private static ConcurrentDictionary<IPAddress, ConcurrentDictionary<Guid, int>> UserConnectionCounts = new ConcurrentDictionary<IPAddress, ConcurrentDictionary<Guid, int>>();

		/// <summary>
		/// Get the amount of active websocket connections this user currently has.
		/// </summary>
		/// <param name="user">The user</param>
		/// <returns></returns>
		public static int GetConnectionCount(User user)
		{
			if (!Balancer.IsMaster)
			{
				return Balancer.MasterServer.SendAndWait(new ServerMessage(MessageType.WebSocketCount, user.ID)).Data;
			} else
			{
				int total = 0;
				foreach (KeyValuePair<IPAddress, ConcurrentDictionary<Guid, int>> entry in UserConnectionCounts)
					if (entry.Value.TryGetValue(user.ID, out int i))
						total += i;
				return total;
			}
		}

		/// <summary>
		/// Event handler for GetConnectionCount
		/// </summary>
		/// <param name="message"></param>
		public static void ConnectionCountHandler(ServerMessage message)
		{
			if (message.Type != MessageType.WebSocketCount)
				return;
			message.Reply(GetConnectionCount((User)message.Data));
		}

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
			if (!UserConnectionCounts.ContainsKey(slave))
				UserConnectionCounts.TryAdd(slave, new ConcurrentDictionary<Guid, int>());
			if (!UserConnectionCounts[slave].ContainsKey(user.ID))
				UserConnectionCounts[slave].TryAdd(user.ID, 0);

			//Increment the counter for this user
			UserConnectionCounts[slave][user.ID] += 1;

			//If the counter is now 1, announce to all relevant users that this user is now online.
			if(UserConnectionCounts[slave][user.ID] == 1)
			{
				IEnumerable<Guid> chatrooms = from C in Chatroom.GetAccessableByUser(Database, user) select C.ID;
				JObject userInfo = user.GetJson();
				userInfo.Add("Status", (int)UserStatus.Online);
				ChatCommand.BroadcastChatMessage(TargetType.Chatrooms, chatrooms, new ChatMessage(MessageType.UserStatusChanged, userInfo));
			}
		}

		/// <summary>
		/// Event handler for WebSocketConnect events
		/// </summary>
		/// <param name="connection"></param>
		public static void UserConnectionHandler(ServerMessage message)
		{
			if (message.Type == MessageType.WebSocketConnect)
				return;
			UserConnect(message.Data, message.Connection.Address);
		}

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
			if (UserConnectionCounts[slave][user.ID] <= 0)
				throw new ArgumentOutOfRangeException("Connection counter already 0");
			
			//Decrement the counter for this user
			UserConnectionCounts[slave][user.ID] -= 1;

			//If the counter is now 0, announce to all relevant users that this user is now offline.
			if (UserConnectionCounts[slave][user.ID] == 0)
			{
				IEnumerable<Guid> chatrooms = from C in Chatroom.GetAccessableByUser(Database, user) select C.ID;
				JObject userInfo = user.GetJson();
				userInfo.Add("Status", (int)UserStatus.Offline);
				ChatCommand.BroadcastChatMessage(TargetType.Chatrooms, chatrooms, new ChatMessage(MessageType.UserStatusChanged, userInfo));
			}
		}

		public static void UserDisconnectionHandler(ServerMessage message)
		{
			if (message.Type == MessageType.WebSocketDisconnect)
				return;
			UserDisconnect(message.Data, message.Connection.Address);
		}

		public enum UserStatus
		{
			Online,
			Busy,
			Away,
			Offline,
		}
	}
}
