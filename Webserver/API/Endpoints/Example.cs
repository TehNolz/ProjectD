using Newtonsoft.Json.Linq;

namespace Webserver.API.Endpoints
{
	/// <summary>
	/// Example API endpoint, demonstrating basic use of the framework
	/// </summary>
	[Route("EXAMPLE")]
	class Example : APIEndpoint
	{
		/// <summary>
		/// Override the HTTP method you want this endpoint to respond to. Methods that aren't overriden will automatically return a 405 Method Not Allowed
		/// </summary>
		public override void GET() => Response.Send(new JObject(){
				{"test", "Greetings Earth" },
		});
	}
}
