using System.Collections.Generic;
using System.Linq;
using Webserver.LoadBalancer;
using System.Security.Cryptography;
using Database.SQLite;
using System.IO;
using Database.SQLite.Modeling;
using Newtonsoft.Json.Linq;
using System;
using System.Reflection;
using Newtonsoft.Json;

namespace Webserver.Replication
{
	public class Changes
	{
		[Primary]
		public long? Id { get; set; }
		public ChangeType Type { get; set; }
		public string CollectionTypeName
		{
			get => _collectionTypeName;
			set
			{
				_collectionType = null;
				_collectionTypeName = value;
			}
		}
		public string CollectionJSON { get; set; }
		public string Condition { get; set; }

		private string _collectionTypeName;
		private Type _collectionType;

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
			get => JArray.Parse(CollectionJSON);
			set => CollectionJSON = value.ToString(Formatting.None);
		}
		public virtual Message Source { get; }

		public Changes() { }

		public Changes(Message message)
		{
			Source = message;
			Id = message.Data.Id;
			Type = Enum.Parse<ChangeType>(message.Data.Type);
			CollectionTypeName = message.Data.ItemType;
			CollectionJSON = message.Data.Items;
		}

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
		private void Broadcast(IEnumerable<ServerConnection> servers)
			=> ServerConnection.Send(servers, new Message(MessageType.DbChange, (JObject)this));

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
				var newChanges = new Changes(new Message(MessageType.DbChange, (JObject)this).SendAndWait(Balancer.MasterServer));
				CollectionJSON = newChanges.CollectionJSON;
				Id = newChanges.Id;
			}
		}

		public static explicit operator JObject(Changes changes) => new JObject() {
				{ "Id", changes.Id },
				{ "Type", changes.Type.ToString() },
				{ "ItemType", changes.CollectionTypeName },
				{ "Items", changes.CollectionJSON },
			};
	}

	public enum ChangeType
	{
		None = default,
		INSERT,
		UPDATE,
		DELETE
	}
}
