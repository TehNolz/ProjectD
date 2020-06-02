using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Net;

using Webserver.Models;

namespace Webserver.API.Endpoints
{
	[Route("add")]
	internal class AddThing : APIEndpoint
	{
		[ContentType("application/json")]
		public override void POST()
		{
			string message = Data.Message;
			Guid guid = Data.Guid;

			var items = new ExampleModel[] { new ExampleModel() { Message = message, GuidStr = guid.ToString() } };
			Database.Insert<ExampleModel>(items);

			Response.Send(JObject.FromObject(items[0]), HttpStatusCode.Created);
		}

		[ContentType("application/json")]
		public override void DELETE()
		{
			int id = Data.ID;

			int deleted = Database.Delete<ExampleModel>("`ID` = @id", new { id });

			if (deleted == 0)
				Response.Send(HttpStatusCode.Gone);
			else
				Response.Send(HttpStatusCode.NoContent);
		}

		[ContentType("application/json")]
		public override void PATCH()
		{
			int id = Data.ID;
			string message = Data.Message;

			ExampleModel model = Database.Select<ExampleModel>("ID = @id", new { id }).FirstOrDefault();
			if (model is null)
			{
				Response.Send(HttpStatusCode.Gone);
				return;
			}

			model.Message = message;
			int updated = Database.Update(model);

			if (updated == 0)
				Response.Send(HttpStatusCode.NoContent);
			else
				Response.Send(HttpStatusCode.OK);
		}
	}
}
