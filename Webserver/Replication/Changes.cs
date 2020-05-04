using Database.SQLite.Modeling;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
	[Table("__changes")]
	public class Changes
	{
		[AutoIncrement]
		public long? ID { get; set; }
		public ChangeType? Type { get; set; }
		[ForeignKey(typeof(ModelType))]
		[Database.SQLite.Modeling.NotNull]
		public int? ModelTypeID { get; set; }
		public string Data
		{
			get => _data;
			set
			{
				_collection = null;
				_data = value;
			}
		}

		private string _data;
		private JArray _collection;
		private ModelType _collectionType;

		public virtual ModelType CollectionType
		{
			get => _collectionType;
			set
			{
				ModelTypeID = value.ID;
				_collectionType = value;
			}
		}
		public virtual JArray Collection
		{
			get
			{
				if (_collection is null)
					_collection = JArray.Parse(Data);
				return _collection;
			}
			private set
			{
				_data = value?.ToString(Formatting.None);
				_collection = value;
			}
		}
		[JsonIgnore]
		public virtual ServerMessage Source { get; }

		/// <summary>
		/// Initializes a new instance of <see cref="Changes"/>.
		/// </summary>
		public Changes() { }
		/// <summary>
		/// Initializes a new instance of <see cref="Changes"/> with the specified collection
		/// of objects.
		/// <para/>
		/// </summary>
		/// <param name="collection">The list of objects to add to this <see cref="Changes"/> instance.</param>
		/// <param name="type">The type of the collection elements.</param>
		public Changes(IList<object> collection, ModelType type) => SetCollection(collection, type);
		/// <summary>
		/// Initializes a new instance of <see cref="Changes"/> with the specified condition
		/// string and optional parameter object.
		/// <para/>
		/// This also sets the <see cref="ChangeType.WithCondition"/> flag in <see cref="Type"/>.
		/// </summary>
		/// <param name="condition">The condition string used in the query.</param>
		/// <param name="param">An optional parameter object whose properties will be
		/// included in the command.</param>
		public Changes(string condition, [AllowNull] object param) => SetCondition(condition, param);
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
			// TODO Use JArray to decrease message size
			ID = (long?)data.ID;
			Type = Enum.Parse<ChangeType>((string)data.Type);
			Data = (string)data.Data;

			// Get the type as id or as fullname based on type
			JValue itemType = data.ItemType;
			if (itemType.Type == JTokenType.Integer)
				CollectionType = new ModelType() { ID = (int)data.ItemType };
			else
				CollectionType = new ModelType() { FullName = (string)data.ItemType };
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
				Data = newChanges.Data;
				ID = newChanges.ID;
			}
		}

		/// <summary>
		/// Changes the <see cref="Collection"/> <see cref="JArray"/> by combining the
		/// values from the nested arrays with the property names of the <see cref="CollectionType"/>.
		/// </summary>
		/// <returns>A new <see cref="JArray"/> with nested <see cref="JObject"/>s.</returns>
		public JArray ExpandCollection()
		{
			// Convert the JArrays into JObjects by adding the keys of the type's properties
			PropertyInfo[] props = Utils.GetProperties(CollectionType).ToArray();
			return new JArray(Collection.Select(x =>
			{
				var @out = new JObject();
				for (int i = 0; i < props.Length; i++)
					@out[props[i].Name] = x[i];
				return @out;
			}));
		}

		/// <summary>
		/// Converts the given <paramref name="collection"/> to a JArray and sets the
		/// <see cref="Collection"/> property.
		/// </summary>
		/// <param name="collection">The list of objects to add to this <see cref="Changes"/> instance.</param>
		/// <param name="type">The type of the collection elements.</param>
		public void SetCollection([AllowNull] IList<object> collection, ModelType type = null)
		{
			type ??= CollectionType;

			// Local function to parse only the properties of an object into a JObject
			static JObject parse(PropertyInfo[] props, object item)
			{
				var @out = new JObject();
				foreach (PropertyInfo prop in props)
					@out[prop.Name] = new JValue(prop.GetValue(item));
				return @out;
			}
			PropertyInfo[] props = Utils.GetProperties(type).ToArray();
			Collection = collection is null ? null : JArray.FromObject(collection.Select(x => parse(props, x).Values()));
			CollectionType = type;
		}

		/// <summary>
		/// Packs the given <paramref name="condition"/> and <paramref name="param"/>
		/// into a <see cref="JArray"/> and sets the <see cref="Collection"/> property.
		/// </summary>
		/// <para/>
		/// This also sets the <see cref="ChangeType.WithCondition"/> flag in <see cref="Type"/>.
		/// <param name="condition">The condition string used in the query.</param>
		/// <param name="param">An optional parameter object whose properties will be
		/// included in the command.</param>
		public void SetCondition(string condition, object param = null)
		{
			Collection = new JArray() { condition, JObject.FromObject(param) };
			Type |= ChangeType.WithCondition;
		}

		public override string ToString()
		{
			var typeText = Type switch
			{
				ChangeType.INSERT => "I",
				ChangeType.UPDATE => "U",
				ChangeType.DELETE => "D",
				ChangeType.DELETE | ChangeType.WithCondition => "D`",
				_ => "?"
			};
			return $"{GetType().Name}<{typeText}>[{ID ?? '?'}]";
		}

		public static explicit operator JObject(Changes changes) => new JObject() {
				{ "ID", changes.ID },
				{ "Type", (int)changes.Type },
				{ "ItemType", new JValue((object)changes.CollectionType.ID ?? changes.CollectionType.FullName) },
				{ "Data", changes.Data },
			};
		public static explicit operator Changes(JObject jObject) => new Changes(jObject);
	}

	/// <summary>
	/// Represents the different variants of <see cref="Changes"/> instances.
	/// </summary>
	/// <remarks>
	/// Flags <see cref="INSERT"/>, <see cref="UPDATE"/> and <see cref="DELETE"/> are
	/// mutually exclusive.
	/// </remarks>
	[Flags]
	public enum ChangeType
	{
		INSERT = 0b00000001,
		UPDATE = 0b00000011,
		DELETE = 0b00000111,
		WithCondition = 1 << 3
	}
}
