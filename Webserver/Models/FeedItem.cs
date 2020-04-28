using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
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
		public string Category { get; set; }

		/// <summary>
		/// Parameterless constructor to make SQLiteAdapter happy.
		/// </summary>
		public FeedItem() { }

		/// <summary>
		/// Creates a new feed item.
		/// </summary>
		/// <param name="title">The feed item's title.</param>
		/// <param name="description">The feed item's description.</param>
		public FeedItem(SQLiteAdapter database, string title, string description, string category)
		{
			Title = title;
			Description = description;
			Category = category;

			database.Insert(this);
		}

		/// <summary>
		/// Gets the feed item by the given ID.
		/// </summary>
		/// <param name="database">The database in which to search for the feed item.</param>
		/// <param name="id">The ID of the desired feed item.</param>
		/// <returns>The first feed item with the given ID. Null if no feed item was found.</returns>
		public static FeedItem GetFeedItemByID(SQLiteAdapter database, int id)
		{
			return database.Select<FeedItem>("ID = @id", new { id }).FirstOrDefault();
		}
	}
}
