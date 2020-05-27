using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Config
{
	public static class ConfigFile
	{
		/// <summary>
		/// Write a configuration file to disk, using the current settings.
		/// </summary>
		/// <param name="Path">The path the file will be written to. Existing files will be overwritten.</param>
		public static void Write(string Path)
		{
			//Create writers.
			using var SW = new StringWriter();
			using var writer = new JsonTextWriter(SW)
			{
				Formatting = Formatting.Indented,
			};

			writer.WriteStartObject();
			//Go through all ConfigSection classes
			foreach (Type T in from T in Assembly.GetCallingAssembly().GetTypes() where T.GetCustomAttribute<ConfigSectionAttribute>() != null select T)
			{
				writer.WritePropertyName(T.Name);
				writer.WriteStartObject();

				//Get all fields
				foreach (FieldInfo F in T.GetFields())
				{
					CommentAttribute Attr = F.GetCustomAttribute<CommentAttribute>();
					if (Attr != null)
					{
						writer.WriteWhitespace("\n");
						writer.WriteComment(Attr.Comment);
					}

					//Write fields
					writer.WritePropertyName(F.Name);
					//If the field is a list, write it as an array.
					if (F.FieldType.IsGenericType && F.FieldType.GetGenericTypeDefinition() == typeof(List<>))
					{
						writer.WriteStartArray();
						var L = (IList)F.GetValue(null);
						foreach (object Entry in L)
						{
							writer.WriteValue(Entry);
						}
						writer.WriteEndArray();
					}
					else
					{
						writer.WriteValue(F.GetValue(null));
					}
				}
				writer.WriteEndObject();
			}
			writer.WriteEndObject();

			File.WriteAllText(Path, SW.ToString());
		}

		/// <summary>
		/// Load the configuration file at the specified path. The amount of missing values will be returned.
		/// </summary>
		/// <param name="Path">The path of the configuration file to load.</param>
		/// <returns>The amount of missing values.</returns>
		/// <exception cref="FileNotFoundException">No file found at the given <paramref name="Path"/>.</exception>
		/// <exception cref="JsonReaderException">The file at the given <paramref name="Path"/> is not a valid JSON file.</exception>
		public static int Load(string Path)
		{
			// Load the given config json
			var configJson = JObject.Parse(File.ReadAllText(Path));
			return Load(configJson);
		}
		/// <summary>
		/// Load the given <paramref name="configJson"/> into the <see cref="ConfigSectionAttribute"/> classes.
		/// </summary>
		/// <param name="configJson">The <see cref="JObject"/> to load.</param>
		/// <returns>The amount of missing values.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="configJson"/> is <see langword="null"/>.</exception>
		public static int Load(JObject configJson)
		{
			if (configJson is null)
				throw new ArgumentNullException(nameof(configJson));

			int missing = 0;

			// Loop through all types from the caller's assembly which have the ConfigSectionAttribute
			foreach (Type configSection in Assembly.GetCallingAssembly().GetTypes().Where(x => x.GetCustomAttribute<ConfigSectionAttribute>() != null))
			{
				if (!configJson.ContainsKey(configSection.Name))
				{
					// If a section is missing, add the sections amount of fields to `missing`
					missing += configSection.GetFields().Length;
					continue;
				}

				// Get the config section and look for missing keys in said section
				var section = (JObject)configJson[configSection.Name];
				foreach (FieldInfo field in configSection.GetFields())
				{
					// Increment `missing` if a field is missing
					if (!section.ContainsKey(field.Name))
					{
						missing++;
						continue;
					}
					// Convert the JValue to the field's type and set the field. This also serves as a typecheck.
					field.SetValue(null, section[field.Name].ToObject(field.FieldType));
				}
			}

			return missing;
		}
	}

	/// <summary>
	/// Marks a class as a configuration file section.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	public class ConfigSectionAttribute : Attribute { }

	/// <summary>
	/// Adds a comment to a configuration value.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field)]
	public class CommentAttribute : Attribute
	{
		public readonly string Comment;
		public CommentAttribute(string Comment)
		{
			this.Comment = Comment;
		}
	}
}
