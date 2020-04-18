using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Webserver
{
	/// <summary>
	/// Static package-private class containing miscellaneos utility methods.
	/// </summary>
	public static class Utils
	{
		public static Dictionary<string, List<string>> NameValueToDict(NameValueCollection data)
		{
			var result = new Dictionary<string, List<string>>();
			foreach (string key in data)
				result.Add(key?.ToLower() ?? "null", new List<string>(data[key]?.Split(',')));
			return result;
		}

		/// <summary>
		/// Invokes a generic function with the specified <see cref="Type"/> and parameter and
		/// returns the result.
		/// </summary>
		/// <typeparam name="T">The return type of the <paramref name="func"/>.</typeparam>
		/// <param name="func">The generic function to invoke.</param>
		/// <param name="type">The type to use as generic type parameter.</param>
		/// <param name="args">An array of arguments to pass to the function.</param>
		public static T InvokeGenericMethod<T>(dynamic func, Type type, object[] args)
		{
			// Cast the generic type to a specific type
			dynamic concreteMethod = func.Method.GetGenericMethodDefinition().MakeGenericMethod(new[] { type });
			// Invoke and return the new concretely typed method
			return (T)concreteMethod.Invoke(func.Target, args);
		}

		private static IEnumerable<A> Cast<A>(IEnumerable<dynamic> yeet) => yeet.Cast<A>().ToArray();
		public static dynamic[] Cast<T>(this IEnumerable<T> obj, Type type)
			=> InvokeGenericMethod<dynamic[]>((Func<IEnumerable<dynamic>, object>)Cast<object>, type, new[] { obj });
	}

	/// <summary>
	/// Extension method class.
	/// </summary>
	public static class JObjectExtension
	{
		/// <summary>
		/// Tries to get the JToken with the specified property name.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="obj"></param>
		/// <param name="propertyName"></param>
		/// <param name="value"></param>
		/// <returns>Returns false if the JToken can't be cast to the specified type.</returns>
		public static bool TryGetValue<T>(this JObject obj, string propertyName, out T value)
		{
			value = default;
			bool found = obj.TryGetValue(propertyName, out JToken jtoken);
			if (!found) return false;

			// Attempt to parse the jtoken
			try
			{
				// Parse the jtoken to an enum if T is an enum type
				if (typeof(T).IsEnum)
					value = (T)Enum.Parse(typeof(T), jtoken.ToString());
				else
					value = jtoken.ToObject<T>();
#pragma warning disable CA1031 // Silence "Do not catch general exception types" message.
			}
			catch (ArgumentException) { return false; }
			catch (OverflowException) { return false; }
			catch (InvalidCastException) { return false; }
			return true;
		}
	}
}
