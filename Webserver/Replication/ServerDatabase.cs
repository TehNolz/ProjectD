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
		/// <summary>
		/// Gets or sets whether this <see cref="ServerDatabase"/> broadcasts it's changes
		/// to other servers.
		/// </summary>
		public bool BroadcastChanges { get; set; }

		/// <summary>
		/// FileLock object used to keep ownership over a specific database.
		/// </summary>
		private FileLock fileLock;

		private ChangeStack changeStack;

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
			changeStack = new ChangeStack(this);
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
			changeStack = parent.changeStack;
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
					Log.Debug("Sending batch to master");
					// Get a message containing an updated collection
					changes.Synchronize();
					Log.Debug("Got updated batch from master");

					Log.Debug(((JObject)changes).ToString());

					// Swap the elements in the collections
					T[] newItems = changes.Collection.Select(x => x.ToObject<T>()).ToArray();
					for (int i = 0; i < items.Count; ++i)
						items[i] = newItems[i];
				}

				// Insert the collection
				long @out;
				lock (this)
					@out = base.Insert(items);

				// Update changes collection if it wasn't synchronized with the master
				changes.Collection = JArray.FromObject(items);

				changeStack.Push(changes, false);
				return @out;
			}
			catch (Exception)
			{
				changes = null;
				throw;
			}
			finally
			{
				if (BroadcastChanges && changes != null && Balancer.IsMaster)
				{
					// changes?.Broadcast();
					Log.Debug("Sending updated batch to slaves");

					// Send the message to all other servers
					Log.Debug("Sending updated batch to the remaining slaves");
					changes.Broadcast();
				}
			}
		}

		public void Synchronize()
		{
			if (Balancer.IsMaster)
				throw new InvalidOperationException();

			JArray newChanges = new Message(MessageType.DbSync, new { Id = changeStack.Peek()?.Id ?? 0 }).SendAndWait(Balancer.MasterServer, 5000 ).Data;

			Log.Debug($"Applying {newChanges.Count} changes");

			lock (this)
			{
				SQLiteTransaction databaseTransaction = changeStack.database.Connection.BeginTransaction();
				SQLiteTransaction changelogTransaction = changeStack.changeLog.Connection.BeginTransaction();

				int i = 0;
				foreach (Changes changes in newChanges.Select(x => x.ToObject<Changes>()))
				{
					i++;
					changeStack.Push(changes);
					Utils.ProgressBar(i, newChanges.Count);
				}

				databaseTransaction.Commit();
				changelogTransaction.Commit();
				Utils.ClearProgressBar();
			}
		}

		public void Apply(Changes changes)
		{
			changeStack.Push(changes);
		}

		public IEnumerable<Changes> GetChanges(long id) => changeStack.Crawl(id);

		/// <summary>
		/// Returns a new instance of <see cref="ServerDatabase"/> with the same datasource as
		/// this instance by inheriting the database lock.
		/// <para/>
		/// The created <see cref="ServerDatabase"/> has <see cref="BroadcastChanges"/> set to
		/// <see langword="true"/> by default.
		/// </summary>
		public ServerDatabase NewConnection() => new ServerDatabase(this);

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
			changeStack.Dispose();
			base.Dispose();
		}
	}
}
