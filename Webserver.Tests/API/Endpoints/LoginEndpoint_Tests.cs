using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;

using Webserver.Models;
using Webserver.Webserver;

namespace WebserverTests.API_Endpoints.Tests
{
	[TestClass]
	public class LoginEndpoint_Tests : APITestMethods
	{

		/// <summary>
		/// Call base ClassInit because it can't be inherited
		/// </summary>
		[ClassInitialize]
		public static new void ClassInit(TestContext C) => APITestMethods.ClassInit(C);

		[TestMethod]
		public void POST_ValidArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/login", HttpMethod.POST, new JObject() {
				{"Email", "Administrator" },
				{"Password", "W@chtw00rd" },
				{"RememberMe", true }
			}, false, contentType: "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.NoContent);
			Assert.IsTrue(Response.Headers.AllKeys.Contains("Set-Cookie"));
			var S = Session.GetSession(Database, Response.Headers.Get("Set-Cookie").Split(";")[0].Replace("SessionID=", ""));
			Assert.IsNotNull(S);
			Assert.IsTrue(S.UserEmail == "Administrator");
			Assert.IsTrue(S.RememberMe);
		}

		[TestMethod]
		public void POST_RenewSession()
		{
			Cookie SessionCookie = CreateNewSessionCookie("Administrator", true);
			long CurrentToken = Session.GetSession(Database, SessionCookie.Value).Token;

			ResponseProvider Response = ExecuteSimpleRequest("/api/login", HttpMethod.POST, new JObject(), false, SessionCookie, "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			Assert.IsTrue(Response.Data == "Renewed");
			var S = Session.GetSession(Database, SessionCookie.Value);
			Assert.IsNotNull(S);
			Assert.IsTrue(S.Token == CurrentToken);
		}

		[SuppressMessage("Code Quality", "IDE0051")]
		private static IEnumerable<object[]> InvalidPostTestData => new[]{
			new object[] {
				new JObject() {
					{ "Email", "Administrator" }
				},
				HttpStatusCode.BadRequest,
				"Missing fields",
			},
			new object[] {
				new JObject() {
					{"Email", "SomeEmail" },
					{"Password", "W@chtw00rd" },
					{"RememberMe", true }
				},
				HttpStatusCode.BadRequest,
				"Invalid Email",
			},
			new object[] {
				new JObject() {
					{"Email", "user@example.com" },
					{"Password", "W@chtw00rd" },
					{"RememberMe", true }
				},
				HttpStatusCode.BadRequest,
				"No such user",
			},
			new object[] {
				new JObject() {
					{"Email", "Administrator" },
					{"Password", "" },
					{"RememberMe", true }
				},
				HttpStatusCode.BadRequest,
				"Empty password",
			},
			new object[] {
				new JObject() {
					{"Email", "Administrator" },
					{"Password", "SomePassword" },
					{"RememberMe", true }
				},
				HttpStatusCode.Unauthorized,
				null,
			}
		};

		/// <summary>
		/// Check invalid arguments
		/// </summary>
		[TestMethod]
		[DynamicData("InvalidPostTestData")]
		public void POST_InvalidArguments(JObject Request, HttpStatusCode StatusCode, string ResponseMsg)
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/login", HttpMethod.POST, Request, false, contentType: "application/json");
			Assert.IsTrue(Response.StatusCode == StatusCode);
			if (ResponseMsg != null)
				Assert.IsTrue(Response.Data == ResponseMsg);
		}

		[TestMethod]
		public void DELETE()
		{
			Cookie SessionCookie = CreateNewSessionCookie();
			ResponseProvider Response = ExecuteSimpleRequest("/api/login", HttpMethod.DELETE, null, false, SessionCookie);
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			Assert.IsNull(Session.GetSession(Database, SessionCookie.Value));
		}
	}
}
