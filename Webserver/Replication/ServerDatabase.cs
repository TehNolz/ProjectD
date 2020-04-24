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
				// Create the Changes object
				changes = new Changes()
				{
					Type = ChangeType.INSERT,
					CollectionType = typeof(T),
					Collection = JArray.FromObject(items)
				};

				// Broadcast the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
				{
					Log.Debug("Sending changes to master");
					// Get a message containing an updated collection
					changes.Synchronize();
					Log.Debug($"Got updated changes {changes.ID} from master");

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
					Log.Debug($"Sending updated changes {changes.ID} to slaves");
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

			//var totalTimer = new Stopwatch();
			//totalTimer.Start();

			// Get the amount of new changes to request
			long updateCount = new Message(MessageType.DbSync, null).SendAndWait(Balancer.MasterServer).Data.Version - changelog.Version;

			//totalTimer.Stop();
			//var ping = totalTimer.ElapsedMilliseconds;
			//Log.Debug($"Applying {updateCount} changes. Chunksize: {SynchronizeChunkSize}, Ping: {totalTimer.Format()}");

			lock (this)
			{
				SQLiteTransaction databaseTransaction = changelog.database.Connection.BeginTransaction();
				SQLiteTransaction changelogTransaction = changelog.changeLog.Connection.BeginTransaction();

				int chunkSize = SynchronizeChunkSize; // Copy the value to keep things thread-safe
				long interval = Math.Max(1, (long)(updateCount / (Console.WindowWidth * 0.8))); // Interval for refreshing the progress bar

				//var chunkTimer = new Stopwatch();
				//var IOTimer = new Stopwatch();
				//totalTimer.Restart();

				//var chunkTimes = new List<long>();
				//var IOTimes = new List<long>();

				for (long l = 0; l < updateCount;)
				{
					//IOTimer.Restart();

					// Request another chunk of updates
					JArray updates = new Message(
						MessageType.DbSync,
						new { changelog.Version, Amount = Math.Min(updateCount - l, chunkSize) }
					).SendAndWait(Balancer.MasterServer).Data;
					
					//IOTimes.Add(IOTimer.ElapsedMilliseconds);

					//chunkTimer.Restart();

					// Apply each update, increment l and occasionally update the progressbar
					foreach (Changes update in updates.Select(x => (Changes)x))
					{
						changelog.Push(update);
						if (l++ % interval == 0) Utils.ProgressBar(l, updateCount);
					}

					//chunkTimes.Add(chunkTimer.ElapsedMilliseconds);
					Utils.ProgressBar(l, updateCount);
				}
				//totalTimer.Stop();

				//StreamWriter writer = File.AppendText("stats.csv");
				//writer.WriteLine(string.Join(',',
				//	chunkSize,
				//	totalTimer.ElapsedMilliseconds,
				//	chunkTimes.Max(),
				//	chunkTimes.Average(),
				//	chunkTimes.Min(),
				//	IOTimes.Max(),
				//	IOTimes.Average(),
				//	IOTimes.Min(),
				//	ping
				//));
				//writer.Dispose();
				
				databaseTransaction.Commit();
				changelogTransaction.Commit();

				changelog.Dispose();
				changelog = new ChangeLog(this);

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
