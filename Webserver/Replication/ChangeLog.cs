using Database.SQLite;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Math.EC.Rfc7748;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Webserver.Replication
{
	/// <summary>
	/// Represents a collection of <see cref="Changes"/> made to a <see cref="ServerDatabase"/>.
	/// <para/>
	/// Only one <see cref="ChangeLog"/> instance should be made per database.
	/// </summary>
	internal sealed class ChangeLog : IDisposable
	{
		/// <summary>
		/// Gets the most ID of the most recent <see cref="Changes"/> object.
		/// </summary>
		public long Version { get; private set; }

		// TODO: Make these members private and handle transactions differently
		public SQLiteAdapter changeLog;
		public SQLiteAdapter database;

		private SynchronizingHandle synchronizer;

		/// <summary>
		/// Initializes a new instance of <see cref="ChangeLog"/> by creating a new
		/// co-database for the specified <see cref="ServerDatabase"/>.
		/// </summary>
		/// <param name="database">The database to create a changelog for.</param>
		public ChangeLog(ServerDatabase database)
		{
			string changeLogName = $"{Path.GetDirectoryName(database.Connection.FileName)}\\{database.Connection.DataSource}_changelog.db";

			changeLog = new SQLiteAdapter(changeLogName);
			changeLog.TryCreateTable<Changes>();

			// Open an extra connection to the main database in for applying changes
			this.database = new SQLiteAdapter(database.Connection.FileName);

			// Set the synchronizer's id to 1 + the id of the last changelog item
			Version = Peek()?.ID ?? 0;
			synchronizer = new SynchronizingHandle(Version + 1);
		}

		/// <summary>
		/// Pushes the given <paramref name="changes"/> to the changelog database.
		/// <para/>
		/// If <paramref name="applyChanges"/> is <see langword="true"/>, the data in the
		/// <paramref name="changes"/> object will also be applied to the main database.
		/// </summary>
		/// <param name="changes"></param>
		/// <param name="applyChanges"></param>
		public void Push(Changes changes, bool applyChanges = true)
		{
			if (changes.ID <= Version)
				throw new ArgumentException($"The given {nameof(Changes)} object is already present in the changelog.");

			if (changes.ID.HasValue)
			{
				synchronizer.WaitUntilReady(changes.ID.Value);
			}

			if (applyChanges)
			{
				// Get a dynamic[] from the changes' collection JArray (this uses a custom extension method)
				dynamic[] items = changes.Collection.Select(x => x.ToObject(changes.CollectionType)).Cast(changes.CollectionType);

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
				changes.Collection = JArray.FromObject(items);
			}

			// Push the changes onto the stack
			changeLog.Insert(changes);
			Version = changes.ID.Value;

			synchronizer.Increment();
		}

		/// <summary>
		/// Returns the most recent <see cref="Changes"/> object from the changelog database without
		/// removing it.
		/// </summary>
		public Changes Peek() => changeLog.Select<Changes>("1 ORDER BY `ROWID` DESC LIMIT 1").SingleOrDefault();

		/// <summary>
		/// Returns the most recent <see cref="Changes"/> object and removes it from the changelog
		/// database.
		/// </summary>
		public Changes Pop()
		{
			// Pop may be unnescessary
			// Instead, the changestack with the most changes / most recent changes is uses as a reference
			// Though this still poses a problem for unsynchronized databases
			// TODO: Test this stuff
			Program.Log.Debug("TODO: Add database 'pop' functionality.");
			return null;
		}

		/// <summary>
		/// Returns all new <see cref="Changes"/> objects with an ID larger than the given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">The version for which to collect all newer changes.</param>
		/// <param name="limit">An optional limit to how many changes are returned.
		/// Values less than 0 indicate no limit.</param>
		public IEnumerable<Changes> GetNewChanges(long id, long limit = -1)
			=> changeLog.Select<Changes>("1 LIMIT @id,@limit", new { id, limit });

		public void Dispose()
		{
			database.Dispose();
			changeLog.Dispose();
		}
	}

	/// <summary>
	/// A class that provides ID-based synchronization using blocking function calls.
	/// </summary>
	public class SynchronizingHandle : IDisposable
	{
		/// <summary>
		/// Gets the current "un-blocking threshold" value.
		/// </summary>
		/// <remarks>
		/// Calls to <see cref="WaitUntilReady(long)"/> will block while the
		/// given id is larger than this value.
		/// </remarks>
		public long CurrentValue { get; private set; } = 0;

		/// <summary>
		/// A dictionary containing all semaphores that are currently in a waiting state.
		/// </summary>
		private ConcurrentDictionary<long, SemaphoreSlim> waiting = new ConcurrentDictionary<long, SemaphoreSlim>();

		/// <summary>
		/// Initializes a new <see cref="SynchronizingHandle"/> with the specified starting value.
		/// </summary>
		/// <param name="initialValue">The value to create this <see cref="SynchronizingHandle"/> with.</param>
		public SynchronizingHandle(long initialValue = 0)
		{
			CurrentValue = initialValue;
		}

		/// <summary>
		/// Blocks the calling thread while the <see cref="CurrentValue"/> is less than
		/// the given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">The id to block until it is unlocked.</param>
		/// <seealso cref="Increment"/>
		/// <exception cref="ObjectDisposedException">
		/// Thrown when this <see cref="SynchronizingHandle"/> was disposed before or during
		/// the function call.
		/// </exception>
		public void WaitUntilReady(long id)
		{
			var sem = new SemaphoreSlim(1, 1);
			while (true) // this while true loops once at most
			{
				// Blocks all calls after the first attempt
				sem.Wait();

				// Unblock when currentvalue is equal or greater than the id
				lock (this)
				{
					if (isDisposed)
						throw new ObjectDisposedException(null, $"Cannot access a disposed {GetType().Name}.");

					if (id <= CurrentValue)
						return;

					Program.Log.Debug($"Blocking {id} until {id - CurrentValue} more are applied");
					// If the first attempt failed, put the semaphore in the waiting dictionary
					waiting[id] = sem;
				}
			}
		}

		/// <summary>
		/// Increments the <see cref="CurrentValue"/> and unlocks any blocking calls to
		/// <see cref="WaitUntilReady(long)"/> whose id is equal to the new
		/// <see cref="CurrentValue"/>.
		/// </summary>
		/// <seealso cref="WaitUntilReady(long)"/>
		/// <exception cref="ObjectDisposedException"/>
		public void Increment()
		{
			if (isDisposed)
				throw new ObjectDisposedException(null, $"Cannot access a disposed {GetType().Name}.");

			lock (this)
			{
				++CurrentValue;
				// Unlock the next waiting semaphore (if any)
				waiting.TryRemove(CurrentValue, out SemaphoreSlim sem);
				sem?.Release();
			}
		}

		#region IDisposable Support
		private bool isDisposed = false; // To detect redundant calls

		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				if (disposing)
				{
					lock (this)
					{
						// Unlock all semaphores to the waiting threads throw an exception.
						foreach (SemaphoreSlim sem in waiting.Values)
							sem.Release();
						waiting.Clear();
					}
				}
				isDisposed = true;
			}
		}

		// This code added to correctly implement the disposable pattern.
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
		}
		#endregion
	}
}
