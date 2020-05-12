using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
	[Route("/feedItem")]
	public partial class FeedItemEndpoint : APIEndpoint
	{
		/// <summary>
		/// Usage:
		/// 
		/// Feed item by ID:
		///		api/feedItem/?id=[id]
		///		
		/// Feed items by category:
		///		api/feedItem/?category=[category]
		///		
		/// Feed items by limit and offset:
		///		api/feedItem/?limit=10&offset=0 (to get last 10 feed items)
		///		api/feedItem/?limit=15&offset=20 (to get feed items 21 to 30)
		/// </summary>
		public override void GET()
		{
			// If an ID is given, one feed item with that ID is requested.
			if (Params.ContainsKey("id"))
			{
				// Get the feed item from the database.
				var feedItem = FeedItem.GetFeedItemByID(Database, int.Parse(Params["id"][0]));

				// Check if the feed item exists.
				if (feedItem == null)
				{
					Response.Send("Feed item not found", HttpStatusCode.NotFound);
					return;
				}

				var json = JObject.FromObject(feedItem);
				Response.Send(json.ToString(), HttpStatusCode.OK);
			}
			// If a category is given, all feed items with that category are requested.
			else if (Params.ContainsKey("category"))
			{
				string category = Params["category"][0];

				// Check if the given category is a valid feed item category.
				if (!FeedItem.IsCategoryValid(category))
				{
					Response.Send("Category " + category + " not supported", HttpStatusCode.BadRequest);
					return;
				}

				// Get the feed items with the category from the database.
				List<FeedItem> feedItems = FeedItem.GetFeedItemsByCategory(Database, category);

				var json = JsonSerializer.Serialize(feedItems);
				Response.Send(json.ToString(), HttpStatusCode.OK);
			}
			// If a limit and an offset are given, the feed items are requested that start at the offset until the limit is reached.
			else if (Params.ContainsKey("limit") && Params.ContainsKey("offset"))
			{
				// Check if the amount value is an int.
				if (int.TryParse(Params["limit"][0], out int limit) && int.TryParse(Params["offset"][0], out int offset))
				{
					// Get the feed items, starting at offset and until the limit is reached, from the database.
					List<FeedItem> feedItems = FeedItem.GetFeedItems(Database, limit, offset);

					var json = JsonSerializer.Serialize(feedItems);
					Response.Send(json.ToString(), HttpStatusCode.OK);
				}
				else
				{
					Response.Send("Limit or offset value not valid: they must both be integers.", HttpStatusCode.BadRequest);
				}
			}
			// If the request contains no useful information.
			else
			{
				Response.Send("Missing parameters: provide an ID, a category or a limit and offset.", HttpStatusCode.BadRequest);
			}
		}
	}
}
