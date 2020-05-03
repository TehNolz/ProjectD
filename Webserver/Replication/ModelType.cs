using Database.SQLite.Modeling;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Webserver.Replication
{
	/// <summary>
	/// Represents a row in the __types table that holds the names of the types referenced
	/// by <see cref="Changes"/>.
	/// </summary>
	[Table("__types")]
	public class ModelType
	{
		[AutoIncrement]
		public int? ID { get; set; }
		public string FullName { get; set; }

		public static implicit operator ModelType(Type type) => new ModelType() { FullName = type.FullName };
		public static implicit operator Type(ModelType type) => Assembly.GetExecutingAssembly().GetType(type.FullName);
	}
}
