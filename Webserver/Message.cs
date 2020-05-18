using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Text;

using Webserver.LoadBalancer;

namespace Webserver
{
	public abstract class Message
	{
		/// <summary>
		/// The unique ID associated with this message. Used for replying to messages that require an answer. Null if no answer is required.
		/// </summary>
		public Guid ID { get; set; }
		/// <summary>
		/// Gets or sets the flags for this message. These must be set with bitwise operations.
		/// </summary>
		public MessageFlags Flags { get; set; }
		/// <summary>
		/// Gets the message type. Used to determine how this message should be processed.
		/// </summary>
		public MessageType Type { get; set; }
		/// <summary>
		/// The data that this message contains.
		/// </summary>
		public dynamic Data { get; set; }

		/// <summary>
		/// Whether this message is a broadcast.
		/// </summary>
		public bool isBroadcast { get; set; }

		/// <summary>
		/// Create a new message
		/// </summary>
		/// <param name="type">The type this message has. Controls how the receiver will respond to it.</param>
		/// <param name="Data">The data attached to this message, if any.</param>
		public Message(MessageType type, dynamic data = null)
		{
			Type = type;
			Data = data;
		}

		/// <summary>
		/// Converts a message into a Message object.
		/// </summary>
		/// <param name="buffer">The byte array containing the message.</param>
		public static T FromBytes<T>(byte[] buffer) where T : Message => FromJson<T>(JObject.Parse(Encoding.UTF8.GetString(buffer)));

		/// <summary>
		/// Converts a JOBject into a message object, if possible.
		/// </summary>
		/// <param name="json">The JObject containing the message.</param>
		/// <returns></returns>
		protected static T FromJson<T>(JObject json) where T : Message
		{
			//Check if all necessary keys are present.
			if (!json.TryGetValue("MessageID", out string rawID))
				throw new JsonReaderException("Invalid JSON: missing MessageID");
			if (!json.TryGetValue("Flags", out MessageFlags flags))
				throw new JsonReaderException("Invalid JSON: missing Flags");
			if (!json.TryGetValue("Type", out MessageType type))
				throw new JsonReaderException("Invalid JSON: missing Type");
			if (!json.TryGetValue("Data", out JToken dataValue))
				throw new JsonReaderException("Invalid JSON: missing Data");

			var result = (T)Activator.CreateInstance(typeof(T), new object[] { type, null });

			//Assign values
			if (!Guid.TryParse(rawID, out Guid ID))
				throw new FormatException("ID key is not a valid Guid");
			result.ID = ID;
			result.Flags = flags;

			//Deserialize data if necessary
			if (dataValue.Type != JTokenType.Null)
				result.Data = JsonConvert.DeserializeObject(dataValue.ToString(), NetworkUtils.JsonSettings);

			return result;
		}

		/// <summary>
		/// Get a JSON representation of this message.
		/// </summary>
		/// <returns></returns>
		public virtual JObject GetJson() => new JObject() {
				{ "MessageID", ID },
				{ "Flags", (int)Flags },
				{ "Type", Type.ToString() },
				{ "Data", Data == null? null : (Data is JObject || Data is JArray? Data : JsonConvert.SerializeObject(Data, NetworkUtils.JsonSettings)) }
			};

		/// <summary>
		/// Get this message's JSON representation as a byte array.
		/// </summary>
		public byte[] GetBytes() => Encoding.UTF8.GetBytes(GetJson().ToString(Formatting.None));
	}

	/// <summary>
	/// Enum defining various bitwise flags for <see cref="ServerMessage"/> instances.
	/// </summary>
	[Flags]
	public enum MessageFlags
	{
		None = default,
		Reply = 1 << 0,
	}

	/// <summary>
	/// Enum of message types used for internal server communication.
	/// </summary>
	public enum MessageType
	{
		//General
		InvalidMessage,

		// Load balancer message types
		Timeout,
		Discover,
		DiscoverResponse,
		Register,
		RegisterResponse,
		NewServer,
		StateChange,

		// Database replication message types
		DbChange,
		DbSyncBackupStart,
		DbSyncBackup,
		DbSyncStart,
		DbSync,

		//Chat
		Chat,
		ChatMessage,
		ChatroomUpdate,
	}
}
