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
		/// Check if we can delete an account
		/// </summary>
		[TestMethod]
		public void DELETE_ValidArguments()
		{
			new User(Database, "user@example.com", "SomePassword");
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.DELETE, new JObject() {
				{"Email", "user@example.com"},
			}, contentType: "application/json");

			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			Assert.IsNull(User.GetByEmail(Database, "user@example.com"));
		}

		[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
		static IEnumerable<object[]> InvalidDeleteTestData => new[]{
			new object[] {
				new JObject() {
					{"Email", "Administrator"},
				},
				HttpStatusCode.Forbidden,
				"Can't delete built-in administrator"
			},
			new object[] {
				new JObject() {
					{"Email", "user@example.com"},
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
