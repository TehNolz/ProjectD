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
		/// <summary>
		/// Check if we can delete an account
		/// </summary>
		[TestMethod]
		public void DELETE_ValidArguments()
		{
			var account = new User("user@example.com", "SomePassword");
			Database.Insert(account);
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.DELETE, new JObject() {
				{"ID", account.ID},
			}, contentType: "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			Assert.IsNull(User.GetByEmail(Database, "user@example.com"));
		}

		[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
		private static IEnumerable<object[]> InvalidDeleteTestData => new[]{
			new object[] {
				new JObject() {
					{"ID", Guid.Empty},
				},
				HttpStatusCode.NotFound,
				"No such user"
			},
			new object[] {
				new JObject(),
				HttpStatusCode.BadRequest,
				"Missing fields"
			}
		};

		/// <summary>
		/// Check if we get an error if we specify invalid arguments
		/// </summary>
		[TestMethod]
		[DynamicData("InvalidDeleteTestData")]
		public void DELETE_InvalidArguments(JObject JSON, HttpStatusCode StatusCode, string ResponseMessage)
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.DELETE, JSON, contentType: "application/json");
			Assert.IsTrue(Response.StatusCode == StatusCode);
			Assert.IsTrue(Response.Data == ResponseMessage);
		}
	}
}
