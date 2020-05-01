using Microsoft.VisualStudio.TestTools.UnitTesting;

using Webserver.API;

namespace WebserverTests
{
	[TestClass]
	internal class AssemblyInit
	{
		[AssemblyInitialize]
		public static void Init(TestContext _) => APIEndpoint.DiscoverEndpoints();
	}
}
