using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Webserver
{
	class Utils
	{
		public static Dictionary<string, List<string>> NameValueToDict(NameValueCollection data)
		{
			var result = new Dictionary<string, List<string>>();
			foreach (string key in data)
				result.Add(key?.ToLower() ?? "null", new List<string>(data[key]?.Split(',')));
			return result;
		}
	}

	/// <summary>
	/// JObject extension class
	/// </summary>
	public static class JObjectExtension
	{
		/// <summary>
		/// Tries to get the JToken with the specified property name.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="obj"></param>
		/// <param name="propertyName"></param>
		/// <param name="Value"></param>
		/// <returns>Returns false if the JToken can't be cast to the specified type.</returns>
		public static bool TryGetValue<T>(this JObject obj, string propertyName, out JToken Value)
		{
			bool Found = obj.TryGetValue(propertyName, out Value);
			if (!Found)
			{
				return false;
			}
			try
			{
				Value.ToObject<T>();
#pragma warning disable CA1031 // Silence "Do not catch general exception types" message.
			}
			catch (ArgumentException)
			{
				return false;
			}
			catch (InvalidCastException)
			{
				return false;
			}
#pragma warning restore CA1031
			return true;
		}
	}
}
