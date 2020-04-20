
using Newtonsoft.Json.Linq;

using System;
using System.Net;

using Webserver.Models;

namespace Webserver.API.Endpoints
{
	[Route("add")]
	class AddThing : APIEndpoint
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
	}
}
