using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Webserver.Replication
{
	/// <summary>
	/// A file locking wrapper that can have multiple lock owners. The lock disengages
	/// whenever the owner count reaches 0.
	/// <para/>
	/// This class is thread safe.
	/// </summary>
	internal sealed class FileLock : IDisposable
	{
		/// <summary>
		/// Gets the path of the file used by this <see cref="FileLock"/>.
		/// </summary>
		public string Path { get; }
		/// <summary>
		/// Gets whether this <see cref="FileLock"/> is currently locked.
		/// </summary>
		public bool Locked => file != null;

		private readonly List<object> owners = new List<object>();
		/// <summary>
		/// Stream object used for locking the file. Locking is done using <see cref="FileShare.None"/>.
		/// </summary>
		private FileStream file;

		/// <summary>
		/// Initializes a new unlocked instance of <see cref="FileLock"/>.
		/// </summary>
		/// <param name="path">The path to the file lock. This </param>
		public FileLock(string path)
		{
			Path = path;

			// Subscribe to the processExit event for file cleanup
			AppDomain.CurrentDomain.ProcessExit += (sender, args) => this?.Dispose();
			AppDomain.CurrentDomain.UnhandledException += (sender, args) => this?.Dispose();
		}
		/// <summary>
		/// Initializes a new locked instance of <see cref="FileLock"/> with the given owner object.
		/// </summary>
		/// <param name="path">The path to the file to use as a lock.</param>
		/// <param name="owners">The objects to make owners of this lock.</param>
		/// <exception cref="ArgumentException">Duplicate object in <paramref name="owners"/>.</exception>
		/// <exception cref="ArgumentNullException">An element in <paramref name="owners"/> is null.</exception>
		/// <exception cref="IOException">The lock file was already acquired by another process.</exception>
		public FileLock(string path, params object[] owners) : this(path)
		{
			// Check for duplicates by grouping instances by reference
			if (owners.GroupBy(x => x).Any(x => x.Count() > 1))
				throw new ArgumentException("Array contains duplicate entries.", nameof(owners));

			foreach (object obj in owners)
				Acquire(obj);
		}

		/// <summary>
		/// Adds the given caller to the collection of lock owners.
		/// <para/>
		/// If this <see cref="FileLock"/> is currently empty, this will engage the lock.
		/// </summary>
		/// <param name="obj">The object to make an owner of this lock.</param>
		/// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
		/// <exception cref="InvalidOperationException"><paramref name="obj"/> is already a lock owner.</exception>
		/// <exception cref="IOException">The lock file was already acquired by another process.</exception>
		public void Acquire(object obj)
		{
			if (obj is null)
				throw new ArgumentNullException(nameof(obj));
			if (owners.Contains(obj))
				throw new InvalidOperationException($"'{nameof(obj)}' is already an owner of this lock.");

			// Protect read-write section
			lock (owners)
			{
				// Engage the lock if the owner list was empty
				if (!owners.Any())
				{
					// Open a new file stream with no sharing options
					file = File.Open(Path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
					File.SetAttributes(Path, FileAttributes.Hidden);
				}

				owners.Add(obj);
			}
		}

		/// <summary>
		/// Removes the given object from the collection of lock owners.
		/// <para/>
		/// If <paramref name="obj"/> is the last lock owner, this will disengage the lock.
		/// </summary>
		/// <param name="obj">The object to remove as owner of this lock.</param>
		/// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
		/// <exception cref="InvalidOperationException"><paramref name="obj"/> is not a lock owner.</exception>
		public void Release(object obj)
		{
			if (obj is null)
				throw new ArgumentNullException(nameof(obj));
			if (!owners.Contains(obj))
				throw new InvalidOperationException($"'{nameof(obj)}' is not an owner of this lock.");

			// Protect read-write section
			lock (owners)
			{
				// Disengage the lock if the obj was the last owner
				if (owners.Count == 1)
				{
					file.Dispose();
					file = null;
				}

				owners.Remove(obj);
			}
		}

		#region IDisposable Support
		private bool isDisposed = false; // To detect redundant calls

		void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				// Dispose managed resources
				if (disposing)
				{
					file?.Dispose();
				}

				// Dispose unmanaged resources
				try
				{
					File.Delete(Path);
				}
				catch (IOException) { } // Ignore exception

				isDisposed = true;
			}
		}

		~FileLock()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(false);
		}

		// This code added to correctly implement the disposable pattern.
		/// <summary>
		/// Releases and disposes of the lock file used by this <see cref="FileLock"/>.
		/// <para/>
		/// This shouldn't normally be called if this <see cref="FileLock"/> is still locked.
		/// </summary>
		public void Dispose()
		{
			// Do not change this code. Put cleanup code in Dispose(bool disposing) above.
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
