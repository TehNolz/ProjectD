using Database.SQLite;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
		public static ConcurrentDictionary<IPAddress, ConcurrentDictionary<Guid, int>> UserConnectionCounts = new ConcurrentDictionary<IPAddress, ConcurrentDictionary<Guid, int>>();

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
			}
			else
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
	}
}
