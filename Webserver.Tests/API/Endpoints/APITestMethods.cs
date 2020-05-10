using Database.SQLite;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Concurrent;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

using Webserver;
using Webserver.Models;
using Webserver.Webserver;

namespace WebserverTests.API_Endpoints.Tests
{
	public class APITestMethods
	{
		/// <summary>
		/// Request queue.
		/// </summary>
		public BlockingCollection<ContextProvider> Queue = new BlockingCollection<ContextProvider>();

		/// <summary>
		/// Database connection.
		/// </summary>
		public static SQLiteAdapter Database;

		public TestContext TestContext { get; set; }

		public SQLiteTransaction Transaction;

		[ClassInitialize]
		public static void ClassInit(TestContext _)
		{
			//Run inits
			Database = new SQLiteAdapter(":memory:");
			Program.InitDatabase(Database);
		}

		[ClassCleanup]
		public static void ClassCleanup() => Database.Dispose();

		/// <summary>
		/// Sends a simple request to a RequestWorker
		/// </summary>
		public ResponseProvider ExecuteSimpleRequest(string url, HttpMethod method, JToken json = null, bool login = true, Cookie cookie = null, string contentType = null)
		{
			var Request = new RequestProvider(new Uri("http://localhost" + url), method);

			//Create a session cookie if necessary
			if (login)
				Request.Cookies.Add(CreateNewSessionCookie());

			//Add a cookie if necessary
			if (cookie != null)
				Request.Cookies.Add(cookie);

			//Add JSON if necessary
			if (json != null)
			{
				Request.ContentEncoding = Encoding.UTF8;
				Request.InputStream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString()));
			}

			//Set content type if necessary
			if (contentType != null)
			{
				Request.ContentType = contentType;
			}

			var Context = new ContextProvider(Request);
			Queue.Add(Context);
			ExecuteQueue();
			return Context.Response;
		}

		/// <summary>
		/// Creates a RequestWorker and runs it. The RequestWorker will continue to run until all requests in the queue have been processed.
		/// </summary>
		public void ExecuteQueue()
		{
			RequestWorker.Queue = Queue;
			var Worker = new RequestWorker(Database, true);
			Worker.Run();
		}

		/// <summary>
		/// Creates a new session cookie. Bypasses the Login endpoint to save time.
		/// </summary>
		/// <param name="Email">The user to login. Defaults to Administrator</param>
		/// <param name="RememberMe">If true, delays session expiration</param>
		/// <returns>A cookie named SessionID, which contains the session ID</returns>
		public Cookie CreateNewSessionCookie(string Email = "Administrator", bool RememberMe = false) => new Cookie("SessionID", new Session(Database, User.GetByEmail(Database, Email).Email, RememberMe).SessionID);

		[TestInitialize]
		public void Init()
		{
			//Check if init should be skipped
			if (GetType().GetMethod(TestContext.TestName).GetCustomAttributes<SkipInitCleanup>().Any())
				return;
			Transaction = Database.Connection.BeginTransaction();
		}
		[TestCleanup]
		public void Cleanup()
		{
			//Check if cleanup should be skipped
			if (GetType().GetMethod(TestContext.TestName).GetCustomAttributes<SkipInitCleanup>().Any())
				return;

			Transaction?.Rollback();
		}

		public class SkipInitCleanup : Attribute { }
	}
}
