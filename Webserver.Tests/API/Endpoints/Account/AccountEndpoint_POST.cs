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

		/// <summary>
		/// Check if we can create an account using valid arguments
		/// </summary>
		[TestMethod]
		public void POST_ValidArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.POST, new JObject() {
				{"Email", "user@example.com"},
				{"Password", "ex@amplep@ssword12345"}
			}, contentType: "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.Created);
			var Account = User.GetByEmail(Database, "user@example.com");
			Assert.IsNotNull(Account);
			Assert.IsTrue(Account.PermissionLevel == PermissionLevel.User);
		}

		[SuppressMessage("Code Quality", "IDE0051")]
		private static IEnumerable<object[]> InvalidPostTestData => new[]{

			//Bad email
			new object[] {
				new JObject() {
					{"Email", "SomeEmail"},
					{"Password", "examplepassword12345"},
				},
				HttpStatusCode.BadRequest,
				"Invalid email"
			},

			//Bad password
			new object[] {
				new JObject() {
					{"Email", "user@example.com"},
					{"Password", "a"},
				},
				HttpStatusCode.BadRequest,
				"Password does not meet requirements"
			},

			//Account already exists
			new object[] {
				new JObject() {
					{"Email", "Administrator"},
					{"Password", "examplepassword12345"},
				},
				HttpStatusCode.BadRequest,
				"A user with this email already exists"
			}
		};

		/// <summary>
		/// Check invalid arguments
		/// </summary>
		[TestMethod]
		[DynamicData("InvalidPostTestData")]
		public void POST_InvalidArguments(JObject Request, HttpStatusCode StatusCode, string ResponseMsg)
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.POST, Request, contentType: "application/json");
			Assert.IsTrue(Response.StatusCode == StatusCode);
			if (ResponseMsg != null)
				Assert.IsTrue(Response.Data == ResponseMsg);
		}

		/*
		/// <summary>
		/// Check if we can properly apply optional fields.
		/// </summary>
		[TestMethod]
		public void POST_OptionalArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.POST, new JObject() {
				{"Email", "user@example.com"},
				{"Password", "examplepassword12345"},
			});

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.Created);
			var Account = User.GetByEmail(Database, "user@example.com");
			Assert.IsNotNull(Account);
		}
		*/
	}
}