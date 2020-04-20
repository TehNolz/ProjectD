using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Net;

using Webserver.Webserver;

using WebserverTests.API_Endpoints.Tests;

namespace WebserverTests.API_Endpoints
{
	[TestClass]
	public class Authentication_Tests : APITestMethods
	{

		/// <summary>
		/// Call base ClassInit because it can't be inherited
		/// </summary>
		[ClassInitialize]
		public new static void ClassInit(TestContext C) => APITestMethods.ClassInit(C);

		/// <summary>
		/// Check if we get an Unauthorized status code if we try to use an API method without being logged in.
		/// </summary>
		[TestMethod]
		public void Authentication_LoggedOut()
		{
			ResponseProvider Response = ExecuteSimpleRequest("/api/account?email=Administrator", HttpMethod.GET, login: false);
			Assert.IsTrue(Response.StatusCode == HttpStatusCode.Unauthorized);
		}
	}
}
