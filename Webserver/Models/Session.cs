using Database.SQLite;
using Database.SQLite.Modeling;

using System;
using System.Linq;

using Webserver.Config;

namespace Webserver.Models
{
	/// <summary>
	/// A user login session
	/// </summary>
	public class Session
	{
		public string UserEmail { get; set; }
		public long Token { get; set; }
		[Primary]
		public string SessionID { get; set; }
		public bool RememberMe { get; set; }

		/// <summary>
		/// Creates a new user Session
		/// </summary>
		/// <param name="user">The user this session belongs to</param>
		/// <param name="rememberMe"></param>
		public Session(SQLiteAdapter database, string userEmail, bool rememberMe)
		{
			SessionID = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
			UserEmail = userEmail;
			RememberMe = rememberMe;
			Token = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
			database.Insert(this);
		}

		/// <summary>
		/// Constructor for deserializing database rows into Session objects
		/// </summary>
		public Session() { }

		/// <summary>
		/// Renews the token
		/// </summary>
		public void Renew(SQLiteAdapter database)
		{
			Token = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
			database.Update(this);
		}

		/// <summary>
		/// Get the amount of seconds remaining until this session expires.
		/// The number will be negative if the session has already expired.
		/// </summary>
		/// <returns></returns>
		public long GetRemainingTime() => GetRemainingTime(Token, RememberMe);
		/// <summary>
		/// Get the amount of seconds remaining until this session expires.
		/// The number will be negative if the session has already expired.
		/// </summary>
		public static long GetRemainingTime(long Token, bool rememberMe)
		{
			long timeout = rememberMe ? AuthenticationConfig.SessionTimeoutLong : AuthenticationConfig.SessionTimeoutShort;
			long tokenAge = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds - Token;
			return timeout - tokenAge;
		}

		/// <summary>
		/// Gets a user session. If the session doesn't exist or expired, null will be returned.
		/// </summary>
		/// <param name="Connection"></param>
		/// <returns></returns>
		public static Session GetSession(SQLiteAdapter database, string sessionID)
		{
			//Get the session
			Session s = database.Select<Session>("SessionID = @SessionID", new { sessionID }).FirstOrDefault();
			if (s == null)
			{
				return null;
			}

			//Check if this session is still valid. If it isn't, delete it and return null.
			if (GetRemainingTime(s.Token, s.RememberMe) < 0)
			{
				database.Delete(s);
				return null;
			}
			else
			{
				return s;
			}
		}
	}
}