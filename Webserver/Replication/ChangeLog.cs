using Database.SQLite;

using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
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

		/// <summary>
		/// Gets the collection of model types listed in the database.
		/// </summary>
		public ReadOnlyCollection<ModelType> TypeList => typeList.AsReadOnly();

		private List<ModelType> typeList;
		private SynchronizingHandle synchronizer;

		private readonly SQLiteAdapter database;
		private readonly ServerDatabase parentDatabase;

		/// <summary>
		/// Initializes a new instance of <see cref="ChangeLog"/> by creating a new
		/// co-database for the specified <see cref="ServerDatabase"/>.
		/// </summary>
		/// <param name="database">The database to create a changelog for.</param>
		public ChangeLog(ServerDatabase database)
		{
			// Keep a reference to the parent to copy settings such as StoreEnumsAsText when applying changes
			parentDatabase = database;

			// Open an extra connection to the database in for applying changes
			this.database = new SQLiteAdapter(database.Connection.FileName)
			{
				StoreEnumsAsText = false
			};

			// Add the changelog to the database
			this.database.CreateTableIfNotExists<ModelType>();
			this.database.CreateTableIfNotExists<Changes>();

			// Build the typeList cache
			typeList = this.database.Select<ModelType>().ToList();

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

			// Block until it is this id's turn to be applied
			if (changes.ID.HasValue)
				synchronizer.WaitUntilReady(changes.ID.Value);

			// Assign the foreign key to the changes object
			ModelType type;
			lock (typeList)
			{
				type = typeList.FirstOrDefault(x => x == changes.CollectionType);
				if (type is null)
				{
					// Insert a new type if it doesn't already exist
					type = new ModelType() { FullName = changes.CollectionType.FullName };
					database.Insert(type);
					typeList.Add(type);
				}
			}
			changes.CollectionType = type;

			if (applyChanges)
			{
				dynamic[] items;

				// Unpack the changes object's data
				if (changes.Type.Value.HasFlag(ChangeType.WithCondition))
				{
					// Convert the first element to string and the second (if present) to dynamic
					items = new dynamic[2];
					items[0] = (string)changes.Collection[0];
					items[1] = (System.Collections.IDictionary)new Dictionary<string, JToken>(
						(JObject)changes.Collection.ElementAtOrDefault(1))
							.ToDictionary(x => x.Key, x => x.Value.ToObject<object>()
					);
				}
				else
				{
					// Get a dynamic[] from the changes' collection JArray (this uses a custom extension method)
					items = changes.ExpandCollection().Select(x => x.ToObject(changes.CollectionType)).Cast(changes.CollectionType);
				}

				// Copy parent db settings
				(bool, bool) oldSettings = (database.AutoAssignRowId, database.StoreEnumsAsText);
				(database.AutoAssignRowId, database.StoreEnumsAsText) = (parentDatabase.AutoAssignRowId, parentDatabase.StoreEnumsAsText);

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
					case ChangeType.DELETE | ChangeType.WithCondition:
						Utils.InvokeGenericMethod<int>((Func<string, object, int>)database.Delete<object>,
							changes.CollectionType,
							items
						);
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(changes.Type));
				}

				if (!changes.Type.Value.HasFlag(ChangeType.WithCondition))
					changes.SetCollection(items as IList<object>);

				// Reset db settings
				(database.AutoAssignRowId, database.StoreEnumsAsText) = oldSettings;
			}

			// Push the changes onto the stack
			database.Insert(changes);
			Version = changes.ID.Value;

			synchronizer.Increment();
		}

		/// <summary>
		/// Returns the most recent <see cref="Changes"/> object from the changelog database without
		/// removing it.
		/// </summary>
		public Changes Peek()
		{
			Changes changes = database.Select<Changes>("1 ORDER BY `ROWID` DESC LIMIT 1").SingleOrDefault();
			if (!(changes is null))
				changes.CollectionType = typeList.First(x => x.ID == changes.ModelTypeID);
			return changes;
		}

		/// <inheritdoc cref="SQLiteConnection.BeginTransaction()"/>
		public SQLiteTransaction BeginTransaction()
		{
			SQLiteTransaction transaction = database.Connection.BeginTransaction();

			database.Connection.RollBack += OnRollback;

			return transaction;
		}

		private void OnRollback(object sender, EventArgs e)
		{
			synchronizer.Dispose();

			// Reset the version and synchronizer
			Version = Peek()?.ID ?? 0;
			synchronizer = new SynchronizingHandle(Version + 1);

			// Rebuild the typeList cache
			typeList = database.Select<ModelType>().ToList();

			// Remove this handler
			database.Connection.RollBack -= OnRollback;
		}

		/// <summary>
		/// Returns all new <see cref="Changes"/> objects with an ID larger than the given <paramref name="id"/>.
		/// </summary>
		/// <param name="id">The version for which to collect all newer changes.</param>
		/// <param name="limit">An optional limit to how many changes are returned.
		/// Values less than 0 indicate no limit.</param>
		public IEnumerable<Changes> GetNewChanges(long id, long limit = -1)
		{
			foreach (Changes changes in database.Select<Changes>("1 LIMIT @id,@limit", new { id, limit }))
			{
				changes.CollectionType = typeList.First(x => x.ID == changes.ModelTypeID);
				yield return changes;
			}
		}

		public void Dispose()
		{
			database.Dispose();
			database.Dispose();
			synchronizer.Dispose();
		}
	}

	/// <summary>
	/// A class that provides ID-based synchronization using blocking function calls.
	/// </summary>
	internal class SynchronizingHandle : IDisposable
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
