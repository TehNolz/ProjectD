using System;
using System.Collections.Generic;
using System.Text;
using Database.SQLite.Modeling;

namespace Webserver.Models
{
	public sealed class Example
	{
		[Primary]
		public int? Id { get; set; }
		public string Message { get; set; }
		public Guid Guid { get; set; } = Guid.NewGuid();
	}
}
