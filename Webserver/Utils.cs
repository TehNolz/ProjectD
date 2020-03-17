using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver.Utils {
	class Utils {
	}

	/// <summary>
	/// JObject extension class
	/// </summary>
	public static class JObjectExtension {
		/// <summary>
		/// Tries to get the JToken with the specified property name. Returns false if the JToken can't be cast to the specified type.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="obj"></param>
		/// <param name="propertyName"></param>
		/// <param name="Value"></param>
		/// <returns></returns>
		public static bool TryGetValue<T>(this JObject obj, string propertyName, out JToken Value) {
			bool Found = obj.TryGetValue(propertyName, out Value);
			if (!Found) {
				return false;
			}
			try {
				Value.ToObject<T>();
#pragma warning disable CA1031 // Silence "Do not catch general exception types" message.
			} catch (ArgumentException) {
				return false;
			} catch (InvalidCastException) {
				return false;
			}
#pragma warning restore CA1031
			return true;
		}
	}
}
