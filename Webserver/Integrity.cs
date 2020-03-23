using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Webserver {
	class Integrity {
		/// <summary>
		/// Checks file integrity for the given directory, comparing it with the MD5 hashes stored in Checksums.json.
		/// If Checksums.json doesn't exist, this function will assume all files are OK and return 0.
		/// </summary>
		/// <param name="Dir">The directory to check</param>
		/// <param name="Recalculate">If true, Checksums.json will be ignored and new checksums will be calculated.</param>
		/// <returns>The amount of files that didn't pass the integrity check</returns>
		public static int VerifyIntegrity(string Dir, bool Recalculate = false) {
			Dictionary<string, string> Checksums = GetChecksums(Dir);

			if(File.Exists("Checksums.json") && !Recalculate) {
				//File exists. Check if the file contains an entry for the chosen directory.
				JObject SavedChecksums = JObject.Parse(File.ReadAllText("Checksums.json"));
				if(SavedChecksums.ContainsKey(Dir)) {
					//Search for differences
					//TODO: File deletions aren't detected.
					Dictionary<string, string> Saved = SavedChecksums[Dir].ToObject<Dictionary<string, string>>();
					int Diff = 0;
					foreach(KeyValuePair<string, string> Entry in Checksums) {
						if(!Saved.ContainsKey(Entry.Key) || Entry.Value != Saved[Entry.Key]) {
							Diff++;
							continue;
						}
					}
					return Diff;

				} else {
					//No entry exists. Add it.
					File.WriteAllText("Checksums.json", new JObject(){
						{ Dir, JObject.FromObject(Checksums)}
					}.ToString(Formatting.Indented));
					return 0;
				}

			} else {
				//File doesn't exist. Create it.
				File.WriteAllText("Checksums.json", new JObject(){
					{ Dir, JObject.FromObject(Checksums)}
				}.ToString(Formatting.Indented));
				return 0;
			}
		}

		/// <summary>
		/// Recursively computes the checksums for all files in this directory
		/// </summary>
		/// <param name="Dir">The directory to crawl</param>
		/// <returns>A dictionary, where the key is the filepath and the value is the MD5 checksum</returns>
		private static Dictionary<string, string> GetChecksums(string Dir) {
			Dictionary<string, string> Result = new Dictionary<string, string>();

			//If given folder doesn't exist, create it and return an empty dict
			if(!Directory.Exists(Dir)) {
				Directory.CreateDirectory(Dir);
				return new Dictionary<string, string>();
			}

			//Check files
			using MD5 md5 = MD5.Create();
			foreach(string F in Directory.GetFiles(Dir)) {
				Result.Add(F, BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(F))).Replace("-", "").ToLower());
			}

			//Recursively check subdirectories
			foreach(string D in Directory.GetDirectories(Dir)) {
				Result = Result.Concat(GetChecksums(D)).ToDictionary(x => x.Key, x => x.Value);
			}

			return Result;
		}
	}
}
