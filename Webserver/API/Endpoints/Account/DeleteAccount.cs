using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;

using Webserver.Models;

namespace Webserver.API.Endpoints.Account
{
	public partial class AccountEndpoint : APIEndpoint
	{
		[Permission(PermissionLevel.Admin)]
		[ContentType("application/json")]
		public override void DELETE()
		{
			//Get required fields
			if (!((JObject)Data).TryGetValue("ID", out Guid ID))
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			var account = Database.Select<User>("ID = @ID", new { ID }).FirstOrDefault();

			//Check if the specified user exists. If it doesn't, send a 404 Not Found
			if (account == null)
			{
				Response.Send("No such user", HttpStatusCode.NotFound);
				return;
			}

			//Cancel if Email is "Administrator", because the built-in Admin shouldn't ever be deleted.
			if (account.Email == "Administrator")
			{
				Response.Send("Can't delete built-in administrator", HttpStatusCode.Forbidden);
				return;
			}

			Database.Delete(account);
			Response.Send(HttpStatusCode.OK);
		}
	}
}
