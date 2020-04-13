using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Text;

namespace Webserver.LoadBalancer
{
	/// <summary>
	/// Represents a message which can be sent to a server.
	/// </summary>
	public class Message
	{
		/// <summary>
		/// The message type. Used to determine how this message should be processed.
		/// </summary>
		public readonly string Type;
		/// <summary>
		/// The data that this message contains.
		/// </summary>
		public readonly dynamic Data;

		/// <summary>
		/// Create a new server communication message
		/// </summary>
		/// <param name="type">The message type. Used by the receiver to determine how this message should be processed.</param>
		/// <param name="data">The data to be transmitted. Can be any serializable object</param>
		public Message(string type, dynamic data)
		{
			Type = type ?? throw new ArgumentNullException("Type cannot be null");
			Data = data;
		}

		///<inheritdoc cref="Message(string, object, bool)"/>
		public Message(InternalMessageType type, object data) : this(type.ToString(), data) { }

		/// <summary>
		/// Converts a received server communication message into a Message object.
		/// </summary>
		/// <param name="buffer">The byte array containing the message.</param>
		public Message(byte[] buffer)
		{
			//Convert the buffer to JObject
			var json = JObject.Parse(Encoding.UTF8.GetString(buffer));

			//Check if all necessary keys are present.
			if (!json.TryGetValue<string>("Type", out JToken typeValue) ||
				!json.TryGetValue<JToken>("Data", out JToken dataValue))
			{
				throw new JsonReaderException("Invalid server JSON: missing/invalid keys");
			}

			//Assign values
			Type = (string)typeValue;

			//Deserialize data if necessary
			if (dataValue.Type != JTokenType.Null)
			{
				Data = JsonConvert.DeserializeObject(dataValue.ToString(), Utils.JsonSettings);
			}
		}

		/// <summary>
		/// Get a byte representation of this message.
		/// </summary>
		/// <returns></returns>
		public byte[] GetBytes() => Encoding.UTF8.GetBytes(new JObject()
			{
				{"Type",  Type},
				{"Data", Data == null? null : JsonConvert.SerializeObject(Data, Utils.JsonSettings) },
			}.ToString(Formatting.None));
	}

	/// <summary>
	/// Enum of message types used for internal server communication.
	/// </summary>
	public enum InternalMessageType
	{
		Timeout,
		Discover,
		DiscoverResponse,
		Register,
		RegisterResponse,
		NewServer,
	}
}
