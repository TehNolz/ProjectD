using System;
using System.Collections.Generic;
using System.Text;
using Database.SQLite;
using Database.SQLite.Modeling;

namespace Webserver.Models
{
	public class FeedItem
	{
		[Primary]
		public int? ID { get; set; } = null;
		public string Title { get; set; }
		public string Description { get; set; }

		/// <summary>
		/// Parameterless constructor to make SQLiteAdapter happy.
		/// </summary>
		public FeedItem() { }

		/// <summary>
		/// Creates a new feed item.
		/// </summary>
		/// <param name="title">The feed item's title.</param>
		/// <param name="description">The feed item's description.</param>
		public FeedItem(SQLiteAdapter database, string title, string description)
		{
			Title = title;
			Description = description;

			database.Insert(this);
		}
	}
}
