using MimeKit;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

using Webserver.Config;

namespace Webserver.Webserver
{
	public static class Resource
	{
		/// <summary>
		/// A list of filepaths to all resources in the wwwroot folder
		/// </summary>
		public static List<string> WebPages = Crawl(WebserverConfig.WWWRoot);

		/// <summary>
		/// Processes an incoming request
		/// </summary>
		/// <param name="context">A provider that provides a context. (AKA TODO add documentation)</param>
		public static void ProcessResource(ContextProvider context)
		{
			RequestProvider request = context.Request;
			ResponseProvider response = context.Response;

			//If target is '/', send index.html if it exists
			string target = WebserverConfig.WWWRoot + request.Url.LocalPath.ToLower();
			if (target == WebserverConfig.WWWRoot + "/" && File.Exists(WebserverConfig.WWWRoot + "/index.html"))
				target += "index.html";

			//Check if the file exists. If it doesn't, send a 404.
			if (!WebPages.Contains(target) || !File.Exists(target))
			{
				Program.Log.Warning($"Refused request for {target}: File not found");
				response.Send(Redirects.GetErrorPage(HttpStatusCode.NotFound), HttpStatusCode.NotFound);
				return;
			}

			//Switch to the request's HTTP method
			switch (request.HttpMethod)
			{
				case HttpMethod.GET:
					//Send the resource to the client. Content type will be set according to the resource's file extension.
					string contentType = MimeTypes.GetMimeType(Path.GetExtension(target));
					response.Send(File.ReadAllBytes(target), HttpStatusCode.OK, contentType);
					return;

				case HttpMethod.HEAD:
					//A HEAD request is the same as GET, except without the body. Since the resource exists, we can just send back a 200 OK and call it a day.
					response.Send(HttpStatusCode.OK);
					return;

				case HttpMethod.OPTIONS:
					//TODO: CORS support
					response.Send(HttpStatusCode.OK);
					return;

				default:
					//Resources only support the three methods defined above, so send back a 405 Method Not Allowed.
					Program.Log.Warning("Refused request for resource " + target + ": Method Not Allowed (" + request.HttpMethod + ")");
					response.Send(Redirects.GetErrorPage(HttpStatusCode.MethodNotAllowed), HttpStatusCode.MethodNotAllowed);
					return;
			}
		}

		/// <summary>
		/// Recursively returns a list of paths of the files under the given <paramref name="path"/>.
		/// </summary>
		/// <param name="path">The path whose files to recursively find and return.</param>
		public static List<string> Crawl(string path)
		{
			var result = new List<string>();

			//If the folder doesn't exist, create it and return an empty list.
			if (!Directory.Exists(path))
			{
				Directory.CreateDirectory(path);
				return result;
			}

			//Add files to list
			result.AddRange(from string item in Directory.GetFiles(path)
							select item.Replace('\\', '/').ToLower());

			//Crawl subfolders
			foreach (string dir in Directory.GetDirectories(path))
				result.AddRange(Crawl(dir));

			return result;
		}
	}
}
