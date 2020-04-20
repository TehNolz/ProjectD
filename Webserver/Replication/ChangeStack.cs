using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Webserver.Replication
{
	internal sealed class ChangeStack : IDisposable
	{
		public SQLiteAdapter changeLog;
		public SQLiteAdapter database;

		public ChangeStack(ServerDatabase database)
		{
			string changeLogName = $"{Path.GetDirectoryName(database.Connection.FileName)}\\{database.Connection.DataSource}_changelog.db";

			changeLog = new SQLiteAdapter(changeLogName);
			changeLog.TryCreateTable<Changes>();

			this.database = new SQLiteAdapter(database.Connection.FileName);
		}

		public void Push(Changes changes, bool applyChanges = true)
		{
			if (changes.Id <= Peek()?.Id)
				throw new ArgumentException($"The given {nameof(Changes)} object is already present in the changelog.");

			lock (this)
			{
				if (applyChanges)
				{
					// Get a dynamic[] from the changes' collection JArray (this uses a custom extension method)
					dynamic[] items = changes.Collection.Select(x => x.ToObject(changes.CollectionType)).Cast(changes.CollectionType);

					bool AutoAssignRowId_cache = database.AutoAssignRowId;
					database.AutoAssignRowId = false;

					// Apply changes
					switch (changes.Type)
					{
						case ChangeType.INSERT:
							Utils.InvokeGenericMethod<long>((Func<IList<object>, long>)database.Insert,
								changes.CollectionType,
								new[] { items }
							);
							break;
						case ChangeType.UPDATE:
							Utils.InvokeGenericMethod<int>((Func<IList<object>, int>)database.Update,
								changes.CollectionType,
								new[] { items }
							);
							break;
						case ChangeType.DELETE:
							Utils.InvokeGenericMethod<int>((Func<IList<object>, int>)database.Delete,
								changes.CollectionType,
								new[] { items }
							);
							break;
						default:
							throw new ArgumentOutOfRangeException(nameof(changes.Type));
					}
					database.AutoAssignRowId = AutoAssignRowId_cache;
				}

				// Push the changes onto the stack
				changeLog.Insert(changes);
			}
		}

		public Changes Peek() => changeLog.Select<Changes>("1 ORDER BY `ROWID` DESC LIMIT 1").SingleOrDefault();

		public Changes Pop()
		{
			// Pop may be unnescessary
			// Instead, the changestack with the most changes / most recent changes is uses as a reference
			// Though this still poses a problem for unsynchronized databases
			// TODO: Test this stuff
			throw new NotImplementedException();
		}

		public IEnumerable<Changes> Crawl(long id)
			=> changeLog.Select<Changes>("1 ORDER BY `ROWID` DESC").TakeWhile(x => x.Id != id);

		public void Dispose()
		{
			changeLog.Dispose();
		}
	}
}
