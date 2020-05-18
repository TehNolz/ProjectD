using Newtonsoft.Json.Linq;

using Webserver.LoadBalancer;

namespace Webserver.API.Endpoints
{
	/// <summary>
	/// Example API endpoint, demonstrating basic use of the framework
	/// </summary>
	[Route("example")]
	internal class Example : APIEndpoint
	{
		public override void GET() => Response.Send(JArray.FromObject(ServerConnection.BroadcastAndWait(new ServerMessage(MessageType.DebugType, 1))));

		[EventMessageType(MessageType.DebugType)]
		public static void TestHandler(ServerMessage message) => message.Reply(2);
	}
}
