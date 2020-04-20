using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Webserver.Replication;

namespace WebserverTests.Replication
{
	[TestClass]
	public partial class DatabaseTests
	{
		[TestMethod]
		public void CreateDatabase()
		{
			fuck();
			GC.Collect();
			Trace.WriteLine("ech");
		}

		private static void fuck()
		{
			var db = new ServerDatabase("Database.db");
			var db1 = db.NewConnection();
			var db2 = db1.NewConnection();
			Trace.WriteLine("ech");
		}
	}
}
