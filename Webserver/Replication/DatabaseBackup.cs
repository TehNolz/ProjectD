using Database.SQLite;

using System;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using static Webserver.Config.DatabaseConfig;

namespace Webserver.Replication
{
	/// <summary>
	/// A <see cref="SQLiteAdapter"/> subclass that handles the database backup process for
	/// <see cref="ServerDatabase"/> instances.
	/// <para/>
	/// Instances of this class must be disposed in order to complete the backup process.
	/// </summary>
	public sealed class DatabaseBackup : SQLiteAdapter, IDisposable
	{
		/// <summary>
		/// The <see cref="DateTime"/> format string used for the backup files.
		/// </summary>
		public const string TimeStampFormat = "yyyy-MM-dd_HH-mm-ss";

		/// <summary>
		/// Initializes a new instance of <see cref="DatabaseBackup"/> and creates a new database backup file
		/// from the given <paramref name="source"/> database.
		/// <para/>
		/// The new backup no longer contains the <see cref="Changes"/> and <see cref="ModelType"/> tables.
		/// </summary>
		/// <param name="source">The database to back up.</param>
		public DatabaseBackup(ServerDatabase source) : base(GetBackupFileName(source.Connection.FileName))
		{
			// Create the backup
			source.Connection.BackupDatabase(Connection, Connection.Database, source.Connection.Database, -1, BackupCallback, -1);
			Utils.ClearProgressBar();

			// Drop the __changes and __types tables
			DropTableIfExists<Changes>();
			DropTableIfExists<ModelType>();

			// Increment the user_version pragma
			UserVersion = source.UserVersion + 1;
		}

		/// <summary>
		/// Displays a progressbar while a database backup is in progress.
		/// </summary>
		private bool BackupCallback(
			SQLiteConnection source,
			string sourceName,
			SQLiteConnection destination,
			string destinationName,
			int pages,
			int remainingPages,
			int totalPages,
			bool retry)
		{
			Utils.ProgressBar(pages - remainingPages, pages);
			return true;
		}

		/// <summary>
		/// Returns a new backup file path for the given file path.
		/// </summary>
		/// <param name="databaseFile">The database file to create a backup file for.</param>
		private static string GetBackupFileName(string databaseFile)
		{
			// Create the backup dir if it doesn't exist yet
			if (!Directory.Exists(BackupDir))
				Directory.CreateDirectory(BackupDir);

			return Path.Combine(
				BackupDir,
				$"{Path.GetFileNameWithoutExtension(databaseFile)}_{DateTime.UtcNow.ToString(TimeStampFormat)}.db"
			);
		}

		#region IDisposable Support
		private bool isDisposed = false; // To detect redundant calls
		private void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				if (disposing)
				{
					if (CompressBackups)
					{
						// Shrink the database file
						Program.Log.Info($"Compressing backup...");
						using (var command = new SQLiteCommand(Connection) { CommandText = "VACUUM" })
							command.ExecuteNonQuery();

						// Dispose the database connection
						string backup = Connection.FileName;
						base.Dispose();

						// Move the backup to a zip archive
						string archiveName = Path.ChangeExtension(backup, ".zip");
						ZipArchive archive = ZipFile.Open(archiveName, ZipArchiveMode.Create);
						archive.CreateEntryFromFile(backup, Path.GetFileName(backup));
						archive.Dispose();

						// Delete the backup
						File.Delete(backup);
						Program.Log.Info($"Successfully created backup '{Path.GetFileName(archiveName)}'");
					}
					else
					{
						string fileName = Connection.FileName;
						base.Dispose();
						Program.Log.Info($"Successfully created backup '{fileName}'");
					}

				}
				isDisposed = true;
			}
		}

		/// <summary>
		/// Finishes the database backup process and disposes all managed resources.
		/// </summary>
		// This code added to correctly implement the disposable pattern.
		public override void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
		}
		#endregion
	}
}
