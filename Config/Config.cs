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
		public static int Load(string Path)
		{
			int Missing = 0;

			var ConfigFile = JObject.Parse(File.ReadAllText(Path));
			foreach (Type T in from T in Assembly.GetCallingAssembly().GetTypes() where T.GetCustomAttribute<ConfigSectionAttribute>() != null select T)
			{
				if (!ConfigFile.ContainsKey(T.Name))
				{
					Missing += T.GetFields().Length;
					continue;
				}
				var Fields = (JObject)ConfigFile[T.Name];
				foreach (FieldInfo F in T.GetFields())
				{
					if (!Fields.ContainsKey(F.Name))
					{
						Missing++;
						continue;
					}
					F.SetValue(null, Fields[F.Name].ToObject(F.FieldType));
				}
			}

			return Missing;
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
