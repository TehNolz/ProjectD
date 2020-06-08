using Newtonsoft.Json.Linq;

using System.Net;

using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
	public partial class FeedItemEndpoint : APIEndpoint
	{
		[ContentType("application/json")]
		[Permission(PermissionLevel.User)]
		public override void PATCH()
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

			// Check if the user's email is the logged in user's email
			if (feedItem.UserEmail != User.Email)
			{
				Response.Send("Feed item does not belong to logged in user.", HttpStatusCode.Forbidden);
				return;
			}

			// Change the properties of the feed item
			if (((JObject)Data).TryGetValue("newTitle", out string newTitle))
				feedItem.Title = newTitle;

			if (((JObject)Data).TryGetValue("newDescription", out string newDescription))
				feedItem.Description = newDescription;

			if (((JObject)Data).TryGetValue("newCategory", out string newCategory))
				feedItem.Category = newCategory;

			// Update feed item in the database
			Database.Update(feedItem);

			Response.Send("Feed item successfully been edited", HttpStatusCode.OK);
		}
	}
}
