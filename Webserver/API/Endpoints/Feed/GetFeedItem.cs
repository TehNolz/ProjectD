using System.Collections.Generic;
using System.Linq;
using System.Net;
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
		/// All feed items:
		///		api/feedItem/
		/// </summary>
		public override void GET()
		{
			// If an ID is given, one feed item with that ID is requested
			if (Params.ContainsKey("id"))
			{
				// Get the feed item from the database
				var feedItem = FeedItem.GetFeedItemByID(Database, int.Parse(Params["id"][0]));

				// Check if the feed item exists
				if (feedItem == null)
				{
					Response.Send("Feed item not found", HttpStatusCode.NotFound);
					return;
				}

				var json = JObject.FromObject(feedItem);
				Response.Send(json.ToString(), HttpStatusCode.OK);
			}
			// If a category is given, all feed items with that category are requested
			else if (Params.ContainsKey("category"))
			{
				string category = Params["category"][0];

				// Check if the given category is a valid feed item category.
				if (!FeedItem.IsCategoryValid(category))
				{
					Response.Send("Category " + category + " not supported", HttpStatusCode.BadRequest);
					return;
				}

				// Get the feed items with the category from the database
				List<FeedItem> feedItems = FeedItem.GetFeedItemsByCategory(Database, FeedItem.GetFeedItemCategoryFromString(category));

				var json = JObject.FromObject(feedItems);
				Response.Send(json.ToString(), HttpStatusCode.OK);
			}
			// If nothing is given, all feed items are requested
			else if (Params.Keys.Count == 0)
			{
				List<FeedItem> feedItems = FeedItem.GetAllFeedItems(Database);

				//var json = JObject.FromObject(feedItems);
				//Response.Send(json.ToString(), HttpStatusCode.OK);
			}
		}
	}
}
