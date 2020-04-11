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
		/// <param name="directory">The directory to check</param>
		/// <param name="recalculate">If true, Checksums.json will be ignored and new checksums will be calculated.</param>
		/// <returns>The amount of files that didn't pass the integrity check</returns>
		public static int VerifyIntegrity(string directory, bool recalculate = false)
		{
			Dictionary<string, string> Checksums = GetChecksums(directory);

			if (File.Exists("Checksums.json") && !recalculate)
			{
				//File exists. Check if the file contains an entry for the chosen directory.
				var savedChecksumsJson = JObject.Parse(File.ReadAllText("Checksums.json"));
				if (savedChecksumsJson.ContainsKey(directory))
				{
					//Search for differences
					//TODO: File deletions aren't detected.
					Dictionary<string, string> savedChecksums = savedChecksumsJson[directory].ToObject<Dictionary<string, string>>();
					int diffCount = 0;
					foreach (KeyValuePair<string, string> Entry in Checksums)
					{
						if (!savedChecksums.ContainsKey(Entry.Key) || Entry.Value != savedChecksums[Entry.Key])
						{
							diffCount++;
							continue;
						}
					}
					return diffCount;

				}
				else
				{
					//No entry exists. Add it.
					File.WriteAllText("Checksums.json", new JObject(){
						{ directory, JObject.FromObject(Checksums)}
					}.ToString(Formatting.Indented));
					return 0;
				}

			}
			else
			{
				//File doesn't exist. Create it.
				File.WriteAllText("Checksums.json", new JObject(){
					{ directory, JObject.FromObject(Checksums)}
				}.ToString(Formatting.Indented));
				return 0;
			}
		}

		/// <summary>
		/// Recursively computes the checksums for all files in this directory
		/// </summary>
		/// <param name="Dir">The directory to crawl</param>
		/// <returns>A dictionary, where the key is the filepath and the value is the MD5 checksum</returns>
		private static Dictionary<string, string> GetChecksums(string Dir)
		{
			var result = new Dictionary<string, string>();

			//If given folder doesn't exist, create it and return an empty dict
			if (!Directory.Exists(Dir))
			{
				Directory.CreateDirectory(Dir);
				return new Dictionary<string, string>();
			}

			//Check files
			using var md5 = MD5.Create();
			foreach (string filename in Directory.GetFiles(Dir))
			{
				result.Add(filename, BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(filename))).Replace("-", "").ToLower());
			}

			//Recursively check subdirectories
			foreach (string directory in Directory.GetDirectories(Dir))
			{
				result = result.Concat(GetChecksums(directory)).ToDictionary(x => x.Key, x => x.Value);
			}

			return result;
		}
	}
}
