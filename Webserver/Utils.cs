using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Webserver
{
	/// <summary>
	/// Static package-private class containing miscellaneos utility methods.
	/// </summary>
	public static partial class Utils
	{
		/// <summary>
		/// Convert a NameValueCollection to a dictionary for ease of access.
		/// </summary>
		/// <param name="data">The NameValueCollection to convert</param>
		/// <returns></returns>
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

		/// <summary>
		/// Parses the give string into a long. This accepts decimal and binary orders of magnitude.
		/// </summary>
		/// <param name="s">The string to parse.</param>
		public static long ParseDataSize(string s)
		{
			// Use a regex to split value from unit  @"(\d+)\s*([A-Za-z]*)"
			GroupCollection groups = Regex.Match(s, @"([+-]?(?=\.\d|\d)(?:\d+)?(?:\.?\d*)(?:[eE][+-]?\d+)?)\s*([A-Za-z]+)?").Groups;

			// Extract the values
			decimal value = decimal.Parse(groups[1].Value, NumberStyles.Any);
			string unit = groups[1].Value.Length == 0 ? "B" : groups[2].Value;

			bool isBinaryMagnitude = unit.Length == 3;

			// Calculate the value using switch label "fallthrough"
			int magnitude = 0;
			switch (unit.ToUpper()[0])
			{
				case 'Y': magnitude++; goto case 'Z';
				case 'Z': magnitude++; goto case 'E';
				case 'E': magnitude++; goto case 'P';
				case 'P': magnitude++; goto case 'T';
				case 'T': magnitude++; goto case 'G';
				case 'G': magnitude++; goto case 'M';
				case 'M': magnitude++; goto case 'K';
				case 'K': magnitude++; break;
				default: break;
			}

			// Multiply the value by the magnitude and return it
			return (long)(value * (decimal)(isBinaryMagnitude ? Math.Pow(2, 10 * magnitude) : Math.Pow(1000, magnitude)));
		}
	}

	/// <summary>
	/// Custom string formatter for reducing a data size in bytes to a smaller unit.
	/// </summary>
	public sealed class DataFormatter : IFormatProvider, ICustomFormatter
	{
		/// <summary>
		/// Gets or sets whether a space is added between the value and the unit.
		/// </summary>
		public bool SpaceBeforeUnit { get; set; } = true;

		/// <summary>
		/// Describes the units that the <see cref="DataFormatter"/> can format to.
		/// </summary>
		[Flags]
		private enum Unit
		{
			/// <summary>
			/// Displays the value as standard decimal unit.
			/// <para/>
			/// This can be overridden by <see cref="Binary"/>.
			/// </summary>
			Decimal = 1 << 0,
			/// <summary>
			/// Displays the value as a binary unit.
			/// <para/>
			/// Overrides <see cref="Decimal"/>.
			/// </summary>
			Binary = (1 << 1) | Decimal,
			/// <summary>
			/// Flag specifying whether to display bits.
			/// </summary>
			Bits = 1 << 2
		}

		public string Format(string formatStr, object arg, IFormatProvider formatProvider)
		{
			// Check whether this is an appropriate callback
			if (!Equals(formatProvider))
				return null;

			// Set default format specifier
			if (string.IsNullOrEmpty(formatStr))
				formatStr = "D";

			Unit format = formatStr[0] switch
			{
				'D' => Unit.Decimal,
				'd' => Unit.Decimal | Unit.Bits,
				'B' => Unit.Binary,
				'b' => Unit.Binary | Unit.Bits,
				_ => throw new FormatException($"The {formatStr} format specifier is invalid.")
			};

			long value = Convert.ToInt64(arg);
			int decimals = formatStr.Length > 1 ? int.Parse(formatStr[1..]) : 0;

			// Calculate the magnitude by comparing the value with each magnitude up to yotta
			sbyte magnitude = 0;
			for (; magnitude <= 8; magnitude++)
				if ((format.HasFlag(Unit.Decimal) ? Math.Pow(1000, magnitude + 1) : Math.Pow(2, 10 * (magnitude + 1))) >= value)
					break;

			string unitPrefix = magnitude switch
			{
				0 => "",
				1 => format.HasFlag(Unit.Binary) ? "Ki" : "k",
				2 => format.HasFlag(Unit.Binary) ? "Mi" : "M",
				3 => format.HasFlag(Unit.Binary) ? "Gi" : "G",
				4 => format.HasFlag(Unit.Binary) ? "Ti" : "T",
				5 => format.HasFlag(Unit.Binary) ? "Pi" : "P",
				6 => format.HasFlag(Unit.Binary) ? "Ei" : "E",
				7 => format.HasFlag(Unit.Binary) ? "Zi" : "Z",
				_ => format.HasFlag(Unit.Binary) ? "Yi" : "Y"
			};
			string unit = $"{unitPrefix}{(format.HasFlag(Unit.Bits) ? "bit" : "B")}";

			// Divide the value by the magnitude
			double formatValue = Math.Round(value / (format.HasFlag(Unit.Binary) ? Math.Pow(2, 10 * magnitude) : Math.Pow(1000, magnitude)), decimals);
			return string.Join(SpaceBeforeUnit ? " " : "",
				formatValue.ToString($"F{decimals}"),
				unit
			);
		}

		public object GetFormat(Type formatType)
		{
			// Yes, this is largely taken from the example on
			// https://docs.microsoft.com/en-us/dotnet/standard/base-types/how-to-define-and-use-custom-numeric-format-providers
			if (formatType == typeof(ICustomFormatter))
				return this;
			else
				return null;
		}
	}

	/// <summary>
	/// Class containing extension methods.
	/// </summary>
	internal static class ExtensionMethods
	{
		private static IEnumerable<A> Cast<A>(IEnumerable<dynamic> yeet) => yeet.Cast<A>().ToArray();
		/// <summary>
		/// Performs black magic.
		/// </summary>
		public static dynamic[] Cast<T>(this IEnumerable<T> obj, Type type)
			=> Utils.InvokeGenericMethod<dynamic[]>((Func<IEnumerable<dynamic>, object>)Cast<object>, type, new[] { obj });

		/// <summary>
		/// Returns a formatted string depicting the elapsed time in either nanoseconds, microseconds or milliseconds
		/// depending on the magnitude of elapsed time.
		/// </summary>
		/// <param name="stopwatch">The <see cref="Stopwatch"/> instance whose elapsed time to format.</param>
		public static string Format(this Stopwatch stopwatch)
		{
			stopwatch.Stop();
			try
			{
				if (stopwatch.ElapsedTicks < 10)
					return (stopwatch.ElapsedTicks * 100) + " ns";
				if (stopwatch.ElapsedTicks < 10000)
					return (stopwatch.ElapsedTicks / 10) + " µs";
				return stopwatch.ElapsedMilliseconds + " ms";
			}
			finally
			{
				stopwatch.Start();
			}
		}

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
			if (!found)
				return false;

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

#nullable enable
		/// <summary>
		/// Returns the first file whose path equals <paramref name="path"/>, or <see langword="null"/>
		/// if none were found.
		/// </summary>
		/// <param name="directory">The directory to search the <paramref name="path"/> for.</param>
		/// <param name="path">The of the <see cref="FileInfo"/> to return. This path must point to a file that exists
		/// within <paramref name="directory"/>, be it absolute or relative.</param>
		/// <param name="caseSensitive">Sets whether the search is case sensitive or insensitive.</param>
		public static FileInfo? FindFile(this DirectoryInfo directory, string path, bool caseSensitive = false)
			=> (from FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories)
				where caseSensitive
					? file.FullName == Path.GetFullPath(path)
					: file.FullName.ToLower() == Path.GetFullPath(path).ToLower()
				select file).FirstOrDefault();
#nullable restore
	}

	/// <summary>
	/// Structure for describing the differences between 2 <see cref="JObject"/>s.
	/// </summary>
	public struct JsonDiff
	{
		/// <summary>
		/// Gets a <see cref="JObject"/> containing all new items.
		/// </summary>
		public JObject Added { get; }
		/// <summary>
		/// Gets a <see cref="JObject"/> containing all changes made to
		/// existing items.
		/// </summary>
		public JObject Changed { get; }
		/// <summary>
		/// Gets a <see cref="JObject"/> containing all removed items.
		/// </summary>
		public JObject Removed { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="JsonDiff"/> structure that describes
		/// all changes made to <paramref name="j2"/> compared to <paramref name="j1"/>.
		/// </summary>
		public JsonDiff(JObject j1, JObject j2)
		{
			// Get all properties of both JObjects
			IEnumerable<JProperty> j1props = j1.Properties();
			IEnumerable<JProperty> j2props = j2.Properties();

			// Initialize all members
			Added = new JObject();
			Changed = new JObject();
			Removed = new JObject();

			// Fill the `Added` JObject with all properties unique to j2
			foreach (JProperty prop in j2props.Where(x => !j1props.Any(y => x.Name == y.Name)))
				Added.Add(prop.Name, prop.Value);

			// Fill the `Removed` JObject with all properties unique to j1
			foreach (JProperty prop in j1props.Where(x => j2props.Where(y => x.Name == y.Name).Count() == 0))
				Removed.Add(prop.Name, prop.Value);

			// Recursively find all changes between j1 and j2
			foreach (JProperty prop in j1props)
			{
				// Find the equivalent property in j2
				JProperty other = j2props.FirstOrDefault(x => x.Name == prop.Name);
				if (other == null)
					continue;

				// If the type of the other property is different, just use the new value
				if (other.Value.Type != prop.Value.Type)
				{
					Changed.Add(prop.Name, other.Value);
				}
				// If they are different, add the other one to this diff
				else if (!JToken.DeepEquals(prop, other))
				{
					// If they are both JObjects, use their diff
					if (other.Value.Type == JTokenType.Object && prop.Value.Type == JTokenType.Object)
					{
						var diff = new JsonDiff(prop.Value as JObject, other.Value as JObject);

						// Add the other diff to this if they contain anything
						if (diff.Added.Count != 0)
							Added.Add(prop.Name, diff.Added);
						if (diff.Changed.Count != 0)
							Changed.Add(prop.Name, diff.Changed);
						if (diff.Removed.Count != 0)
							Removed.Add(prop.Name, diff.Removed);
					}
					// If they aren't both JObjects, simply add the other to this diff
					else
						Changed.Add(prop.Name, other.Value);
				}
			}
		}
	}
}
