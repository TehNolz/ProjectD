using Database.SQLite;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Webserver.LoadBalancer;

using static Webserver.Program;

namespace Webserver.Replication
{
	/// <summary>
	/// Represents a database that shares it's changes across other servers.
	/// <para/>
	/// Only one instance of <see cref="ServerDatabase"/> can be created per datasource
	/// due to the database locks.
	/// </summary>
	public sealed class ServerDatabase : SQLiteAdapter, IDisposable
	{
		/// <inheritdoc cref="ChangeLog.Version"/>
		public long Version => changelog.Version;

		/// <summary>
		/// Gets or sets the maximum amount of changes that are requested each time
		/// during the synchronization process.
		/// </summary>
		/// <seealso cref="Synchronize"/>
		/// <remarks>
		/// Higher chunk sizes may keep the master server too busy with sending one message,
		/// whereas lower chunk sizes may lead to excessive IO time.
		/// </remarks>
		public int SynchronizeChunkSize { get; set; } = 800;
		/// <summary>
		/// Gets or sets whether this <see cref="ServerDatabase"/> broadcasts it's changes
		/// to other servers.
		/// </summary>
		public bool BroadcastChanges { get; set; }

		/// <summary>
		/// FileLock object used to keep ownership over a specific database.
		/// </summary>
		private FileLock fileLock;
		private ChangeLog changelog;

		/// <summary>
		/// Initializes a new instance of <see cref="ServerDatabase"/> with the specified
		/// file lock and datasource.
		/// </summary>
		/// <param name="fileLock">The <see cref="FileLock"/> object to hold.</param>
		/// <param name="datasource">The path to the database file to use.</param>
		private ServerDatabase(FileLock fileLock, string datasource) : base(datasource)
		{
			Connection.ParseViaFramework = true;

			this.fileLock = fileLock;
			fileLock.Acquire(this);
			changelog = new ChangeLog(this);
		}
		/// <summary>
		/// Initializes a new instance of <see cref="ServerDatabase"/> by inheriting
		/// from the specified <see cref="ServerDatabase"/>.
		/// </summary>
		/// <param name="parent">The parent database to inherit from.</param>
		private ServerDatabase(ServerDatabase parent) : base(parent.Connection.FileName)
		{
			Connection.ParseViaFramework = true;

			fileLock = parent.fileLock;
			fileLock.Acquire(this);
			changelog = parent.changelog;
		}

		public override long Insert<T>(IList<T> items)
		{
			Changes changes = null;
			try
			{
				changes = new Changes()
				{
					Type = ChangeType.INSERT,
					CollectionType = typeof(T),
					Collection = JArray.FromObject(items)
				};

				// Synchronize the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
				{
					// Sync to also update the changes object
					changes.Synchronize();

					// Swap the elements in the collections
					T[] newItems = changes.Collection.Select(x => x.ToObject<T>()).ToArray();
					for (int i = 0; i < items.Count; ++i)
						items[i] = newItems[i];
				}

				// Lock this in order to syncronize the insert with the change push
				long @out;
				lock (this)
				{
					// Slaves that broadcast their changes must first push their changes
					// in order to synchronize the database modifications.
					if (BroadcastChanges && !Balancer.IsMaster)
					{
						// Store the new changes
						changelog.Push(changes, false);

						@out = base.Insert(items);
					}
					// Masters insert first to update the id's and then push their changes.
					else
					{
						@out = base.Insert(items);

						// Update the collection in the changes
						changes.Collection = JArray.FromObject(items);

						// Store the new changes
						changelog.Push(changes, false);
					}
				}
				return @out;
			}
			catch (Exception)
			{
				// Set changes to null to skip broadcasting to the other servers
				changes = null;
				throw;
			}
			finally
			{
				if (BroadcastChanges && changes != null && Balancer.IsMaster)
				{
					// Send the message to all other servers
					changes.Broadcast();
				}
			}
		}

		public override int Delete<T>(string condition, [AllowNull] object param)
		{
			Changes changes = null;
			try
			{
				changes = new Changes()
				{
					Type = ChangeType.DELETE | ChangeType.WithCondition,
					CollectionType = typeof(T),
					Data = condition
				};

				// Synchronize the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
					changes.Synchronize();

				// Lock this in order to syncronize the insert with the change push
				int @out;
				lock (this)
				{
					// Slaves that broadcast their changes must first push their changes
					// in order to synchronize the database modifications.
					if (BroadcastChanges && !Balancer.IsMaster)
					{
						changelog.Push(changes, false);
						@out = base.Delete<T>(condition, param);
					}
					// Masters insert first to confirm that the query works
					else
					{
						@out = base.Delete<T>(condition, param);
						changelog.Push(changes, false);
					}
				}
				return @out;
			}
			catch (Exception)
			{
				// Set changes to null to skip broadcasting to the other servers
				changes = null;
				throw;
			}
			finally
			{
				if (BroadcastChanges && changes != null && Balancer.IsMaster)
				{
					// Send the message to all other servers
					changes.Broadcast();
				}
			}
		}
		public override int Delete<T>(IList<T> items)
		{
			Changes changes = null;
			try
			{
				changes = new Changes()
				{
					Type = ChangeType.DELETE,
					CollectionType = typeof(T),
					Collection = JArray.FromObject(items)
				};

				// Synchronize the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
					changes.Synchronize();

				// Lock this in order to syncronize the insert with the change push
				int @out;
				lock (this)
				{
					// Slaves that broadcast their changes must first push their changes
					// in order to synchronize the database modifications.
					if (BroadcastChanges && !Balancer.IsMaster)
					{
						changelog.Push(changes, false);
						@out = base.Delete(items);
					}
					// Masters insert first to confirm that the query works
					else
					{
						@out = base.Delete(items);
						changelog.Push(changes, false);
					}
				}
				return @out;
			}
			catch (Exception)
			{
				// Set changes to null to skip broadcasting to the other servers
				changes = null;
				throw;
			}
			finally
			{
				if (BroadcastChanges && changes != null && Balancer.IsMaster)
				{
					// Send the message to all other servers
					changes.Broadcast();
				}
			}
		}

		public override int Update<T>(IList<T> items)
		{
			Changes changes = null;
			try
			{
				changes = new Changes()
				{
					Type = ChangeType.UPDATE,
					CollectionType = typeof(T),
					Collection = JArray.FromObject(items)
				};

				// Synchronize the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
					changes.Synchronize();

				// Lock this in order to syncronize the insert with the change push
				int @out;
				lock (this)
				{
					// Slaves that broadcast their changes must first push their changes
					// in order to synchronize the database modifications.
					if (BroadcastChanges && !Balancer.IsMaster)
					{
						changelog.Push(changes, false);
						@out = base.Update(items);
					}
					// Masters insert first to confirm that the query works
					else
					{
						@out = base.Update(items);
						changelog.Push(changes, false);
					}
				}
				return @out;
			}
			catch (Exception)
			{
				// Set changes to null to skip broadcasting to the other servers
				changes = null;
				throw;
			}
			finally
			{
				if (BroadcastChanges && changes != null && Balancer.IsMaster)
				{
					// Send the message to all other servers
					changes.Broadcast();
				}
			}
		}

		/// <summary>
		/// Retrieves all new changes from the master server and applies them.
		/// </summary>
		public void Synchronize()
		{
			if (Balancer.IsMaster)
				throw new InvalidOperationException();

			// Get the amount of new changes to request
			long updateCount = new Message(MessageType.DbSync, null).SendAndWait(Balancer.MasterServer).Data.Version - changelog.Version;
			
			lock (this)
			{
				SQLiteTransaction databaseTransaction = changelog.database.Connection.BeginTransaction();
				SQLiteTransaction changelogTransaction = changelog.changeLog.Connection.BeginTransaction();

				int chunkSize = SynchronizeChunkSize; // Copy the value to keep things thread-safe
				long interval = Math.Max(1, (long)(updateCount / (Console.WindowWidth * 0.8))); // Interval for refreshing the progress bar

				for (long l = 0; l < updateCount;)
				{
					// Request another chunk of updates
					JArray updates = new Message(
						MessageType.DbSync,
						new { changelog.Version, Amount = Math.Min(updateCount - l, chunkSize) }
					).SendAndWait(Balancer.MasterServer).Data;
					
					// Apply each update, increment l and occasionally update the progressbar
					foreach (Changes update in updates.Select(x => (Changes)x))
					{
						changelog.Push(update);
						if (l++ % interval == 0) Utils.ProgressBar(l, updateCount);
					}

					Utils.ProgressBar(l, updateCount);
				}
				
				databaseTransaction.Commit();
				changelogTransaction.Commit();

				Utils.ClearProgressBar();
			}
		}

		/// <summary>
		/// Pushes the given changes onto this database's changelog and applies the 
		/// changes specified in the <paramref name="changes"/> object.
		/// </summary>
		/// <param name="changes">The changes to apply to this database.</param>
		public void Apply(Changes changes) => changelog.Push(changes);

		/// <inheritdoc cref="ChangeLog.GetNewChanges(long)"/>
		public IEnumerable<Changes> GetNewChanges(long id, long limit = -1) => changelog.GetNewChanges(id, limit);

		/// <summary>
		/// Returns a new instance of <see cref="ServerDatabase"/> with the same datasource as
		/// this instance by inheriting the database lock.
		/// <para/>
		/// The created <see cref="ServerDatabase"/> has <see cref="BroadcastChanges"/> set to
		/// <see langword="true"/> by default.
		/// </summary>
		public ServerDatabase NewConnection() => new ServerDatabase(this) { BroadcastChanges = true };

		/// <summary>
		/// Returns a new <see cref="ServerDatabase"/> connection for the given datasource.
		/// <para/>
		/// If the given datasource is locked by another process, a number prefix will be added until an
		/// unoccupied datasource was found.
		/// <para/>
		/// The created <see cref="ServerDatabase"/> has <see cref="BroadcastChanges"/> set to
		/// <see langword="false"/> by default.
		/// </summary>
		/// <param name="datasource">The path to the database file to create a new <see cref="ServerDatabase"/> for.</param>
		public static ServerDatabase CreateConnection(string datasource)
		{
			string oldDatasource = new string(datasource);

			// Try to acquire a filelock for the given datasource
			FileLock fileLock;
			object lockHolder = new object();
			for (int i = 1; true; i++)
			{
				// Create and acquire a FileLock
				try
				{
					fileLock = new FileLock(datasource + ".lock", lockHolder);
					fileLock.Release(lockHolder);
					break;
				}
				catch (Exception) { }

				// Create new datasource path if the lock could not be acquired
				datasource = Path.Combine(Path.GetDirectoryName(datasource), $"{Path.GetFileNameWithoutExtension(oldDatasource)}{i}{Path.GetExtension(datasource)}");
			}

			return new ServerDatabase(fileLock, datasource);
		}

		public override void Dispose()
		{
			fileLock?.Release(this);
			fileLock = null;
			changelog.Dispose();
			base.Dispose();
		}
	}
}
