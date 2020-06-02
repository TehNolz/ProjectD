using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace Webserver
{
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
		private Color _barColor = Program.InitialConsoleColor;
		private Color _suffixColor = Program.InitialConsoleColor;
		private double _progress;

		/// <summary>
		/// Draws this progress bar to the console. This also sets the <see cref="Progress"/> value
		/// to the given <paramref name="progress"/>.
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
		/// Increment this progress bar's <see cref="Progress"/> by the given <paramref name="amount"/>
		/// and then draws this progress bar.
		/// </summary>
		/// <param name="amount"></param>
		public void Increment(double amount) => Draw(Progress + amount);

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
			try
			{
				lock (Console.Out)
				{
					// Add this progress bar to the slots if it isn't already there
					if (!Slots.Contains(this))
						Add();

					int windowTop = Console.WindowTop;
					if (Console.CursorTop > Console.WindowHeight - Slots.Count && Console.WindowHeight != Console.BufferHeight)
					{
						// "Re-focus" to the end of the stream by adjusting the WindowTop position
						windowTop = Console.CursorTop - (Console.WindowHeight - Slots.Count - 1);
						if (windowTop > Console.BufferHeight - Console.WindowHeight)
							CustomWriter.ExtendBuffer(windowTop - (Console.BufferHeight - Console.WindowHeight));
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
					if (Console.WindowTop > windowTop)
						Console.WindowTop = windowTop;
				}
			}
			catch (Exception e) when (e is ArgumentOutOfRangeException || e is IOException)
			{
				// These happen during resizing and what not
				// i literally cannot be bothered to fix this because the console seems
				// to be able to change its size and values at virtually any moment
				// so writing code to prevent issues is not really possible.
			}
		}

		/// <summary>
		/// Adds this <see cref="ProgressBar"/> to the <see cref="Slots"/>
		/// and shifts other progress bars upwards.
		/// </summary>
		private void Add()
		{
			try
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
								Console.CursorTop - 1, // -1 to account for the newly created line
								Console.CursorLeft,
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
								Console.CursorTop,
								Console.CursorLeft,
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
						Console.MoveBufferArea(0, sourceTop, Slots.Max(x => x.Size), Slots.Count, 0, sourceTop - 1);
					}
					Slots.Insert(0, this);
				}
			}
			catch (Exception e) when (e is ArgumentOutOfRangeException || e is IOException)
			{
				// Ignore
			}
		}
		/// <summary>
		/// Removes this <see cref="ProgressBar"/> from the <see cref="Slots"/> and shifts
		/// the progress bars above itself downwards.
		/// </summary>
		private void Remove()
		{
			try
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
					Console.MoveBufferArea(0, sourceTop, Slots.Max(x => x.Size), Slots.Count - slotPos, 0, sourceTop + 1);
				}
				Slots.Remove(this);

				// Reset progress cache for potential reuse of this instance
				progress_cache = -1;
			}
			catch (Exception e) when (e is ArgumentOutOfRangeException || e is IOException)
			{
				// Ignore these exceptions
				// I have no idea if this code can throw these exceptions but given the unpredictable
				// nature of the console, I'd say yes.
			}
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
				{
					try
					{
						// Reposition the progressbars 
						MoveProgressBars();
					}
					catch (Exception e) when (e is ArgumentOutOfRangeException || e is IOException)
					{
						try
						{
							// Move the cursor back to try to undo the previous attempt at buffer extension
							if (Console.CursorTop != 0)
								Console.CursorTop--;
						}
						catch (Exception e1) when (e1 is ArgumentOutOfRangeException || e1 is IOException)
						{
							// No idea if this is even nescessary but honestly,
							// talking to the Console class is kind of taboo around here,
							// so it's best to keep this a secret, okay?
						}
					}
				}
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
				if (windowTop > Console.BufferHeight - Console.WindowHeight)
					ExtendBuffer(windowTop - (Console.BufferHeight - Console.WindowHeight));
				Console.WindowTop = windowTop;
			}

			int newLines = Console.BufferHeight - ProgressBar.Slots.Count - Console.CursorTop + 1;

			// If the cursor has reached the end of the buffer
			if (Console.CursorTop >= Console.BufferHeight - ProgressBar.Slots.Count)
			{

				// Cache some console values to reset later
				(int, int, bool) settingsCache = (
					Console.CursorTop - 1, // -1 to account for the newly created line
					Console.CursorLeft,
					Console.CursorVisible
				);

				// Hide the cursor
				Console.CursorVisible = false;

				// Move to the end of the buffer and create a new line (creates extra space where the progress bars can be copied to)
				Console.SetCursorPosition(0, Console.BufferHeight - 1);
				ExtendBuffer(newLines);

				// Reset the cached console values
				(
					Console.CursorTop,
					Console.CursorLeft,
					Console.CursorVisible
				) = settingsCache;

				// Calculate the position of the topmost progress bar (-1 to account for the new emtpy line that was created)
				int sourceTop = windowTop + Console.WindowHeight - ProgressBar.Slots.Count - 1;

				// Move the progress bars one line down
				Console.MoveBufferArea(0, sourceTop, ProgressBar.Slots.Max(x => x.Size), ProgressBar.Slots.Count, 0, sourceTop + newLines);
			}
			else if (Console.CursorTop >= Console.WindowHeight - ProgressBar.Slots.Count)
			{
				// Calculate the position of the topmost progress bar
				int sourceTop = windowTop + Console.WindowHeight - ProgressBar.Slots.Count;

				// Move the progress bar line one down
				Console.MoveBufferArea(0, sourceTop, ProgressBar.Slots.Max(x => x.Size), ProgressBar.Slots.Count, 0, sourceTop + 1);

				// Shift the window view downwards
				Console.WindowTop = windowTop + 1;
			}
		}

		public static void ExtendBuffer(int lines)
		{
			if (lines < 0)
				throw new ArgumentException("Value may not be less than 0. Actual value was " + lines, nameof(lines));

			// Cache some console values to reset later
			(int, int, int, bool) settingsCache = (
				Console.CursorTop - lines, // -lines to account for the newly created line
				Console.CursorLeft,
				Console.WindowTop,
				Console.CursorVisible
			);

			// Move to the end of the buffer
			Console.SetCursorPosition(0, Console.BufferHeight - 1);
			using (var sw = new StreamWriter(Console.OpenStandardOutput()))
				for (int i = 0; i < lines; i++)
					sw.WriteLine();

			// Reset the cached console values
			(
				Console.CursorTop,
				Console.CursorLeft,
				Console.WindowTop,
				Console.CursorVisible
			) = settingsCache;
		}
	}
}
