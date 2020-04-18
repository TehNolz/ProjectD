using Database.SQLite.Modeling;

using System;

namespace Webserver.Models
{
	[Table("Example")]
	public sealed class ExampleModel
	{
		[Primary]
		public int? Id { get; set; }
		public string Message { get; set; }
		public Guid Guid { get; set; } = Guid.NewGuid();
	}
}
