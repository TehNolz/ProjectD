using Newtonsoft.Json.Linq;

using System.Collections.Generic;
using System.Linq;
using System.Net;

using Webserver.Models;

namespace Webserver.API.Endpoints.Account
{
	[Route("/account")]
	public partial class AccountEndpoint : APIEndpoint
	{
		[Permission(PermissionLevel.User)]
		public override void GET()
		{
			//Get required fields
			var users = new List<User>();
			if (Params.ContainsKey("email"))
			{
				//Get all user objects
				foreach (string email in Params["email"])
				{
					if (email == "CurrentUser")
					{
						users.Add(User);
						continue;
					}
					if (User.PermissionLevel < PermissionLevel.Admin)
					{
						Response.Send(HttpStatusCode.Forbidden);
						return;
					}
					var Acc = User.GetByEmail(Database, email);
					if (Acc != null)
						users.Add(Acc);
				}
			}
			//If email is missing, assume all users
			else
			{
				if (User.PermissionLevel < PermissionLevel.Admin)
				{
					Response.Send(HttpStatusCode.Forbidden);
					return;
				}
				users = Database.Select<User>().ToList();
			}

			//Convert to JSON and remove password hashes.
			var json = JArray.FromObject(users);
			foreach (JObject Entry in json)
			{
				Entry.Remove("PasswordHash"); //Security!
			}

			//Send response
			Response.Send(json);
		}
	}
}
