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
		///		api/feedItem?id=[id]
		///		
		/// Feed items by limit and offset:
		///		api/feedItem&limit=[x]&offset=[y]
		/// 
		///		Examples:
		///		To get last 10 feed items: api/feedItem?limit=10&offset=0
		///		To get feed items 21 to 30: api/feedItem?limit=15&offset=20
		///		
		/// Feed items by category, limit and offset:
		///		api/feedItem?category=[category]&limit=[x]&offset=[y]
		///		
		/// Feed items by search string, limit and offset:
		///		api/feedItem?search_string=[string_to_search_for]&limit=[x]&offset=[y]
		/// </summary>
		[Permission(PermissionLevel.User)]
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
			// If a category, a limit and an offset are given, the feed items with the given category are requested, and that start at the
			// offset until the limit is reached.
			else if (Params.ContainsKey("category") && Params.ContainsKey("limit") && Params.ContainsKey("offset"))
			{
				// Check if the limit and offset values are both non-negative integers.
				if (int.TryParse(Params["limit"][0], out int limit) && int.TryParse(Params["offset"][0], out int offset) && limit >= 0 && offset >= 0)
				{
					string category = Params["category"][0];

					// Check if the given category is a valid feed item category.
					if (!FeedItem.IsCategoryValid(category))
					{
						Response.Send("Category " + category + " not supported", HttpStatusCode.BadRequest);
						return;
					}

					// Get the feed items with the category from the database.
					List<FeedItem> feedItems = FeedItem.GetFeedItemsByCategory(Database, category, limit, offset);

					var json = JsonSerializer.Serialize(feedItems);
					Response.Send(json.ToString(), HttpStatusCode.OK);
				}
				else
				{
					Response.Send("Limit or offset value not valid: they must both be non-negative integers.", HttpStatusCode.BadRequest);
				}
			}
			// If a category, a limit and an offset are given, the feed items that contain the given search string are requested, and that start
			// at the offset until the limit is reached.
			else if (Params.ContainsKey("search_string") && Params.ContainsKey("limit") && Params.ContainsKey("offset"))
			{
				// Check if the limit and offset values are both non-negative integers.
				if (int.TryParse(Params["limit"][0], out int limit) && int.TryParse(Params["offset"][0], out int offset) && limit >= 0 && offset >= 0)
				{
					// Get the feed items that contain the given search string from the database.
					List<FeedItem> feedItems = FeedItem.GetFeeditemsBySearchString(Database, Params["search_string"][0], limit, offset);

					var json = JsonSerializer.Serialize(feedItems);
					Response.Send(json.ToString(), HttpStatusCode.OK);
				}
				else
				{
					Response.Send("Limit or offset value not valid: they must both be non-negative integers.", HttpStatusCode.BadRequest);
				}
			}
			// If a limit and an offset are given, the feed items are requested that start at the offset until the limit is reached.
			else if (Params.ContainsKey("limit") && Params.ContainsKey("offset"))
			{
				// Check if the limit and offset values are both non-negative integers.
				if (int.TryParse(Params["limit"][0], out int limit) && int.TryParse(Params["offset"][0], out int offset) && limit >= 0 && offset >= 0)
				{
					// Get the feed items, starting at offset and until the limit is reached, from the database.
					List<FeedItem> feedItems = FeedItem.GetFeedItems(Database, limit, offset);

					var json = JsonSerializer.Serialize(feedItems);
					Response.Send(json.ToString(), HttpStatusCode.OK);
				}
				else
				{
					Response.Send("Limit or offset value not valid: they must both be non-negative integers.", HttpStatusCode.BadRequest);
				}
			}
			// If the request contains no useful information.
			else
			{
				Response.Send("Missing parameters: provide either of the following: an ID -OR- a limit and offset -OR- a category, limit and offset -OR- a search string, limit and offset.", HttpStatusCode.BadRequest);
			}
		}
	}
}
