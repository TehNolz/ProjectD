using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;

namespace Webserver
{
	/// <summary>
	/// Static package-private class containing miscellaneos utility methods.
	/// </summary>
	public static partial class Utils
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

		public static double oldProgress = 0;
		public static int previousBar = -1;
		public static void ProgressBar(
			double progress,
			string prefix = null,
			string suffix = null,
			int width = -1,
			string fillChars = "-=≡■",
			char emptyChar = '.',
			(ConsoleColor foreground, ConsoleColor background)? prefixColor = null,
			(ConsoleColor foreground, ConsoleColor background)? barColor = null,
			(ConsoleColor foreground, ConsoleColor background)? suffixColor = null)
		{
			progress = Math.Clamp(progress, 0, 1);
			oldProgress = progress;

			width = width < 0 ? Console.BufferWidth - 3 : width;
			width -= suffix is null ? 0 : suffix.Length + 1;
			width -= prefix is null ? 0 : prefix.Length + 1;
			width = Math.Min(width, Console.BufferWidth - 3);

			int totalFill = (int)Math.Round(width * progress * fillChars.Length); // Total fill, used for calculating the trailing char.
			int fill = totalFill / fillChars.Length; // Leading character fill. Filled with the last fillChar.
			int charIndex = (totalFill % fillChars.Length) - 1; // Index of the trailing char. -1 is ignored and 4 is never reached here.

			// Cache console values
			(
				int left,
				int top,
				bool showCursor,
				(ConsoleColor, ConsoleColor) color
			) = (
				Console.CursorLeft,
				Console.CursorTop,
				Console.CursorVisible,
				(Console.ForegroundColor, Console.BackgroundColor)
			);
			Console.CursorVisible = false;

			// Move the cursor value back to the end of the stream
			Console.Write((string)null); 

			int wtop = Console.WindowTop;
			if (Console.CursorTop > Console.WindowHeight - 1)
			{
				// "Re-focus" to the end of the stream by adjusting the WindowTop position
				Console.WindowTop = Console.CursorTop - (Console.WindowHeight - 1);
				wtop = Console.WindowTop;
			}
			else
			{
				// Fixes strange issue where WindowTop is 1 despite not having moved the window
				Console.WindowTop = 0;
				wtop -= 1;
			}

			try
			{
				// Remove the previous bar
				if (CustomWriter.ProgressBarPos != wtop + Console.WindowHeight)
					ClearProgressBar();
				Console.SetCursorPosition(0, wtop + Console.WindowHeight);
				CustomWriter.ProgressBarPos = Console.CursorTop;

				// Write the progress bar
				(Console.ForegroundColor, Console.BackgroundColor) = prefixColor ?? color; // Sets the prefix color or defaults to the cached console color
				Console.Write(prefix);

				(Console.ForegroundColor, Console.BackgroundColor) = barColor ?? color;
				Console.Write(prefix is null ? "[" : " [");
				// Add leading chars
				Console.Write(new string(fillChars[^1], fill));
				if (charIndex >= 0)
				{
					Console.Write(fillChars[charIndex]);
					// Add 1 to fill to shorten the trailing spaces
					fill++;
				}
				// Add trailing spaces
				Console.Write(new string(emptyChar, Math.Max(0, width - fill)));
				Console.Write(suffix is null ? "]" : "] ");

				(Console.ForegroundColor, Console.BackgroundColor) = suffixColor ?? color;
				Console.Write(suffix is null ? null : ' ' + suffix);
			}
			catch (IOException) { } // Can happen while resizing. Can be ignored
			finally
			{
				// Reset console values
				Console.SetCursorPosition(left, top);
				Console.CursorVisible = showCursor;
			}
		}
		/// <summary>
		/// Displays a simple progress bar with a predefined style.
		/// </summary>
		/// <param name="progress">The progress value ranging from 0 to 1.</param>
		public static void ProgressBar(double progress)
			=> ProgressBar(progress, $"Progress [{progress,-4:P0}]", prefixColor: (ConsoleColor.White, ConsoleColor.Green));
		/// <summary>
		/// Displays a simple progress bar with a predefined style.
		/// </summary>
		/// <param name="value">The current value.</param>
		/// <param name="max">The maximum value.</param>
		public static void ProgressBar(int value, int max)
		{
			int maxLen = max.ToString().Length;
			double progress = (double)value / max;
#pragma warning disable IDE0071, IDE0071WithoutSuggestion // Simplify interpolation
			ProgressBar(progress, $"Progress [{value.ToString().PadLeft(maxLen)}/{max}]", prefixColor: (ConsoleColor.White, ConsoleColor.Green));
#pragma warning restore IDE0071, IDE0071WithoutSuggestion // Simplify interpolation
		}

		public static void ClearProgressBar()
		{
			if (CustomWriter.ProgressBarPos == -1)
				return;

			// Prepare console for writing
			(int left, int top, bool showCursor, int wtop) = (Console.CursorLeft, Console.CursorTop, Console.CursorVisible, Console.WindowTop);
			Console.CursorVisible = false;
			Console.SetCursorPosition(0, CustomWriter.ProgressBarPos);
			
			// Clear the progress bar
			Console.Write(new string(' ', Console.BufferWidth));

			// Reset console values
			Console.SetCursorPosition(left, top);
			Console.CursorVisible = showCursor;
			Console.WindowTop = wtop;

			CustomWriter.ProgressBarPos = -1;
		}
	}

	/// <summary>
	/// Custom console output stream handler that manages the progressbar position.
	/// </summary>
	public class CustomWriter : TextWriter
	{
		public static int ProgressBarPos { get; set; } = -1;
		public override Encoding Encoding { get; }
		private TextWriter writer;

		public CustomWriter(Encoding encoding, TextWriter writer)
		{
			Encoding = encoding;
			this.writer = writer;
		}

		public override void Write(char value)
		{
			writer.Write(value);
			if (value == '\n')
				MoveProgressBar();
		}

		private void MoveProgressBar()
		{
			if (ProgressBarPos == -1)
				return;

			if (Console.CursorTop >= Console.WindowHeight - 1)
			{
				// Move the progress bar line one down
				Console.MoveBufferArea(0, ProgressBarPos, Console.BufferWidth, 1, 0, ++ProgressBarPos);
				// Shift the window view downwards
				Console.WindowTop++;
			}
		}
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
	}
}
