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
	internal static class Utils
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
			var concreteMethod = func.Method.GetGenericMethodDefinition().MakeGenericMethod(new[] { type });
			// Invoke and return the new concretely typed method
			return (T)concreteMethod.Invoke(func.Target, args);
		}

		private static IEnumerable<A> Cast<A>(IEnumerable<dynamic> yeet) => yeet.Cast<A>().ToArray();
		public static dynamic[] Cast<T>(this IEnumerable<T> obj, Type type)
			=> InvokeGenericMethod<dynamic[]>((Func<IEnumerable<dynamic>, object>)Cast<object>, type, new[] { obj });
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
