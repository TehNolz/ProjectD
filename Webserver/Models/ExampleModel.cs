using Database.SQLite.Modeling;

using System;

namespace Webserver.Models
{
	[Table("Example")]
	public sealed class ExampleModel
	{
		[Primary]
		public int? ID { get; set; }
		public string Message { get; set; }
		public string GuidStr { get; set; } = Guid.NewGuid().ToString();
	}
}
