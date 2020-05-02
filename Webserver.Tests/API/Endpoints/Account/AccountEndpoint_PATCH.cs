using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;

using Webserver.Models;
using Webserver.Webserver;

namespace WebserverTests.API_Endpoints.Tests
{
	public partial class AccountEndpointTests : APITestMethods
	{
		[TestMethod]
		public void EDIT_ValidArguments()
		{
			new User(Database, "user@example.com", "SomePassword");
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=user@example.com", HttpMethod.PATCH, new JObject() {
				{"Email", "test@example.com" },
			}, contentType: "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);

			var acc = User.GetByEmail(Database, "test@example.com");
			Assert.IsNotNull(acc);
			Assert.IsTrue(acc.Email == "test@example.com");
			Assert.IsTrue(acc.PermissionLevel == PermissionLevel.User);
		}

		[SuppressMessage("Code Quality", "IDE0051")]
		private static IEnumerable<object[]> InvalidPatchTestData => new[]{
			//Change administrator name
			new object[] {
				new JObject() {
					{ "Email", "user@example.com" }
				},
				"/api/account?email=Administrator",
				HttpStatusCode.Forbidden,
				null
			},

			//Change nonexistent user
			new object[] {
				new JObject() {
					{ "Email", "user@example.com" }
				},
				"/api/account?email=SomeUser",
				HttpStatusCode.NotFound,
				"No such user"
			},

			//No fields
			new object[] {
				new JObject(),
				"/api/account",
				HttpStatusCode.BadRequest,
				"Missing fields"
			},

			//Bad email
			new object[] {
				new JObject() {
					{ "Email", "SomeEmail" }
				},
				"/api/account?email=user@example.com",
				HttpStatusCode.BadRequest,
				"Invalid Email"
			},

			//Existing email
			new object[] {
				new JObject() {
					{ "Email", "user@example.com" }
				},
				"/api/account?email=user@example.com",
				HttpStatusCode.BadRequest,
				"New Email already in use"
			},

			//Bad password
			new object[] {
				new JObject() {
					{ "Password", "a" }
				},
				"/api/account?email=user@example.com",
				HttpStatusCode.BadRequest,
				"Password does not meet requirements"
			}
		};

		/// <summary>
		/// Check if we get an error if we specify invalid arguments
		/// </summary>
		[TestMethod]
		[DynamicData("InvalidPatchTestData")]
		public void EDIT_InvalidArguments(JObject JSON, string URL, HttpStatusCode StatusCode, string ResponseMessage)
		{
			new User(Database, "user@example.com", "SomePassword");
			ResponseProvider Response = ExecuteSimpleRequest(URL, HttpMethod.PATCH, JSON, contentType: "application/json");
			Assert.IsTrue(Response.StatusCode == StatusCode);
			if (ResponseMessage != null)
				Assert.IsTrue(Response.Data == ResponseMessage);
		}
	}
}
