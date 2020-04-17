using Newtonsoft.Json.Linq;

namespace Webserver.API.Endpoints
{
	/// <summary>
	/// Example API endpoint, demonstrating basic use of the framework
	/// </summary>
	[Route("example")]
	class Example : APIEndpoint
	{
		public override void GET() => Response.Send(new JObject() { { "test", "Greetings Earth" } },
				System.Net.HttpStatusCode.BadRequest
			);
	}
}
