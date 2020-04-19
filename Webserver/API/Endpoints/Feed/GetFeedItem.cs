using System.Net;
using Newtonsoft.Json.Linq;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
    [Route("feedItem")]
    public class GetFeedItem : APIEndpoint
    {
        [ContentType("application/json")]
        public override void GET()
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

            JObject json = JObject.FromObject(feedItem);
            Response.Send(json.ToString(), HttpStatusCode.OK);
        }
    }
}
