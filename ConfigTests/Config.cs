using System.Collections.Generic;

namespace Config.Tests
{
	[ConfigSection]
	internal static class Section1
	{
		[Comment("Hello World")]
		public static int Value = 100;

		public static List<string> Values = new List<string>() { "aaa", "bbb", "ccc" };
	}

	[ConfigSection]
	internal static class Section2
	{
		[Comment("Comment1")]
		public static string Value = "Hello World!";

		public static string Value2 = "Wheee!";
	}
}
