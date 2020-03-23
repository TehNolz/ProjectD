using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Config {
	public static class ConfigFile {

		public static void Write(string Path) {
			using StringWriter SW = new StringWriter();
			using JsonTextWriter Writer = new JsonTextWriter(SW) {
				Formatting = Formatting.Indented,
			};

			Writer.WriteStartObject();
			//Go through all ConfigSection classes
			foreach(Type T in from T in Assembly.GetCallingAssembly().GetTypes() where T.GetCustomAttribute<ConfigSectionAttribute>() != null select T) {
				Writer.WritePropertyName(T.Name);
				Writer.WriteStartObject();

				//Get all fields
				foreach(FieldInfo F in T.GetFields()) {
					CommentAttribute Attr = F.GetCustomAttribute<CommentAttribute>();
					if(Attr != null) {
						Writer.WriteWhitespace("\n");
						Writer.WriteComment(Attr.Comment);
					}

					//Write field
					Writer.WritePropertyName(F.Name);
					//If the field is a list, write it as an array.
					if(F.FieldType.IsGenericType && F.FieldType.GetGenericTypeDefinition() == typeof(List<>)) {
						Writer.WriteStartArray();
						IList L = (IList)F.GetValue(null);
						foreach(object Entry in L) {
							Writer.WriteValue(Entry);
						}
						Writer.WriteEndArray();
					} else {
						Writer.WriteValue(F.GetValue(null));
					}
				}
				Writer.WriteEndObject();
			}
			Writer.WriteEndObject();

			File.WriteAllText(Path, SW.ToString());
		}

		public static int Load(string Path) {
			int Missing = 0;

			JObject ConfigFile = JObject.Parse(File.ReadAllText(Path));
			foreach(Type T in from T in Assembly.GetCallingAssembly().GetTypes() where T.GetCustomAttribute<ConfigSectionAttribute>() != null select T) {
				if(!ConfigFile.ContainsKey(T.Name)) {
					Missing += T.GetFields().Length;
					continue;
				}
				JObject Fields = (JObject)ConfigFile[T.Name];
				foreach(FieldInfo F in T.GetFields()) {
					if(!Fields.ContainsKey(F.Name)) {
						Missing++;
						continue;
					}
					F.SetValue(null, Fields[F.Name].ToObject(F.FieldType));
				}
			}

			return Missing;
		}
	}

	[AttributeUsage(AttributeTargets.Class)]
	public class ConfigSectionAttribute : Attribute { }

	[AttributeUsage(AttributeTargets.Field)]
	public class CommentAttribute : Attribute {
		public readonly string Comment;
		public CommentAttribute(string Comment) => this.Comment = Comment;
	}
}
