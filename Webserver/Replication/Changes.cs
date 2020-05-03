using Database.SQLite.Modeling;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Webserver.LoadBalancer;

namespace Webserver.Replication
{
	/// <summary>
	/// Represents all changes made to a <see cref="ServerDatabase"/>. Instances of this class can
	/// be stored in a <see cref="ChangeLog"/>.
	/// <para/>
	/// This class uses JSON serialization to store the data related to the query.
	/// </summary>
	public class Changes
	{
		[Primary]
		public long? ID { get; set; }
		public ChangeType? Type { get; set; }
		public string CollectionTypeName
		{
			get => _collectionTypeName;
			set
			{
				_collectionType = null;
				_collectionTypeName = value;
			}
		}
		public string CollectionJSON
		{
			get => _collectionJSON;
			set
			{
				_collection = null;
				_collectionJSON = value;
			}
		}
		public string Condition { get; set; }

		private string _collectionTypeName;
		private Type _collectionType;
		private string _collectionJSON;
		private JArray _collection;

		public virtual Type CollectionType
		{
			get
			{
				if (_collectionType is null)
					_collectionType = Assembly.GetExecutingAssembly().GetType(CollectionTypeName);
				return _collectionType;
			}
			set
			{
				_collectionType = value;
				_collectionTypeName = value.FullName;
			}
		}
		public virtual JArray Collection
		{
			get
			{
				if (_collection is null)
					_collection = JArray.Parse(CollectionJSON);
				return _collection;
			}
			set
			{
				_collection = value;
				CollectionJSON = value?.ToString(Formatting.None);
			}
		}
		public virtual ServerMessage Source { get; }

		/// <summary>
		/// Initializes a new instance of <see cref="Changes"/>.
		/// </summary>
		public Changes() { }
		/// <summary>
		/// Initializes a new instance of <see cref="Changes"/> by deserializing the given
		/// <paramref name="message"/> object.
		/// </summary>
		/// <param name="message">The message to deserialize.</param>
		public Changes(ServerMessage message) : this((JObject)message.Data)
		{
			Source = message;
		}
		/// <summary>
		/// Deserializes the given <paramref name="data"/> into a new instance of <see cref="Changes"/>.
		/// </summary>
		/// <param name="data">The data to deserialize.</param>
		protected Changes(dynamic data)
		{
			ID = (long?)data.ID;
			Type = Enum.Parse<ChangeType>((string)data.Type);
			CollectionTypeName = (string)data.ItemType;
			Collection = (JArray)data.Items;
		}

		/// <summary>
		/// Sends the data of this <see cref="Changes"/> instance to all other servers.
		/// <para/>
		/// If this <see cref="Changes"/> instance was serialized from a <see cref="Message"/>
		/// object, a reply will be sent to <see cref="Source"/>.
		/// </summary>
		public void Broadcast()
		{
			if (Source is null)
			{
				Broadcast(ServerProfile.KnownServers.Values
						.Where(x => x is ServerConnection)
						.Cast<ServerConnection>()
				);
			}
			else
			{
				Source.Reply((JObject)this);
				Broadcast(ServerProfile.KnownServers.Values
						.Where(x => x is ServerConnection && x != Source.Connection)
						.Cast<ServerConnection>()
				);
			}
		}
		private void Broadcast(IEnumerable<ServerConnection> servers) => ServerConnection.Send(servers, new ServerMessage(MessageType.DbChange, (JObject)this));

		/// <summary>
		/// Synchronizes these changes with the master server.
		/// <para/>
		/// Does nothing if this server is the master.
		/// </summary>
		/// <param name="changes">The <see cref="Changes"/> to synchronize with the master server.</param>
		public void Synchronize()
		{
			if (!Balancer.IsMaster)
			{
				var newChanges = new Changes(new ServerMessage(MessageType.DbChange, (JObject)this).SendAndWait(Balancer.MasterServer));
				CollectionJSON = newChanges.CollectionJSON;
				ID = newChanges.ID;
			}
		}

		public override string ToString() => $"{GetType().Name}<{ID}>";

		public static explicit operator JObject(Changes changes) => new JObject() {
				{ "ID", changes.ID },
				{ "Type", changes.Type.ToString() },
				{ "ItemType", changes.CollectionTypeName },
				{ "Items", changes.Collection },
			};
		public static explicit operator Changes(JObject jObject) => new Changes(jObject);
	}

	public enum ChangeType
	{
		INSERT,
		UPDATE,
		DELETE
	}
}