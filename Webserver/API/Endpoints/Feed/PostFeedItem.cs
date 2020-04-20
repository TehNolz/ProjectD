using System.Net;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
	public partial class FeedItemEndpoint : APIEndpoint
	{
		[ContentType("application/json")]
		public override void POST()
		{
			// Check if title and description are in the body
			if (!((JObject)Data).TryGetValue("title", out string title) ||
				!((JObject)Data).TryGetValue("description", out string description))
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			// Create a new feed item
			var feedItem = new FeedItem(Database, title, description);

			// Store feed item in the database
			Database.Insert(feedItem);

			// Send success message
			Response.Send("Feed item successfully created", HttpStatusCode.Created);
		}
	}
}
