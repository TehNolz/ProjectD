using Microsoft.VisualStudio.TestTools.UnitTesting;
using Webserver;
using Webserver.API;

namespace WebserverTests
{
	[TestClass]
	class AssemblyInit
	{
		[AssemblyInitialize]
		public static void Init(TestContext _)
		{
			APIEndpoint.DiscoverEndpoints();
		}
	}
}
