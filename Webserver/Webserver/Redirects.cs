using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Webserver.Webserver {
	public static class Redirects {
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
		public static void LoadRedirects(string RedirectsFile) {
			//If the specified file doesn't exist, create it and return.
			if(!File.Exists(RedirectsFile)) {
				File.Create(RedirectsFile);
				return;
			}

			//Open redirects file for reading
			using StreamReader SR = File.OpenText(RedirectsFile);
			int LineCount = 1;
			string Line;

			//Loop through each line.
			while((Line = SR.ReadLine()) != null) {
				bool isErrorPageEntry = false;

				//Ignore empty lines, comments
				if(Line.Length == 0)
					continue;
				if(Line.StartsWith("//"))
					continue;

				//Split the line
				string[] Split = Line.Split(" => ");
				if(Split.Length != 2) {
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid format", RedirectsFile, LineCount);
					continue;
				}

				//Check if source is in the right format.
				if(int.TryParse(Split[0], out int Res)) {
					if(Enum.IsDefined(typeof(HttpStatusCode), Res) && Res >= 400 && Res < 600) {
						isErrorPageEntry = true;
					}
				} else if(!Regex.IsMatch(Line, @"[A-Za-z-_/]{1,}$")) {
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid source", RedirectsFile, LineCount);
					continue;
				}

				//Check if the destination is in the correct format.
				if(!Regex.IsMatch(Split[1], @"[A-Za-z-_/]{1,}$")) {
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid destination", RedirectsFile, LineCount);
					continue;
				}

				//Check if the source is the same as the destination
				if(Split[0] == Split[1]) {
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Destination same as source", RedirectsFile, LineCount);
					continue;
				}

				//Check for duplicate source
				if(RedirectDict.ContainsKey(Split[0])) {
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Duplicate source URL", RedirectsFile, LineCount);
					continue;
				}

				//Add to dict
				if(isErrorPageEntry) {
					ErrorPageDict.Add((HttpStatusCode)int.Parse(Split[0]), Split[1]);
				} else {
					RedirectDict.Add(Split[0], Split[1]);
				}
				LineCount++;
			}
		}

		/// <summary>
		/// Resolves an URL, returning whatever URL it redirects to.
		/// Returns null if the redirection results in a loop.
		/// </summary>
		/// <param name="URL"></param>
		public static string Resolve(string URL) {
			Stack<string> ResolveStack = new Stack<string>();
			while(RedirectDict.ContainsKey(URL)) {
				if(ResolveStack.Contains(URL)) {
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
		public static string GetErrorPage(HttpStatusCode StatusCode) {
			//Check if a custom error code is registered for this status code.
			if(ErrorPageDict.TryGetValue(StatusCode, out string Path)) {
				Path = Resolve(Path);
				if(Path == null) {
					Console.WriteLine("Failed to get error page for statuscode {0}: Infinite loop", StatusCode);
				}
			}

			//If no page was found, take the default ErrorPage.html template, fill it in, then return that instead.
			if(Path == null) {
				using StreamReader reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Webserver.Webserver.ErrorPage.html"));
				return reader.ReadToEnd()
					.Replace("{ERRORTEXT}", StatusCode.ToString())
					.Replace("{STATUSCODE}", ((int)StatusCode).ToString())
					.Replace("{MSG}", "An error occured, and the resource could not be loaded.");
			} else {
				return File.ReadAllText(WebserverConfig.wwwroot + Path);
			}
		}
	}
}
