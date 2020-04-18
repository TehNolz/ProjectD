using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Digests;
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
			if (!((JObject)Data).TryGetValue("Email", out string email))
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			//Cancel if Email is "Administrator", because the built-in Admin shouldn't ever be deleted.
			if (email == "Administrator")
			{
				Response.Send("Can't delete built-in administrator", HttpStatusCode.Forbidden);
				return;
			}

			//Check if the specified user exists. If it doesn't, send a 404 Not Found
			var account = User.GetByEmail(Database, email);
			if (account == null)
			{
				Response.Send("No such user", HttpStatusCode.NotFound);
				return;
			}

			Database.Delete(account);
			Response.Send(HttpStatusCode.OK);
		}
	}
}
