using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System.Net;

using Webserver.Models;
using Webserver.Webserver;

namespace WebserverTests.API_Endpoints.Tests
{
	[TestClass()]
	public partial class AccountEndpointTests : APITestMethods
	{

		/// <summary>
		/// Call base ClassInit because it can't be inherited
		/// </summary>
		[ClassInitialize]
		public static new void ClassInit(TestContext C) => APITestMethods.ClassInit(C);

		/// <summary>
		/// Check if we can retrieve a single user when given valid arguments
		/// </summary>
		[TestMethod]
		public void GET_ValidArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=Administrator", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);

			Assert.IsTrue((string)Data[0]["Email"] == "Administrator");
		}

		/// <summary>
		/// Check if we can retrieve multiple users when given valid params
		/// </summary>
		[TestMethod]
		public void GET_BulkValidArguments()
		{
			//Create test user
			new User(Database, "TestUser1@example.com", "TestPassword1");
			new User(Database, "TestUser2@example.com", "TestPassword2");

			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=Administrator,TestUser1@example.com", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);

			Assert.IsTrue((string)Data[0]["Email"] == "Administrator");
			Assert.IsTrue((string)Data[1]["Email"] == "TestUser1@example.com");
		}

		/// <summary>
		/// Check if we get no results when given invalid params
		/// </summary>
		[TestMethod]
		public void GET_InvalidArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=SomeAccount", HttpMethod.GET);

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);
			Assert.IsTrue(Data.Count == 0);
		}

		/// <summary>
		/// Check if we get no results when given multiple invalid params
		/// </summary>
		[TestMethod]
		public void GET_BulkInvalidArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=SomeAccount,SomeOtherAccount", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);
			Assert.IsTrue(Data.Count == 0);
		}

		/// <summary>
		/// Check if we get one result when given one valid and one invalid param
		/// </summary>
		[TestMethod]
		public void GET_MixedArguments()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=Administrator,SomeUser", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);

			Assert.IsTrue(Data.Count == 1);
			Assert.IsTrue((string)Data[0]["Email"] == "Administrator");
		}

		/// <summary>
		/// Check if we can retrieve the current logged in user if we give CurrentUser as email parameter
		/// </summary>
		[TestMethod]
		public void GET_CurrentUser()
		{
			//Create mock request
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=CurrentUser", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);

			Assert.IsTrue(Data.Count == 1);
			Assert.IsTrue((string)Data[0]["Email"] == "Administrator");
		}

		/// <summary>
		/// Check if we can retrieve all users if we give no email parameter
		/// </summary>
		[TestMethod]
		public void GET_AllUsers()
		{
			//Create test users
			new User(Database, "TestUser1@example.com", "TestPassword1");
			new User(Database, "TestUser2@example.com", "TestPassword2");

			//Create mock request
			ResponseProvider Response = ExecuteSimpleRequest("/api/account", HttpMethod.GET, contentType: "application/json");

			//Verify results
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.OK);
			var Data = JArray.Parse(Response.Data);

			Assert.IsTrue((string)Data[1]["Email"] == "TestUser1@example.com");
			Assert.IsTrue((string)Data[2]["Email"] == "TestUser2@example.com");
		}
	}
}