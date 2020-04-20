using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
	[Route("/feedItem")]
	public partial class FeedItemEndpoint : APIEndpoint
	{
		public override void GET()
		{
			// Check if ID is in the parameters
			if (!Params.ContainsKey("id"))
			{
				Response.Send("Missing ID", HttpStatusCode.BadRequest);
				return;
			}

			// Get the feed item from the database
			var feedItem = Database.Select<FeedItem>("ID = @id", new { id = Params["id"][0] }).FirstOrDefault();

			// Check if the feed item exists
			if (feedItem == null)
			{
				Response.Send("Feed item not found", HttpStatusCode.NotFound);
				return;
			}

			var json = JObject.FromObject(feedItem);
			Response.Send(json.ToString(), HttpStatusCode.OK);
		}
	}
}
