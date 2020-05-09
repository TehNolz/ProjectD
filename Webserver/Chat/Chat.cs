using Database.SQLite;

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
		public static SQLiteAdapter Database = new SQLiteAdapter(Program.DatabaseName);
	}
}
