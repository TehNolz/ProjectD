using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using Webserver.Models;

namespace Webserver.API.Endpoints
{
	[Route("add")]
	class AddThing : APIEndpoint
	{
		private static SQLiteAdapter Database => Program.Database;

		[ContentType("application/json")]
		public override void POST()
		{
			// Begin transaction to prevent changes
			using var transaction = Database.Connection.BeginTransaction();

			string message = Data.Message;
			Guid guid = Data.Guid;

			var items = new ExampleModel[] { new ExampleModel() { Message = message, Guid = guid } };
			Database.Insert<ExampleModel>(items);

			Response.Send(JObject.FromObject(items[0]), HttpStatusCode.Created);

			transaction.Rollback();
		}
	}
}
