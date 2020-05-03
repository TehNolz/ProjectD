using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Webserver
{
	public static partial class Utils
	{
		/// <summary>
		/// Returns all properties from the specified type.
		/// </summary>
		/// <param name="type">The type whose properties to return.</param>
		/// <remarks>
		/// Virtual properties are ignored.
		/// </remarks>
		public static IEnumerable<PropertyInfo> GetProperties(Type type)
			=> type.GetProperties().Where(x => !(x.GetGetMethod()?.IsVirtual ?? false) && !(x.GetSetMethod()?.IsVirtual ?? false));
	}
}
