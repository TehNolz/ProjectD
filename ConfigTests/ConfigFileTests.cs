using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Config.Tests
{
	[TestClass()]
	public class ConfigFileTests
	{
		public const string Filename = "Config.json";

		/// <summary>
		/// Tests a basic export of the data in Config.cs by writing to a file, then checking if
		/// the contents of the created file are as expected.
		/// </summary>
		[TestMethod]
		public void BasicWrite()
		{
			//Write the config settings to file
			ConfigFile.Write(Filename);

			//Check if the file was actually made
			Assert.IsTrue(File.Exists(Filename));

			//Check if the file was written properly.
			string[] fileContents = File.ReadAllLines(Filename);
			var json = JObject.Parse(string.Join('\n', fileContents));

			//Check if both comments were written properly.
			Assert.IsTrue(fileContents[2] == "/*Hello World*/");
			Assert.IsTrue(fileContents[11] == "/*Comment1*/");

			//Check if all keys are present
			Assert.IsTrue(json.ContainsKey("Section1"));
			Assert.IsTrue(json.ContainsKey("Section2"));

			//Check if Section 1's key/values are correct
			Assert.IsTrue(((JObject)json["Section1"]).ContainsKey("Value"));
			Assert.IsTrue((int)json["Section1"]["Value"] == 100);
			Assert.IsTrue(((JObject)json["Section1"]).ContainsKey("Values"));
			List<string> values = ((JArray)json["Section1"]["Values"]).ToObject<List<string>>();
			Assert.IsTrue(values.Count == 3);
			Assert.IsTrue(values[0] == "aaa" && values[1] == "bbb" && values[2] == "ccc");

			//Check if Section 2's key/values are correct
			Assert.IsTrue(((JObject)json["Section2"]).ContainsKey("Value"));
			Assert.IsTrue((string)json["Section2"]["Value"] == "Hello World!");
			Assert.IsTrue(((JObject)json["Section2"]).ContainsKey("Value2"));
			Assert.IsTrue((string)json["Section2"]["Value2"] == "Wheee!");
		}

		/// <summary>
		/// Tests an export of the data in Config.cs where the data has been modified before the write.
		/// </summary>
		[TestMethod]
		public void ModifiedWrite()
		{
			//Change some values in Config.cs
			Section1.Value = 200;
			Section2.Value = "Whoa!";

			//Write these values to disk
			ConfigFile.Write(Filename);

			//Check if the file was written properly.
			string[] fileContents = File.ReadAllLines(Filename);
			var json = JObject.Parse(string.Join('\n', fileContents));

			//Check if the modified values were written properly
			Assert.IsTrue(((JObject)json["Section1"]).ContainsKey("Value"));
			Assert.IsTrue((int)json["Section1"]["Value"] == 200);
			Assert.IsTrue(((JObject)json["Section2"]).ContainsKey("Value"));
			Assert.IsTrue((string)json["Section2"]["Value"] == "Whoa!");
		}

		/// <summary>
		/// Tests a basic read of data by attempting to overwrite to config values.
		/// </summary>
		[TestMethod]
		public void BasicLoad()
		{
			//Create a config file with the default values
			ConfigFile.Write(Filename);

			//Change some values in Config.cs
			Section1.Value = 200;
			Section2.Value = "Whoa!";

			//Load the file. This should overwrite the above changes and set them to default again.
			//The amount of missing settings should be 0.
			Assert.IsTrue(ConfigFile.Load(Filename) == 0);

			//Check if the values were overwritten.
			Assert.IsTrue(Section1.Value == 100);
			Assert.IsTrue(Section2.Value == "Hello World!");
			Assert.IsTrue(Section2.Value2 == "Wheee!");
		}

		/// <summary>
		/// Tests a read where parts of the data in Config.json are missing.
		/// </summary>
		[TestMethod]
		public void MissingLoad()
		{
			//Create a config file with modified values
			Section1.Value = 200;
			Section2.Value = "Whoa!";
			ConfigFile.Write(Filename);

			//Set the two values back to their defaults.
			Section1.Value = 100;
			Section2.Value = "Hello World!";

			//Delete Section1.Values and all of Section 2 from the config file.
			//This will result in 3 missing settings.
			var fileContent = File.ReadLines(Filename).ToList();
			fileContent.RemoveRange(4, 5); //Section1.Values
			fileContent.RemoveRange(5, 5); //Section2
			File.WriteAllLines(Filename, fileContent);

			//Load the file and check if the correct amount of missing fields were returned
			Assert.IsTrue(ConfigFile.Load(Filename) == 3);

			//Check if all values are as expected. Section2.Value should be the default again. Missing values should be set to their defaults.
			Assert.IsTrue(Section1.Value == 200);
			Assert.IsTrue(Section2.Value == "Hello World!");
			Assert.IsTrue(Section2.Value2 == "Wheee!");
			Assert.IsTrue(Section1.Values.Count == 3);
			Assert.IsTrue(Section1.Values[0] == "aaa" && Section1.Values[1] == "bbb" && Section1.Values[2] == "ccc");
		}

		[TestCleanup]
		public void Cleanup()
		{
			//Delete the resulting file if it still exists.
			if (File.Exists(Filename))
				File.Delete(Filename);
		}
	}
}