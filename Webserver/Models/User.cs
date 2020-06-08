using Database.SQLite;
using Database.SQLite.Modeling;

using Newtonsoft.Json.Linq;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Webserver.Models
{
	public class User
	{
		[Primary]
		public Guid ID { get; set; } = Guid.NewGuid();
		public string Username { get; set; }
		public string Email { get; set; }
		public string PasswordHash { get; set; }
		public PermissionLevel PermissionLevel { get; set; } = PermissionLevel.User;
		public virtual bool isAdmin => PermissionLevel == PermissionLevel.Admin;

		/// <summary>
		/// Parameterless constructor to make SQLiteAdapter happy.
		/// </summary>
		public User() { }

		/// <summary>
		/// Creates a new user. The new user object will not be usable in the system until its inserted into the database.
		/// </summary>
		/// <param name="email">The user's email address</param>
		/// <param name="password">The user's password. This will be converted into a salted hash and stored in the PasswordHash field.</param>
		public User(string email, string password)
		{
			Email = email;
			Username = email.Split('@').First();
			PasswordHash = CreateHash(password, email);
		}

		/// <summary>
		/// Change this user's password. The change will not be applied the object is updated in the database.
		/// </summary>
		/// <param name="newPassword">The new password</param>
		public void ChangePassword(SQLiteAdapter database, string newPassword)
		{
			PasswordHash = CreateHash(newPassword, Email);
			database.Update(this);
		}

		/// <summary>
		/// Given a password and salt, returns a salted SHA512 hash.
		/// </summary>
		/// <param name="password">The password.</param>
		/// <param name="salt">The salt to use.</param>
		/// <returns>The new password hash.</returns>
		public static string CreateHash(string password, string salt)
		{
			if (string.IsNullOrEmpty(password))
			{
				throw new ArgumentException("message", nameof(password));
			}

			byte[] passBytes = Encoding.UTF8.GetBytes(password);
			byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
			using var sha = SHA512.Create();
			return string.Concat(sha
				.ComputeHash(passBytes.Concat(saltBytes).ToArray())
				.Select(item => item.ToString("x2")));
		}

		/// <summary>
		/// Get a user by their email address.
		/// </summary>
		/// <param name="database">The database connection to query</param>
		/// <param name="email">The email to search for</param>
		/// <returns>The user with the specified email address. Null if the user doesn't exist.</returns>
		public static User GetByEmail(SQLiteAdapter database, string email) => database.Select<User>("Email = @email", new { email }).FirstOrDefault();

		/// <summary>
		/// Get a JSON object representing this user. Possibly sensitive data is stripped out.
		/// </summary>
		/// <returns></returns>
		public JObject GetJson() => new JObject()
		{
			{"ID", ID },
			{"Username", Username },
			{"PermissionLevel", (int)PermissionLevel }
		};
	}

	public enum PermissionLevel
	{
		User,
		Admin,
	}
}
