using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Webserver
{
	class Integrity
	{
		/// <summary>
		/// Checks file integrity for the given directory, comparing it with the MD5 hashes stored in Checksums.json.
		/// If Checksums.json doesn't exist, this function will assume all files are OK and return 0.
		/// </summary>
		/// <param name="dir">The directory to check</param>
		/// <param name="recalculate">If true, Checksums.json will be ignored and new checksums will be calculated.</param>
		/// <returns>The amount of files that didn't pass the integrity check</returns>
		public static int VerifyIntegrity(string dir, bool recalculate = false)
		{
			Dictionary<string, string> checksums = GetChecksums(dir);

			if (!recalculate && File.Exists("Checksums.json"))
			{
				//File exists. Check if the file contains an entry for the chosen directory.
				var savedChecksums = JObject.Parse(File.ReadAllText("Checksums.json"));
				if (savedChecksums.ContainsKey(dir))
				{
					//Search for differences
					//TODO: File deletions aren't detected.
					var saved = savedChecksums[dir].ToObject<Dictionary<string, string>>();

					// Return the amount of differences between the checksum sets
					return checksums.Count(x => !saved.ContainsKey(x.Key) || x.Value != saved[x.Key]);
				}
				else
				{
					//No entry exists. Add it.
					File.WriteAllText("Checksums.json", new JObject() {
						{ dir, JObject.FromObject(checksums) }
					}.ToString(Formatting.Indented));
					return 0;
				}
			}
			else
			{
				//File doesn't exist. Create it.
				File.WriteAllText("Checksums.json", (new JObject(){
					{ dir, JObject.FromObject(checksums)}
				}).ToString(Formatting.Indented));
				return 0;
			}
		}

		/// <summary>
		/// Recursively computes the checksums for all files in this directory
		/// </summary>
		/// <param name="path">The directory to crawl</param>
		/// <returns>A dictionary, where the key is the filepath and the value is the MD5 checksum</returns>
		private static Dictionary<string, string> GetChecksums(string path)
		{
			var result = new Dictionary<string, string>();

			//If given folder doesn't exist, create it and return an empty dict
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
				return new Dictionary<string, string>();
			}

			//Check files
			using MD5 md5 = MD5.Create();
			foreach (string file in Directory.GetFiles(path))
				result.Add(file, BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLower());

			//Recursively check subdirectories
			foreach (string dir in Directory.GetDirectories(path))
				result = result.Concat(GetChecksums(dir)).ToDictionary(x => x.Key, x => x.Value);

			return result;
		}
	}
}
