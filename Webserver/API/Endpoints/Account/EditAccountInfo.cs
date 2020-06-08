using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

using Webserver.Config;
using Webserver.Models;

namespace Webserver.API.Endpoints.Account
{
	public partial class AccountEndpoint : APIEndpoint
	{
		[Permission(PermissionLevel.Admin)]
		[ContentType("application/json")]
		public override void PATCH()
		{
			var json = (JObject)Data;

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
				Response.Send("Can't edit built-in administrator", HttpStatusCode.Forbidden);
				return;
			}

			//Change email if necessary
			if (json.TryGetValue("Email", out string NewEmail))
			{
				//Check if the new address is valid
				var rx = new Regex("^[A-z0-9]*@[A-z0-9]*.[A-z]*$");
				if (!rx.IsMatch(NewEmail))
				{
					Response.Send("Invalid Email", HttpStatusCode.BadRequest);
					return;
				}

				//Check if the new address is already in use
				if (User.GetByEmail(Database, NewEmail) != null)
				{
					Response.Send("New Email already in use", HttpStatusCode.BadRequest);
					return;
				}
				account.Email = NewEmail;
			}

			//Change password if necessary
			if (json.TryGetValue("Password", out string Password))
			{
				if (!new Regex(AuthenticationConfig.PasswordRegex).IsMatch(Password) || Password.Length == 0)
				{
					Response.Send("Password does not meet requirements", HttpStatusCode.BadRequest);
					return;
				}
				account.ChangePassword(Database, Password);
			}

			//Set optional fields
			foreach (KeyValuePair<string, JToken> x in json)
			{
				if (x.Key == "Email" || x.Key == "PasswordHash")
				{
					continue;
				}
				PropertyInfo prop = account.GetType().GetProperty(x.Key);
				if (prop == null)
				{
					continue;
				}
				dynamic Value = x.Value.ToObject(prop.PropertyType);
				prop.SetValue(account, Value);
			}

			//Update DB row
			Database.Update(account);
			Response.Send(HttpStatusCode.OK);
		}
	}
}
