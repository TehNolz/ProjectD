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
		public FeedItemCategory Category { get; set; }

		/// <summary>
		/// Parameterless constructor to make SQLiteAdapter happy.
		/// </summary>
		public FeedItem() { }

		/// <summary>
		/// Creates a new feed item.
		/// </summary>
		/// <param name="title">The feed item's title.</param>
		/// <param name="description">The feed item's description.</param>
		public FeedItem(SQLiteAdapter database, string title, string description, FeedItemCategory category)
		{
			Title = title;
			Description = description;
			Category = category;

			database.Insert(this);
		}

		/// <summary>
		/// Gets all the feed items.
		/// </summary>
		/// <param name="database">The database in which to get the feed items.</param>
		/// <returns>A list of all the feed items in the database.</returns>
		public static List<FeedItem> GetAllFeedItems(SQLiteAdapter database)
		{
			return database.Select<FeedItem>().ToList();
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

		/// <summary>
		/// Gets the feed items by the given category.
		/// </summary>
		/// <param name="database">The database in which to search for the feed items.</param>
		/// <param name="category">The category of the desired feed items.</param>
		/// <returns>A list of feed items with the given category.</returns>
		public static List<FeedItem> GetFeedItemsByCategory(SQLiteAdapter database, FeedItemCategory category)
		{
			return database.Select<FeedItem>("Category = @category", new { category }).ToList();
		}

		/// <summary>
		/// Determines if the given category can be parsed to a feed item category.
		/// </summary>
		/// <param name="category">The category to check if it can be parsed to a feed item category.</param>
		/// <returns>True if the given category can be parsed to a feed item category, false otherwise.</returns>
		public static bool IsCategoryValid(string category)
		{
			return Enum.TryParse(category, out FeedItemCategory _);
		}

		/// <summary>
		/// Gets the feed item category based on the give category as string.
		/// </summary>
		/// <param name="category">The category as string.</param>
		/// <returns>The feed item category that represents the given category as string.</returns>
		/// <exception cref="ArgumentException">Thrown if the given category as string is not a valid feed item category.</exception>
		public static FeedItemCategory GetFeedItemCategoryFromString(string category)
		{
			if (!IsCategoryValid(category))
			{
				throw new ArgumentException("The given category as string is not a valid feed item category.");
			}

			return (FeedItemCategory)Enum.Parse(typeof(FeedItemCategory), category);
		}

		/// <summary>
		/// A feed item category.
		/// </summary>
		public enum FeedItemCategory
		{
			General,
			Personal,
			Note
		}
	}
}
