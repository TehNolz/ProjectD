using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;

namespace Webserver {
	class Utils {
		/// <summary>
		/// Converts a NameValueCollection to a Dictionary, allowing the data to be easily accessible.
		/// </summary>
		/// <param name="Data">The NameValueCollection to convert.</param>
		/// <returns></returns>
		public static Dictionary<string, List<string>> NameValueToDict(NameValueCollection Data) {
			Dictionary<string, List<string>> Result = new Dictionary<string, List<string>>();
			foreach(string key in Data) {
				Result.Add(key?.ToLower() ?? "null", new List<string>(Data[key]?.Split(',')));
			}
			return Result;
		}
	}

	/// <summary>
	/// JObject extension class
	/// </summary>
	public static class JObjectExtension {
		/// <summary>
		/// Tries to get the JToken with the specified property name. Returns false if the JToken can't be cast to the specified type, or if it doesn't exist.
		/// </summary>
		/// <typeparam name="T">The type the resulting JToken should be castable to. If the JToken can't be cast to this type, this function returns false.</typeparam>
		/// <param name="PropertyName">The name of the property to retrieve</param>
		/// <param name="Value">The resulting JToken, if it exists.</param>
		/// <returns></returns>
		public static bool TryGetValue<T>(this JObject O, string PropertyName, out JToken Value) {
			//Try to get the value. If it can't be found, return false.
			if(!O.TryGetValue(PropertyName, out Value)) {
				return false;
			}

			//Check if the value can be cast to T. If it can't, return false.
			try {
				Value.ToObject<T>();
			} catch(ArgumentException) {
				return false;
			} catch(InvalidCastException) {
				return false;
			}
			return true;
		}
	}
}
