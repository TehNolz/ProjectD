using Newtonsoft.Json.Linq;

using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

using Webserver.API;
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
			if (!Params.ContainsKey("email"))
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			//Administrator account can't be modified;
			if (Params["email"][0] == "Administrator")
			{
				Response.Send(HttpStatusCode.Forbidden);
				return;
			}

			//Check if the specified user exists. If it doesn't, send a 404 Not Found
			var Acc = User.GetByEmail(Database, Params["email"][0]);
			if (Acc == null)
			{
				Response.Send("No such user", HttpStatusCode.NotFound);
				return;
			}

			//Change email if necessary
			if (json.TryGetValue<string>("Email", out JToken NewEmail))
			{
				//Check if the new address is valid
				var rx = new Regex("^[A-z0-9]*@[A-z0-9]*.[A-z]*$");
				if (!rx.IsMatch((string)NewEmail))
				{
					Response.Send("Invalid Email", HttpStatusCode.BadRequest);
					return;
				}

				//Check if the new address is already in use
				if (User.GetByEmail(Database, (string)NewEmail) != null)
				{
					Response.Send("New Email already in use", HttpStatusCode.BadRequest);
					return;
				}
				Acc.Email = (string)NewEmail;
			}

			//Change password if necessary
			if (json.TryGetValue<string>("Password", out JToken Password))
			{
				if (!new Regex(AuthenticationConfig.PasswordRegex).IsMatch((string)Password) || ((string)Password).Length == 0)
				{
					Response.Send("Password does not meet requirements", HttpStatusCode.BadRequest);
					return;
				}
				Acc.ChangePassword(Database, (string)Password);
			}

			//Set optional fields
			foreach (KeyValuePair<string, JToken> x in json)
			{
				if (x.Key == "Email" || x.Key == "PasswordHash")
				{
					continue;
				}
				PropertyInfo prop = Acc.GetType().GetProperty(x.Key);
				if (prop == null)
				{
					continue;
				}
				dynamic Value = x.Value.ToObject(prop.PropertyType);
				prop.SetValue(Acc, Value);
			}

			//Update DB row
			Database.Update(Acc);
			Response.Send(HttpStatusCode.OK);
		}
	}
}
