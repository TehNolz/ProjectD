using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

using Webserver.Config;

namespace Webserver.Webserver
{
	public static class Redirects
	{
		public static Dictionary<string, string> RedirectDict { get; } = new Dictionary<string, string>();
		public static Dictionary<HttpStatusCode, string> ErrorPageDict { get; } = new Dictionary<HttpStatusCode, string>();

		/// <summary>
		/// Parses a redirection configuration file, registering all redirects and error pages.
		/// </summary>
		/// <remarks>
		/// Each line in the file is in the format of "source => destination" (or its a comment prefixed with //). 
		/// Source is either a HTTP error status code (4xx and 5xx) or a relative URL. Destination is always a relative URL.
		/// </remarks>
		/// <param name="redirectsFile"></param>
		public static void LoadRedirects(string redirectsFile)
		{
			//If the specified file doesn't exist, create it and return.
			if (!File.Exists(redirectsFile))
			{
				File.Create(redirectsFile);
				return;
			}

			using StreamReader reader = File.OpenText(redirectsFile);
			string line;
			int lineCount = 0;
			while ((line = reader.ReadLine()) != null)
			{
				lineCount++;
				bool isErrorPageEntry = false;

				//Ignore empty lines, comments
				if (line.Length == 0 || line.StartsWith("//"))
					continue;

				//Split the line
				string[] Split = line.Split(" => ");
				if (Split.Length != 2)
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid format", redirectsFile, lineCount);
					continue;
				}

				//Check if source is in the right format.
				if (int.TryParse(Split[0], out int result))
				{
					isErrorPageEntry = Enum.IsDefined(typeof(HttpStatusCode), result) && result >= 400 && result < 600;
				}
				else if (!Regex.IsMatch(line, @"[A-Za-z-_/]{1,}$"))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid source", redirectsFile, lineCount);
					continue;
				}

				//Check if the destination is in the correct format.
				if (!Regex.IsMatch(Split[1], @"[A-Za-z-_/]{1,}$"))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid destination", redirectsFile, lineCount);
					continue;
				}

				//Check if the source is the same as the destination
				if (Split[0] == Split[1])
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Destination same as source", redirectsFile, lineCount);
					continue;
				}

				//Check for duplicate source
				if (RedirectDict.ContainsKey(Split[0]))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Duplicate source URL", redirectsFile, lineCount);
					continue;
				}

				//Add to dict
				if (isErrorPageEntry)
				{
					ErrorPageDict.Add((HttpStatusCode)int.Parse(Split[0]), Split[1]);
				}
				else
				{
					RedirectDict.Add(Split[0], Split[1]);
				}
			}
		}

		/// <summary>
		/// Resolves an URL, returning whatever URL it redirects to.
		/// </summary>
		/// <param name="url"></param>
		/// <returns>Null if the redirection results in a loop. Otherwise returns the target redirect url.</returns>
		public static string Resolve(string url)
		{
			var resolveStack = new Stack<string>();
			while (RedirectDict.ContainsKey(url))
			{
				if (resolveStack.Contains(url))
				{
					Console.WriteLine("Redirection loop for URL: {0}", url);
					return null;
				}

				resolveStack.Push(url);
				url = RedirectDict[url];
			}
			return url;
		}

		/// <summary>
		/// Given a status code, returns the corresponding error page. If no custom page exists, a default will be returned.
		/// </summary>
		/// <param name="statusCode"></param>
		/// <returns></returns>
		public static string GetErrorPage(HttpStatusCode statusCode)
		{
			if (ErrorPageDict.TryGetValue(statusCode, out string url))
			{
				url = Resolve(url);
				if (url == null)
				{
					Console.WriteLine("Failed to get error page for statuscode {0}: Infinite loop", statusCode);
				}
			}

			if (url == null)
			{
				using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Webserver.Webserver.ErrorPage.html"));
				return reader.ReadToEnd()
					.Replace("{ERRORTEXT}", statusCode.ToString())
					.Replace("{STATUSCODE}", ((int)statusCode).ToString())
					.Replace("{MSG}", "An error occured, and the resource could not be loaded.");
			}
			else
			{
				return File.ReadAllText(WebserverConfig.WWWRoot + url);
			}
		}
	}
}
