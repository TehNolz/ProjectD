using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Webserver.Models;

namespace Webserver.API.Endpoints.Feed
{
    [Route("feedItem")]
    public class PostFeedItem : APIEndpoint
    {
        [ContentType("application/json")]
        public override void POST()
        {
            // Check if title and description are in the parameters
            if (!((JObject)Data).TryGetValue<string>("title", out JToken title) ||
                !((JObject)Data).TryGetValue<string>("description", out JToken description))
            {
                Response.Send("Missing fields", HttpStatusCode.BadRequest);
                return;
            }

            // Create a new feed item
            FeedItem feedItem = new FeedItem((string)title, (string)description);

            // TODO: Store feedItem in database

            // Send success message
            Response.Send(HttpStatusCode.Created);
        }
    }
}
