using System;
using System.Collections.Generic;
using System.Text;

namespace Webserver.Models
{
    public class FeedItem
    {
        public int ID { get; }
        public string Title { get; set; }
        public string Description { get; set; }

        public FeedItem(string title, string description)
        {
            Title = title;
            Description = description;
        }
    }
}
