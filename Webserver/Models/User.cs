using Database.SQLite.Modeling;
using System;
using System.Collections.Generic;
using System.Text;

namespace Webserver.Models
{
	public class User
	{
		[Primary]
		public Guid ID { get; set; } = Guid.NewGuid();

		public string Username { get; set; }
		public string PasswordHash { get; set; }
		public string Email { get; set; }
		public PermissionLevel PermissionLevel { get; set; }
	}

	public enum PermissionLevel
	{
		User,
		Admin,
	}
}
