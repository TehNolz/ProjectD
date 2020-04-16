using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Webserver.LoadBalancer
{
	public static class NetworkUtils
	{
		/// <summary>
		/// Settings for the JSON Serializer. Add converters where needed.
		/// </summary>
		public static JsonSerializerSettings JsonSettings = new JsonSerializerSettings()
		{
			Converters = new List<JsonConverter>()
			{
				new IPAddressConverter(),
			},
			TypeNameHandling = TypeNameHandling.All,
			MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead
		};

		/// <summary>
		///	JsonConverter for IPAddress objects.
		/// </summary>
		public class IPAddressConverter : JsonConverter
		{
			public override bool CanConvert(Type objectType) => objectType == typeof(IPAddress);
			public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue(value.ToString());
			public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => IPAddress.Parse((string)reader.Value);
		}

		/// <summary>
		/// Reads a certain amount of bytes from a network stream.
		/// </summary>
		/// <param name="count">The amount of bytes to read.</param>
		/// <param name="stream">The network stream to read from</param>
		public static byte[] ReadBytes(int count, NetworkStream stream)
		{
			byte[] buffer = new byte[count];
			int totalBytesRead = 0;

			//Read until the necessary amount of bytes have been read.
			while (totalBytesRead < count)
			{
				//Read the remaining bytes and store them in the buffer
				int bytesRead = stream.Read(buffer, totalBytesRead, count - totalBytesRead);
				totalBytesRead += bytesRead;

				//No bytes read apparently indicates a lost connection.
				if (bytesRead == 0)
				{
					throw new SocketException((int)SocketError.TimedOut);
				}
			}
			return buffer;
		}
	}
}
