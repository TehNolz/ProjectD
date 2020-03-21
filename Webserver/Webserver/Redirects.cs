using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Webserver.Webserver {
	public static class Redirects {
		public static readonly Dictionary<string, string> RedirectDict = new Dictionary<string, string>();
		
		/// <summary>
		/// Parses a redirection configuration file, registering all redirects.
		/// </summary>
		/// <param name="RedirectsFile"></param>
		public static void LoadRedirects(string RedirectsFile) {
			//If the specified file doesn't exist, create it and return.
			if (!File.Exists(RedirectsFile)) {
				File.Create(RedirectsFile);
				return;
			}

			using StreamReader SR = File.OpenText(RedirectsFile);
			string Line;
			int LineCount = 1;
			while ((Line = SR.ReadLine()) != null) {

				//Ignore empty lines, comments
				if (Line.Length == 0) continue;
				if (Line.StartsWith("//")) continue;

				//Check if the line is in the correct format.
				//TODO: Improve regex?
				if(!Regex.IsMatch(Line, @"[A-Za-z-_/]{1,} => [A-Za-z-_/]{1,}$")){
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Invalid format", RedirectsFile, LineCount);
					continue;
				}

				//Check if the source is the same as the destination
				string[] Split = Line.Split(" => ");
				if(Split[0] == Split[1]){
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Destination same as source", RedirectsFile, LineCount);
					continue;
				}

				//Check for duplicate source
				if(RedirectDict.ContainsKey(Split[0])){
					Console.WriteLine("Skipping invalid redirection in {0} (line: {1}): Duplicate source URL", RedirectsFile, LineCount);
					continue;
				}

				RedirectDict.Add(Split[0], Split[1]);
				LineCount++;
			}
		}

		/// <summary>
		/// Resolves an URL, returning whatever URL it redirects to.
		/// Returns null if the redirection results in a loop.
		/// </summary>
		/// <param name="URL"></param>
		public static string Resolve(string URL){
			Stack<string> ResolveStack = new Stack<string>();
			while (RedirectDict.ContainsKey(URL)) {
				if (ResolveStack.Contains(URL)) {
					Console.WriteLine("Redirection loop for URL: {0}", URL);
					return null;
				}

				ResolveStack.Push(URL);
				URL = RedirectDict[URL];
			}
			return URL;
		}
	}
}
