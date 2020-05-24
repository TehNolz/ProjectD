using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

#nullable enable
	/// <summary>
	/// Allows for displaying customizable progress bars in the console.
	/// </summary>
	public class ProgressBar : IDisposable
	{
		/// <summary>
		/// Class for describing the color of text written to the console.
		/// </summary>
		public sealed class Color
		{
			/// <summary>
			/// Gets or sets the text foreground color.
			/// <para/>
			/// Equal to <see cref="Console.ForegroundColor"/> by default.
			/// </summary>
			public ConsoleColor Foreground { get; set; } = Console.ForegroundColor;
			/// <summary>
			/// Gets or sets the text background color.
			/// <para/>
			/// Equal to <see cref="Console.BackgroundColor"/> by default.
			/// </summary>
			public ConsoleColor Background { get; set; } = Console.BackgroundColor;

			/// <summary>
			/// Sets the <see cref="Console.ForegroundColor"/> and <see cref="Console.BackgroundColor"/>
			/// to this instance's colors.
			/// </summary>
			public void Apply() => (Console.ForegroundColor, Console.BackgroundColor) = this;

			/// <summary>
			/// Deconstructs this <see cref="Color"/> instance into the given <paramref name="foreground"/>
			/// and <paramref name="background"/> parameters.
			/// </summary>
			public void Deconstruct(out ConsoleColor foreground, out ConsoleColor background)
			{
				foreground = Foreground;
				background = Background;
			}
		}

		/// <summary>
		/// The collection containing all currently active progress bars.
		/// </summary>
		internal static List<ProgressBar> Slots { get; } = new List<ProgressBar>();

		/// <summary>
		/// Formats and returns the prefix or sets the format string for the prefix
		/// displayed by the progress bar.
		/// <para/>
		/// <see cref="FormatArguments"/> is used as arguments to format this value.
		/// </summary>
		/// <seealso cref="FormatArguments"/>
		public string? Prefix
		{
			get => _prefix is null ? null : string.Format(_prefix, FormatArguments);
			set => _prefix = value;
		}
		/// <summary>
		/// Formats and returns the suffix or sets the format string for the suffix
		/// displayed by the progress bar.
		/// <para/>
		/// <see cref="FormatArguments"/> is used as arguments to format this value.
		/// </summary>
		/// <seealso cref="FormatArguments"/>
		public string? Suffix
		{
			get => _suffix is null ? null : string.Format(_suffix, FormatArguments);
			set => _suffix = value;
		}

		/// <summary>
		/// Gets or sets the string of characters that are used to draw the progress bar.
		/// </summary>
		/// <remarks>
		/// The progress bar will cycle through all of the fill characters at the end of the bar
		/// before moving onto the next position. This effectively increases the progress bar resolution.
		/// <para/>
		/// Therefore, a bar with a width of 25 that has 4 fill characters has an effective resolution of 100
		/// characters because each position is divided into 4 characters instead of 1.
		/// </remarks>
		/// <exception cref="ArgumentException">Value is null or empty.</exception>
		[DisallowNull]
		public string FillCharacters
		{
			get => _fillCharacters;
			set
			{
				if (string.IsNullOrEmpty(value))
					throw new ArgumentException("Value must contain at least one character.", nameof(value));
				_fillCharacters = value;
			}
		}
		/// <summary>
		/// Gets or sets the character used for filling the empty space in the progress bar.
		/// </summary>
		public char EmptyCharacter { get; set; } = '.';

		/// <summary>
		/// Gets or sets the color of the progress bar's prefix text.
		/// </summary>
		/// <exception cref="ArgumentNullException">Value is null.</exception>
		[DisallowNull]
		public Color PrefixColor
		{
			get => _prefixColor;
			set
			{
				if (value is null)
					throw new ArgumentNullException(nameof(value));
				_prefixColor = value;
			}
		}
		/// <summary>
		/// Gets or sets the color of the progress bar, excluding the prefix and suffix.
		/// </summary>
		/// <exception cref="ArgumentNullException">Value is null.</exception>
		[DisallowNull]
		public Color BarColor
		{
			get => _barColor;
			set
			{
				if (value is null)
					throw new ArgumentNullException(nameof(value));
				_barColor = value;
			}
		}
		/// <summary>
		/// Gets or sets the color of the progress bar's suffix text.
		/// </summary>
		/// <exception cref="ArgumentNullException">Value is null.</exception>
		[DisallowNull]
		public Color SuffixColor
		{
			get => _suffixColor;
			set
			{
				if (value is null)
					throw new ArgumentNullException(nameof(value));
				_suffixColor = value;
			}
		}

		/// <summary>
		/// Gets or sets the size of the progress bar, including prefix and suffix length.
		/// <para/>
		/// This always returns <c><see cref="Console.BufferWidth"/> - 1</c>.
		/// </summary>
		public virtual int Size
		{
			// -1 because cmd.exe does not like characters written at the rightmost column of the console.
			get => Console.BufferWidth - 1;
			set => throw new NotSupportedException("This progress bar class does not support resizing.");
		}

		/// <summary>
		/// Gets or sets the minimum value for <see cref="Progress"/>.
		/// </summary>
		public virtual double MinProgress { get; set; } = 0;
		/// <summary>
		/// Gets or sets the maximum value for <see cref="Progress"/>.
		/// </summary>
		public virtual double MaxProgress { get; set; } = 1;
		/// <summary>
		/// Gets the progress value ranging from <see cref="MinProgress"/> to <see cref="MaxProgress"/>.
		/// </summary>
		public double Progress
		{
			get => _progress;
			private set => _progress = Math.Clamp(value, MinProgress, MaxProgress);
		}

		/// <summary>
		/// Gets an array of arguments used to format various strings of the progress bar.
		/// <para/>
		/// These include:
		/// <list type="bullet">
		/// <item><c>{0}</c> → The <see cref="Progress"/> value.</item>
		/// <item><c>{1}</c> → The <see cref="MinProgress"/> value.</item>
		/// <item><c>{2}</c> → The <see cref="MaxProgress"/> value.</item>
		/// <item><c>{3}</c> → The <see cref="Progress"/> value ranging from 0 to 1.</item>
		/// </list>
		/// </summary>
		public virtual object[] FormatArguments => new object[]
		{
			Progress,
			MinProgress,
			MaxProgress,
			(Progress - MinProgress) / (MaxProgress - MinProgress)
		};

		private string? _prefix = "Progress [{3,-4:P0}]";
		private string? _suffix;
		private string _fillCharacters = "-=≡■";
		private Color _prefixColor = new Color() { Foreground = ConsoleColor.White, Background = ConsoleColor.Green };
		private Color _barColor = new Color();
		private Color _suffixColor = new Color();
		private double _progress;

		/// <summary>
		/// Draws this progress bar to the console.
		/// <para/>
		/// This class' progress bars are always stacked at the bottom of the console window.
		/// </summary>
		/// <param name="progress">The progress to display, ranging from 0 to 1.</param>
		public void Draw(double progress)
		{
			Progress = progress;

			// Only draw if nescessary
			if (ShouldDraw())
				Draw();
		}

		/// <summary>
		/// Clears this progress bar from the console.
		/// </summary>
		public virtual void Clear()
		{
			lock (Console.Out)
			{
				// Ignore redundant calls
				if (!Slots.Contains(this))
					return;

				Remove();
			}
		}

		#region Cached Values
		// These values are calculated in ShouldDraw() and can be reused in Draw()
		private string? prefix_cache;
		private string? suffix_cache;
		private int maxFill_cache;
		private int fill_cache;
		private double progress_cache = -1;
		#endregion
		/// <summary>
		/// Returns whether the progress bar should be drawn or not.
		/// </summary>
		/// <remarks>
		/// By default, this method returns true when the new progress value will have a visible effect
		/// on the bar portion of this <see cref="ProgressBar"/>.
		/// <para/>
		/// If this returns <see langword="true"/>, then <see cref="Draw"/> will be invoked.
		/// </remarks>
		protected virtual bool ShouldDraw()
		{
			// If they are equal, nothing has changed so return false
			if (Progress == progress_cache)
				return false;
			progress_cache = Progress;

			// Create the current prefix and suffix and compare them with the previous ones
			string? prefix = Prefix;
			string? suffix = Suffix;

			// Bar size is the remaining space for the bar multiplied by the amount of fill characters
			maxFill_cache = FillCharacters.Length * (Size - (prefix is null ? 0 : prefix.Length + 1) - (suffix is null ? 0 : suffix.Length + 1) - 2);
			int fill = (int)Math.Round(maxFill_cache * ((Progress - MinProgress) / (MaxProgress - MinProgress)));

			// Check if anything has changed
			if (fill != fill_cache ||
				prefix != prefix_cache ||
				suffix != suffix_cache)
			{
				// Update the chached values
				prefix_cache = prefix;
				suffix_cache = suffix;
				fill_cache = fill;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Writes this <see cref="ProgressBar"/> to the console.
		/// </summary>
		protected virtual void Draw()
		{
			lock (Console.Out)
			{
				// Add this progress bar to the slots if it isn't already there
				if (!Slots.Contains(this))
					Add();

				int windowTop = Console.WindowTop;
				if (Console.CursorTop > Console.WindowHeight - Slots.Count)
				{
					// "Re-focus" to the end of the stream by adjusting the WindowTop position
					windowTop = Console.CursorTop - (Console.WindowHeight - Slots.Count - 1);
					Console.WindowTop = windowTop;
				}

				// Cache some console values to reset later
				(int, int, bool, Color) settingsCache = (
					Console.CursorLeft,
					Console.CursorTop,
					Console.CursorVisible,
					new Color() // Color initializes with the current console colors by default
				);

				// Hide the cursor
				Console.CursorVisible = false;

				// Get the position of this progress bar within the slots
				int slotPos = Slots.IndexOf(this) + 1;

				// Move the cursor to the bottom left of the console
				Console.SetCursorPosition(0, windowTop + Console.WindowHeight - slotPos);

				if (!(prefix_cache is null))
				{
					PrefixColor.Apply();
					Console.Write(prefix_cache);
					settingsCache.Item4.Apply();
					Console.Write(' ');
				}

				int fill = fill_cache / FillCharacters.Length;
				int charIndex = (fill_cache % FillCharacters.Length) - 1;
				int empty = (maxFill_cache - fill_cache) / FillCharacters.Length;

				BarColor.Apply();
				Console.Write('[');
				Console.Write(new string(FillCharacters[^1], fill));
				if (charIndex >= 0)
					Console.Write(FillCharacters[charIndex]);
				Console.Write(new string(EmptyCharacter, empty));
				Console.Write(']');

				if (!(suffix_cache is null))
				{
					Console.Write(' ');
					SuffixColor.Apply();
					Console.Write(suffix_cache);
				}

				// Reset the cached console values
				(
					Console.CursorLeft,
					Console.CursorTop,
					Console.CursorVisible,
					(Console.ForegroundColor, Console.BackgroundColor)
				) = settingsCache;

				// Undoes any scrolling that may have happened while writing the progress bar (cmd.exe likes to do this)
				Console.WindowTop = windowTop;
			}
		}

		/// <summary>
		/// Adds this <see cref="ProgressBar"/> to the <see cref="Slots"/>
		/// and shifts other progress bars upwards.
		/// </summary>
		private void Add()
		{
			lock (Console.Out)
			{
				// Calculate the position of the topmost progress bar
				int sourceTop = Console.WindowTop + Console.WindowHeight - Slots.Count;

				if (sourceTop - 1 <= Console.CursorTop)
				{
					// If the console has reached the end of the buffer
					if (Console.WindowTop + Console.WindowHeight == Console.BufferHeight)
					{
						// Cache some console values to reset later
						(int, int, bool) settingsCache = (
							Console.CursorLeft,
							Console.CursorTop - 1, // -1 to account for the newly created line
							Console.CursorVisible
						);

						// Hide the cursor
						Console.CursorVisible = false;

						// Move to the end of the buffer
						Console.SetCursorPosition(0, Console.BufferHeight - 1);
						// Open default output and write a newline (bypasses the CustomWriter instance)
						using (var sw = new StreamWriter(Console.OpenStandardOutput()))
							sw.WriteLine();

						// Reset the cached console values
						(
							Console.CursorLeft,
							Console.CursorTop,
							Console.CursorVisible
						) = settingsCache;
					}
					// Otherwise simply move the window downwards
					else
						Console.WindowTop++;
				}
				else if (Slots.Count > 0)
				{
					// Shift the other bars up
					Console.MoveBufferArea(0, sourceTop, Console.BufferWidth, Slots.Count, 0, sourceTop - 1);
				}
				Slots.Insert(0, this);
			}
		}
		/// <summary>
		/// Removes this <see cref="ProgressBar"/> from the <see cref="Slots"/> and shifts
		/// the progress bars above itself downwards.
		/// </summary>
		private void Remove()
		{
			int slotPos = Slots.IndexOf(this) + 1;

			// If this is the last element in the slots, simply clear the line
			if (slotPos == Slots.Count)
			{
				// Cache some console values to reset later
				(int, int, bool) settingsCache = (
					Console.CursorLeft,
					Console.CursorTop,
					Console.CursorVisible
				);

				// Hide the cursor
				Console.CursorVisible = false;

				// Write a full line of whitespaces at this progress bar's position
				Console.SetCursorPosition(0, Console.WindowTop + Console.WindowHeight - slotPos);
				Console.Write(new string(' ', Size));

				// Reset the cached console values
				(
					Console.CursorLeft,
					Console.CursorTop,
					Console.CursorVisible
				) = settingsCache;
			}
			// If this is not the last element in the slots, shift the other bars downwards
			else
			{
				int sourceTop = Console.WindowTop + Console.WindowHeight - Slots.Count;
				Console.MoveBufferArea(0, sourceTop, Console.BufferWidth, Slots.Count - slotPos, 0, sourceTop + 1);
			}
			Slots.Remove(this);

			// Reset progress cache for potential reuse of this instance
			progress_cache = -1;
		}

		/// <summary>
		/// Clears this progress bar from the console. Equivalent to calling
		/// <see cref="Clear"/>.
		/// </summary>
		/// <seealso cref="Clear"/>
		public void Dispose() => Clear();
	}
#nullable restore

	/// <summary>
	/// Custom console output stream handler that manages the progressbar position.
	/// </summary>
	public sealed class CustomWriter : TextWriter
	{
		public override Encoding Encoding { get; }
		private TextWriter writer;

		public CustomWriter(Encoding encoding, TextWriter writer)
		{
			Encoding = encoding;
			this.writer = writer;
		}

		public override void Write(char value)
		{
			lock (this)
			{
				writer.Write(value);
				if (value == '\n')
					MoveProgressBars();
			}
		}

		private void MoveProgressBars()
		{
			// Skip if no progress bars are active
			if (!ProgressBar.Slots.Any())
				return;

			int windowTop = Console.WindowTop;
			if (Console.CursorTop > Console.WindowHeight - ProgressBar.Slots.Count)
			{
				// "Re-focus" to the end of the stream by adjusting the WindowTop position
				windowTop = Console.CursorTop - (Console.WindowHeight - ProgressBar.Slots.Count);
				Console.WindowTop = windowTop;
			}

			// If the cursor has reached the end of the buffer
			if (Console.CursorTop >= Console.BufferHeight - ProgressBar.Slots.Count)
			{
				// Cache some console values to reset later
				(int, int, bool) settingsCache = (
					Console.CursorLeft,
					Console.CursorTop - 1, // -1 to account for the newly created line
					Console.CursorVisible
				);

				// Hide the cursor
				Console.CursorVisible = false;

				// Move to the end of the buffer and create a new line (creates extra space where the progress bars can be copied to)
				Console.SetCursorPosition(0, Console.BufferHeight - 1);
				writer.WriteLine();

				// Reset the cached console values
				(
					Console.CursorLeft,
					Console.CursorTop,
					Console.CursorVisible
				) = settingsCache;

				// Calculate the position of the topmost progress bar (-1 to account for the new emtpy line that was created)
				int sourceTop = windowTop + Console.WindowHeight - ProgressBar.Slots.Count - 1;

				// Move the progress bars one line down
				Console.MoveBufferArea(0, sourceTop, Console.BufferWidth, ProgressBar.Slots.Count, 0, sourceTop + 1);
			}
			else if (Console.CursorTop >= Console.WindowHeight - ProgressBar.Slots.Count)
			{
				// Calculate the position of the topmost progress bar
				int sourceTop = windowTop + Console.WindowHeight - ProgressBar.Slots.Count;

				// Move the progress bar line one down
				Console.MoveBufferArea(0, sourceTop, Console.BufferWidth, ProgressBar.Slots.Count, 0, sourceTop + 1);

				// Shift the window view downwards
				Console.WindowTop = windowTop + 1;
			}
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
	}
}
