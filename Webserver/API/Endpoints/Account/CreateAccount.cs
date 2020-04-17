using Newtonsoft.Json.Linq;

using System.Collections.Generic;
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
		public override void POST()
		{
			var json = (JObject)Data;

			//Get all required fields
			if (
				!json.TryGetValue<string>("Email", out JToken email) ||
				!json.TryGetValue<string>("Password", out JToken password)
			)
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			//Check if a user already exists with this email. If it isn't, send a 400 Bad Request
			if (User.GetByEmail(Database, (string)email) != null)
			{
				Response.Send("A user with this email already exists", HttpStatusCode.BadRequest);
				return;
			}

			//Check if the email is valid. If it isn't, send a 400 Bad Request.
			if (!new Regex("^[A-z0-9]*@[A-z0-9]*\\.[A-z]{1,}$").IsMatch((string)email))
			{
				Response.Send("Invalid email", HttpStatusCode.BadRequest);
				return;
			}

			//Check if the password is valid. If it isn't, send a 400 Bad Request.
			if (!new Regex(AuthenticationConfig.PasswordRegex).IsMatch((string)password) || ((string)password).Length == 0)
			{
				Response.Send("Password does not meet requirements", HttpStatusCode.BadRequest);
				return;
			}

			//Create a new user
			var newUser = new User(Database, (string)email, (string)password);

			//Set optional fields
			foreach (KeyValuePair<string, JToken> field in json)
			{
				if (field.Key == "Email" || field.Key == "Password")
				{
					continue;
				}
				PropertyInfo Prop = newUser.GetType().GetProperty(field.Key);
				if (Prop == null)
				{
					continue;
				}
				Prop.SetValue(newUser, field.Value.ToObject(Prop.PropertyType));
			}

			//Upload account to database
			Database.Update(newUser);

			//Send OK
			Response.Send(HttpStatusCode.Created);
		}
	}
}
