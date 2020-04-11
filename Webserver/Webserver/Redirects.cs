using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Webserver.Webserver
{
	public static class Redirects
	{
		/// <summary>
		/// Contains all redirection mappings. Keys are source URLs, values are the destinations they map to.
		/// </summary>
		public static readonly Dictionary<string, string> RedirectDict = new Dictionary<string, string>();
		/// <summary>
		/// Contains all error page mappings. Key is the HTTP status codes, value is the destination they map to.
		/// </summary>
		public static readonly Dictionary<HttpStatusCode, string> ErrorPageDict = new Dictionary<HttpStatusCode, string>();

		/// <summary>
		/// Parses a redirection configuration file, registering all redirects and error pages.
		/// Each line in the file is in the format of "source => destination" (or its a comment prefixed with //). 
		/// Source is either a HTTP error status code (4xx and 5xx) or a relative URL. Destination is always a relative URL.
		/// </summary>
		/// <param name="RedirectsFile">The path to the config file.</param>
		public static void LoadRedirects(string RedirectsFile)
		{
			//If the specified file doesn't exist, create it and return.
			if (!File.Exists(RedirectsFile))
			{
				File.Create(RedirectsFile);
				return;
			}

			//Open redirects file for reading
			using StreamReader SR = File.OpenText(RedirectsFile);
			int lineCount = 1;
			string line;

			//Loop through each line.
			while ((line = SR.ReadLine()) != null)
			{
				bool isErrorPageEntry = false;

				//Ignore empty lines, comments
				if (line.Length == 0)
					continue;
				if (line.StartsWith("//"))
					continue;

				//Split the line
				string[] split = line.Split(" => ");
				if (split.Length != 2)
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid format", RedirectsFile, lineCount);
					continue;
				}

				//Check if source is in the right format.
				if (int.TryParse(split[0], out int res))
				{
					if (Enum.IsDefined(typeof(HttpStatusCode), res) && res >= 400 && res < 600)
					{
						isErrorPageEntry = true;
					}
				}
				else if (!Regex.IsMatch(line, @"[A-Za-z-_/]{1,}$"))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid source", RedirectsFile, lineCount);
					continue;
				}

				//Check if the destination is in the correct format.
				if (!Regex.IsMatch(split[1], @"[A-Za-z-_/]{1,}$"))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid destination", RedirectsFile, lineCount);
					continue;
				}

				//Check if the source is the same as the destination
				if (split[0] == split[1])
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Destination same as source", RedirectsFile, lineCount);
					continue;
				}

				//Check for duplicate source
				if (RedirectDict.ContainsKey(split[0]))
				{
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Duplicate source URL", RedirectsFile, lineCount);
					continue;
				}

				//Add to dict
				if (isErrorPageEntry)
				{
					ErrorPageDict.Add((HttpStatusCode)int.Parse(split[0]), split[1]);
				}
				else
				{
					RedirectDict.Add(split[0], split[1]);
				}
				lineCount++;
			}
		}

		/// <summary>
		/// Resolves an URL, returning whatever URL it redirects to.
		/// Returns null if the redirection results in a loop.
		/// </summary>
		/// <param name="URL"></param>
		public static string Resolve(string URL)
		{
			var ResolveStack = new Stack<string>();
			while (RedirectDict.ContainsKey(URL))
			{
				if (ResolveStack.Contains(URL))
				{
					Console.WriteLine("Redirection loop for URL: {0}", URL);
					return null;
				}

				ResolveStack.Push(URL);
				URL = RedirectDict[URL];
			}
			return URL;
		}

		/// <summary>
		/// Given a status code, returns the corresponding error page. If no custom page exists, a default will be returned.
		/// </summary>
		/// <param name="StatusCode"></param>
		/// <returns></returns>
		public static string GetErrorPage(HttpStatusCode StatusCode)
		{
			//Check if a custom error code is registered for this status code.
			if (ErrorPageDict.TryGetValue(StatusCode, out string Path))
			{
				Path = Resolve(Path);
				if (Path == null)
				{
					Console.WriteLine("Failed to get error page for statuscode {0}: Infinite loop", StatusCode);
				}
			}

			//If no page was found, take the default ErrorPage.html template, fill it in, then return that instead.
			if (Path == null)
			{
				using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Webserver.Webserver.ErrorPage.html"));
				return reader.ReadToEnd()
					.Replace("{ERRORTEXT}", StatusCode.ToString())
					.Replace("{STATUSCODE}", ((int)StatusCode).ToString())
					.Replace("{MSG}", "An error occured, and the resource could not be loaded.");
			}
			else
			{
				return File.ReadAllText(WebserverConfig.wwwroot + Path);
			}
		}
	}
}
