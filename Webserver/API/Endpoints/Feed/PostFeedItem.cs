using System;
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
				!((JObject)Data).TryGetValue("description", out string description) ||
				!((JObject)Data).TryGetValue("category", out string category))
			{
				Response.Send("Missing fields", HttpStatusCode.BadRequest);
				return;
			}

			// Check if the given category is a valid feed item category.
			if (!FeedItem.IsCategoryValid(category))
			{
				Response.Send("Category " + category + " not supported", HttpStatusCode.BadRequest);
				return;
			}

			// Create a new feed item
			FeedItem.FeedItemCategory categoryEnum = FeedItem.GetFeedItemCategoryFromString(category);
			var feedItem = new FeedItem(title, description, categoryEnum);

			// Store feed item in the database
			Database.Insert(feedItem);

			// Send success message
			Response.Send("Feed item successfully created", HttpStatusCode.Created);
		}
	}
}
