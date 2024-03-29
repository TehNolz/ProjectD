using Database.SQLite;
using Database.SQLite.Modeling;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Webserver.Models
{
	public class FeedItem
	{
		[Primary]
		public int? ID { get; set; } = null;
		public string Title { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string UserEmail { get; set; }

		/// <summary>
		/// Parameterless constructor to make SQLiteAdapter happy.
		/// </summary>
		public FeedItem() { }

		/// <summary>
		/// Creates a new feed item.
		/// </summary>
		/// <param name="title">The feed item's title.</param>
		/// <param name="description">The feed item's description.</param>
		/// <param name="category">The feed item's category.</param>
		/// <param name="userEmail">The user's email who made the feed item.</param>
		public FeedItem(string title, string description, string category, string userEmail)
		{
			Title = title;
			Description = description;
			Category = category;
			UserEmail = userEmail;
		}

		/// <summary>
		/// Gets all the feed items.
		/// </summary>
		/// <param name="database">The database in which to get the feed items.</param>
		/// <returns>A list of all the feed items in the database.</returns>
		public static List<FeedItem> GetAllFeedItems(SQLiteAdapter database) => database.Select<FeedItem>().ToList();

		/// <summary>
		/// Gets alls the feed items.
		/// </summary>
		/// <param name="database">The database in which to get the feed items.</param>
		/// <returns>A list of all feed items.</returns>
		public static List<FeedItem> GetFeedItems(SQLiteAdapter database) => database.Select<FeedItem>("1 ORDER BY ID DESC").ToList();

		/// <summary>
		/// Gets the feed items based on the given limit and offset.
		/// </summary>
		/// <param name="database">The database in which to get the feed items.</param>
		/// <param name="limit">The total amount of feed items to get.</param>
		/// <param name="offset">The first amount of feed items to exclude.</param>
		/// <returns>A list of feed items based on the given limit and offset.</returns>
		public static List<FeedItem> GetFeedItems(SQLiteAdapter database, int limit, int offset) => database.Select<FeedItem>("1 ORDER BY ID DESC LIMIT @limit OFFSET @offset", new { limit, offset }).ToList();

		/// <summary>
		/// Gets the feed item by the given ID.
		/// </summary>
		/// <param name="database">The database in which to search for the feed item.</param>
		/// <param name="id">The ID of the desired feed item.</param>
		/// <returns>The first feed item with the given ID. Null if no feed item was found.</returns>
		public static FeedItem GetFeedItemByID(SQLiteAdapter database, int id) => database.Select<FeedItem>("ID = @id", new { id }).FirstOrDefault();

		/// <summary>
		/// Gets the feed items by the given category.
		/// </summary>
		/// <param name="database">The database in which to search for the feed items.</param>
		/// <param name="category">The category of the desired feed items.</param>
		/// <param name="limit">The total amount of feed items with the category to get.</param>
		/// <param name="offset">The first amount of feed items with the category to exclude.</param>
		/// <returns>A list of feed items with the given category.</returns>
		public static List<FeedItem> GetFeedItemsByCategory(SQLiteAdapter database, string category, int limit, int offset) => GetFeedItems(database).Where(f => f.Category == category).Skip(offset).Take(limit).ToList();

		/// <summary>
		/// Get the feed items which title or description contains the given search string. This is case-insensitive.
		/// </summary>
		/// <param name="database">The database in which to search for the feed items.</param>
		/// <param name="searchString">The search string to check if the title or description contains.</param>
		/// <param name="limit">The total amount of feed items with the search string to get.</param>
		/// <param name="offset">The first amount of feed items with the search string to exclude.</param>
		/// <returns>A list of feed items which title or description contains the given search string.</returns>
		public static List<FeedItem> GetFeeditemsBySearchString(SQLiteAdapter database, string searchString, int limit, int offset) => GetFeedItems(database).Where(f => f.Title.ToLower().Contains(searchString.ToLower()) ||
																																													 f.Description.ToLower().Contains(searchString.ToLower())).Skip(offset).Take(limit).ToList();

		/// <summary>
		/// Determines if the given category can be parsed to a feed item category.
		/// </summary>
		/// <param name="category">The category to check if it can be parsed to a feed item category.</param>
		/// <returns>True if the given category can be parsed to a feed item category, false otherwise.</returns>
		public static bool IsCategoryValid(string category) => Enum.TryParse(category, out FeedItemCategory _);

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
