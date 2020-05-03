using Database.SQLite.Modeling;

using System;
using System.Reflection;

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
		[Unique]
		public string FullName { get; set; }

		public override bool Equals(object obj) => obj is ModelType other ? other.ID == ID || other.FullName == FullName : base.Equals(obj);
		public override int GetHashCode() => FullName?.GetHashCode() ?? ID?.GetHashCode() ?? base.GetHashCode();

		public static bool operator ==(ModelType a, ModelType b) => a.Equals(b);
		public static bool operator !=(ModelType a, ModelType b) => !(a == b);
		public static bool operator ==(ModelType a, Type b) => a?.FullName == b?.FullName;
		public static bool operator !=(ModelType a, Type b) => !(a == b);

		public static implicit operator ModelType(Type type) => new ModelType() { FullName = type.FullName };
		public static implicit operator Type(ModelType type) => Assembly.GetExecutingAssembly().GetType(type.FullName);
	}
}
