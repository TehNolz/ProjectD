using System.Linq;
using System.Net;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
	public partial class FeedItemEndpoint : APIEndpoint
	{
		public override void DELETE()
		{
			// Check if ID is in the parameters
			if (!Params.ContainsKey("id"))
			{
				Response.Send("Missing ID", HttpStatusCode.BadRequest);
				return;
			}

			// Get the feed item from the database
			var feedItem = FeedItem.GetFeedItemByID(Database, int.Parse(Params["id"][0]));

			// Check if the feed item exists
			if (feedItem == null)
			{
				Response.Send("Feed item not found", HttpStatusCode.NotFound);
				return;
			}

			// Delete feed item from the databse
			Database.Delete(feedItem);

			Response.Send("Feed item successfully deleted", HttpStatusCode.OK);
		}
	}
}
