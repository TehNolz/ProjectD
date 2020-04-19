using System.Net;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
    [Route("feedItem")]
    public class EditFeedItem : APIEndpoint
    {
        [ContentType("application/json")]
        public override void PATCH()
        {
            // Check if ID is in the body
            if (!((JObject)Data).TryGetValue<string>("id", out JToken ID))
            {
                Response.Send("Missing ID", HttpStatusCode.BadRequest);
                return;
            }

            // TODO: Get the feed item from the database
            FeedItem feedItem = null;

            // Check if the feed item exists
            if (feedItem == null)
            {
                Response.Send("Feed item not found", HttpStatusCode.NotFound);
                return;
            }

            // Change the properties of the feed item
            if (!((JObject)Data).TryGetValue<string>("newTitle", out JToken newTitle))
                feedItem.Title = (string)newTitle;
            if (!((JObject)Data).TryGetValue<string>("newDescription", out JToken newDescription))
                feedItem.Description = (string)newDescription;

            // TODO: Update feed item in database

            Response.Send("Feed item has successfully been edited", HttpStatusCode.OK);
        }
    }
}
