using Newtonsoft.Json.Linq;

using System.Net;
using System.Text.RegularExpressions;

using Webserver.API;
using Webserver.Models;

namespace Webserver.API_Endpoints
{

	/// <summary>
	/// API endpoint to manage user logins.
	/// </summary>
	[Route("/login")]
	internal class Login : APIEndpoint
	{
		/// <summary>
		/// Login method. Requires JSON input in the form of;
		/// {
		///		"Email":		The user's email address
		///		"Password":		The user's password
		///		"RememberMe":	Whether the RememberMe checkbox was ticked during login.
		/// }
		/// </summary>
		[ContentType("application/json")]
		public override void POST()
		{
			var json = (JObject)Data;

			//Check if a session cookie was sent.
			Cookie SessionIDCookie = Request.Cookies["SessionID"];
			if (SessionIDCookie != null)
			{
				//Get the session belonging to this session ID. If the session is still valid, renew it. If it isn't, send back a 401 Unauthorized, signaling that the client should send an email and password to reauthenticate
				var currentSession = Session.GetSession(Database, SessionIDCookie.Value);

				if (currentSession != null)
				{
					currentSession.Renew(Database);
					Response.Send("Renewed", HttpStatusCode.OK);
					return;
				}
			}

			//Get the email and password from the request. If one of the values is missing, send a 400 Bad Request.
			bool foundEmail = json.TryGetValue("Email", out string email);
			bool foundPassword = json.TryGetValue("Password", out string password);
			bool foundRememberMe = json.TryGetValue("RememberMe", out bool rememberMe);
			if (!foundEmail || !foundPassword || !foundRememberMe)
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			//Check if the email is valid. If it isn't, send a 400 Bad Request.
			if (!new Regex("^[A-z0-9]*@[A-z0-9]*.[A-z]*$").IsMatch(email) && email != "Administrator")
			{
				Response.Send("Invalid Email", HttpStatusCode.BadRequest);
				return;
			}

			//Check if the user exists. If it doesn't, send a 400 Bad Request
			var account = User.GetByEmail(Database, email);
			if (account == null)
			{
				Response.Send("No such user", HttpStatusCode.BadRequest);
				return;
			}

			//Check if password is an empty string, and send a 400 Bad Request if it is.
			if (password.Length == 0)
			{
				Response.Send("Empty password", HttpStatusCode.BadRequest);
				return;
			}

			//Check password. If its invalid, return a 401 Unauthorized
			if (account.PasswordHash != User.CreateHash(password, email))
			{
				Response.Send(HttpStatusCode.Unauthorized);
				return;
			}

			//At this point, we know the user exists and that the credentials are valid. The user will now be logged in.
			//Create a new session, store it, and send back the Session ID
			var newSession = new Session(Database, account.Email, rememberMe);

			Response.AddCookie("SessionID", newSession.SessionID, newSession.GetRemainingTime());
			Response.Send(HttpStatusCode.NoContent);
		}

		/// <summary>
		/// Logout method. Requires no JSON or parameters.
		/// </summary>
		[Permission(PermissionLevel.User)]
		public override void DELETE()
		{
			Database.Delete(UserSession);
			Response.AddCookie("SessionID", "", 0);
			Response.Send(HttpStatusCode.OK);
		}
	}
}
