using Database.SQLite;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

using Webserver.LoadBalancer;

using static Webserver.Config.DatabaseConfig;

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
		/// <inheritdoc cref="ChangeLog.ChangelogVersion"/>
		public long ChangelogVersion => changelog.ChangelogVersion;
		/// <inheritdoc cref="ChangeLog.TypeList"/>
		public ReadOnlyCollection<ModelType> TypeList => changelog.TypeList;

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
		/// The <see cref="Thread"/> responsible for periodically backing up the database.
		/// </summary>
		private Thread backupThread;
		private bool backupThread_stop = false;

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

			// Create a new database backup thread
			backupThread = new Thread(BackupThread_Run) { Name = "Database Backup" };
			backupThread.Start();
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
			backupThread = parent.backupThread;
		}

		/// <summary>
		/// Main loop for <see cref="backupThread"/>.
		/// </summary>
		/// <param name="db">A <see cref="ServerDatabase"/> that will be cloned.
		/// The cloned instance will not be a file lock owner.</param>
		private void BackupThread_Run()
		{
			try
			{
				DateTime lastBackup = File.GetCreationTimeUtc(Connection.FileName);
				if (Directory.Exists(BackupDir))
				{
					// Get the datetime from the most recent .db or .zip file (if any)
					FileInfo file = DatabaseBackup.LastBackup;
					if (!(file is null))
						lastBackup = file.LastWriteTimeUtc;
				}

				while (!backupThread_stop)
				{
					// Calculate the remaining time until a new backup must be made
					TimeSpan remainingTime = BackupPeriod - (DateTime.UtcNow - lastBackup);

					// Only call Thread.Sleep if the remaining time is larger than 0
					if (remainingTime.Ticks > 0)
						Thread.Sleep(remainingTime);

					// Backup this connection
					lock (changelog)
					{
						// Clone the connection to prevent changes broadcasts
						using ServerDatabase database = NewConnection();
						database.BroadcastChanges = false;

						new DatabaseBackup(database).Dispose();

						// Clear the changelog
						base.Delete<Changes>("1", null);
					}
					lastBackup = DateTime.UtcNow;
				}
			}
			catch (ThreadInterruptedException) { } // Thrown only when the thread is sleeping
		}

		#region SQLiteAdapter overrides
		public override long Insert<T>(IList<T> items)
		{
			Changes changes = null;
			try
			{
				changes = new Changes(items as IList<object>, GetModelType<T>())
				{
					Type = ChangeType.INSERT
				};

				// Synchronize the changes if this server is not a master
				if (BroadcastChanges && !Balancer.IsMaster)
				{
					// Sync to also update the changes object
					changes.Synchronize();

					// Swap the elements in the collections
					T[] newItems = changes.ExpandCollection().Select(x => x.ToObject<T>()).ToArray();
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
						changes.SetCollection(items as IList<object>);

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
				changes = new Changes(condition, param)
				{
					Type = ChangeType.DELETE | ChangeType.WithCondition,
					CollectionType = GetModelType<T>()
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
				changes = new Changes(items as IList<object>, GetModelType<T>())
				{
					Type = ChangeType.DELETE
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
				changes = new Changes(items as IList<object>, GetModelType<T>())
				{
					Type = ChangeType.UPDATE
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
		#endregion

		/// <summary>
		/// Syncronizes the entire database with the master server.
		/// </summary>
		public void Synchronize()
		{
			if (Balancer.IsMaster)
				throw new InvalidOperationException();

			using var progress = new ProgressBar() { MaxProgress = 3 };

			progress.Draw(1);
			SynchronizeBackup();
			progress.Draw(2);
			SynchronizeChanges();
			progress.Draw(3);
		}
		/// <summary>
		/// Retrieves a database backup file from master if nescessary.
		/// </summary>
		private void SynchronizeBackup()
		{
			long backupSize;
			string backupName;
			string backupSizeStr;
			long transferChunkSize = Utils.ParseDataSize(BackupTransferChunkSize);

			// Get the backup filename and size
			{
				dynamic data = new ServerMessage(MessageType.DbSyncBackupStart, ChangelogVersion).SendAndWait(Balancer.MasterServer).Data;

				if (data is JObject json && json.ContainsKey("error"))
					throw new OperationCanceledException($"Master rejected synchronization request: {json["error"]}");
				if (data == null)
					return;

				backupName = data.Name;
				backupSize = data.Length;
				backupSizeStr = string.Format(new DataFormatter(), "{0:B1}", backupSize);
			}

			Program.Log.Config($"Downloading '{backupName}' ({backupSizeStr}) from the master server...");
			using var progress = new ProgressBar() { MaxProgress = backupSize };

			// Create a temp file
			FileStream temp = File.OpenWrite(Path.GetTempFileName());

			// Download the backup from master
			while (temp.Position < backupSize)
			{
				// Get a chunk of data from the parent's backup file
				byte[] data = new ServerMessage(
					MessageType.DbSyncBackup,
					new
					{
						FileName = backupName,
						Offset = temp.Position,
						Amount = transferChunkSize
					}
				).SendAndWait(Balancer.MasterServer).Data;
				temp.Write(data);

				// Draw progressbar
				string sizeStr = string.Format(new DataFormatter() { SpaceBeforeUnit = false }, "{0:B1}", temp.Position);
				progress.Prefix = $"Downloading [{sizeStr}/{backupSizeStr}]";
				progress.Draw(temp.Position);
			}
			progress.Draw(backupSize);
			temp.Dispose();

			// Replace the current database file
			string databaseFile = Connection.FileName;
			
			// Close all connections
			Connection.Close();
			changelog.Close();

			// Replace the database with the temp file
			if (Path.GetExtension(backupName).ToLower() == ".zip")
			{
				ZipArchive archive = ZipFile.OpenRead(temp.Name);
				archive.Entries.First().ExtractToFile(databaseFile, true);
				archive.Dispose();
				File.Delete(temp.Name);
			}
			else
			{
				File.Delete(databaseFile);
				File.Move(temp.Name, databaseFile);
			}

			// Re-open all connections
			Connection.Open();
			changelog.Open();
		}
		/// <summary>
		/// Retrieves all new changes from the master server and applies them.
		/// </summary>
		private void SynchronizeChanges()
		{
			long updateCount;
			Dictionary<int?, ModelType> types;

			// Get the amount of new changes and a typelist from the master
			{
				dynamic data = new ServerMessage(MessageType.DbSyncStart, null).SendAndWait(Balancer.MasterServer).Data;
				updateCount = data.Version - changelog.ChangelogVersion;
				types = ((JArray)data.Types).Select(x => x.ToObject<ModelType>()).ToDictionary(x => x.ID);
			}

			if (updateCount == 0)
				return;

			Program.Log.Config($"Retrieving {updateCount} change{(updateCount == 1 ? "" : "s")} from master...");
			using var progress = new ProgressBar()
			{
				Prefix = $"Changes [{{0,-{updateCount.ToString().Length}}}/{{2}}]",
				MaxProgress = updateCount
			};

			lock (changelog)
			{
				// Create transaction to commit the changes only at the end (increases speed)
				SQLiteTransaction transaction = changelog.BeginTransaction();
				try
				{
					long interval = Math.Max(1, updateCount / progress.Size); // Interval for refreshing the progress bar

					for (long l = 0; l < updateCount;)
					{
						// Request another chunk of updates
						IEnumerable<Changes> updates = (new ServerMessage(MessageType.DbSync, new
						{
							Version = changelog.ChangelogVersion,
							Amount = (int)Math.Min(updateCount - l, SynchronizeChunkSize)
						}).SendAndWait(Balancer.MasterServer).Data as JArray).Select(x => (Changes)x);

						// Apply all changes from the last request
						foreach (Changes update in updates)
						{
							update.CollectionType = new ModelType() { FullName = types[update.ModelTypeID].FullName };
							changelog.Push(update);

							// Increment l and draw the progressbar if l has reached the interval
							if (l++ % interval == 0)
								progress.Draw(l);
						}

						progress.Draw(l);
					}
					progress.Draw(updateCount);
					transaction.Commit();
				}
				catch (Exception)
				{
					transaction.Rollback();
					throw;
				}
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
		/// Returns an existing <see cref="ModelType"/> instance from the <see cref="TypeList"/>
		/// or creates a new one.
		/// </summary>
		/// <typeparam name="T">The type to get a <see cref="ModelType"/> for.</typeparam>
		private ModelType GetModelType<T>() => TypeList.FirstOrDefault(x => x == typeof(T)) ?? typeof(T);

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
			if (!(fileLock is null))
			{
				fileLock.Release(this);
				if (!fileLock.Locked)
				{
					backupThread_stop = true;
					backupThread.Interrupt();
					backupThread.Join();
					backupThread = null;

					changelog.Dispose();
				}
			}

			fileLock = null;
			base.Dispose();
		}
	}
}