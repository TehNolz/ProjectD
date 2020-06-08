using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;
using System;
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
			Database.Insert(new User("user@example.com", "SomePassword"));
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.PATCH, new JObject() {
				{"ID", User.GetByEmail(Database, "user@example.com").ID.ToString() },
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
				IDType.Administrator,
				HttpStatusCode.Forbidden,
				"Can't edit built-in administrator"
			},

			//Change nonexistent user
			new object[] {
				new JObject() {
					{ "Email", "user@example.com" }
				},
				IDType.Empty,
				HttpStatusCode.NotFound,
				"No such user"
			},

			//Bad email
			new object[] {
				new JObject() {
					{ "Email", "SomeEmail" }
				},
				IDType.CreatedUser,
				HttpStatusCode.BadRequest,
				"Invalid Email"
			},

			//Existing email
			new object[] {
				new JObject() {
					{ "Email", "user@example.com" }
				},
				IDType.CreatedUser,
				HttpStatusCode.BadRequest,
				"New Email already in use"
			},

			//Bad password
			new object[] {
				new JObject() {
					{ "Password", "a" }
				},
				IDType.CreatedUser,
				HttpStatusCode.BadRequest,
				"Password does not meet requirements"
			}
		};

		/// <summary>
		/// Check if we get an error if we specify invalid arguments
		/// </summary>
		[TestMethod]
		[DynamicData("InvalidPatchTestData")]
		public void EDIT_InvalidArguments(JObject JSON, IDType ID, HttpStatusCode StatusCode, string ResponseMessage)
		{
			Database.Insert(new User("user@example.com", "SomePassword"));


			JSON.Add("ID", ID switch
			{
				IDType.Administrator => User.GetByEmail(Database, "Administrator").ID.ToString(),
				IDType.CreatedUser => User.GetByEmail(Database, "user@example.com").ID.ToString(),
				IDType.Empty => Guid.Empty.ToString(),
				_ => string.Empty,
			});

			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.PATCH, JSON, contentType: "application/json");
			Assert.IsTrue(Response.StatusCode == StatusCode);
			if (ResponseMessage != null)
				Assert.IsTrue(Response.Data == ResponseMessage);
		}

		public enum IDType
		{
			CreatedUser,
			Administrator,
			Empty,
		}
	}
}
