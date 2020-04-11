using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Webserver.Webserver
{
	public static class Resource
	{
		/// <summary>
		/// A list of filepaths to all resources in the wwwroot folder
		/// </summary>
		public static List<string> WebPages = Crawl(WebserverConfig.wwwroot);

		/// <summary>
		/// Processes an incoming request
		/// </summary>
		/// <param name="Context"></param>
		public static void ProcessResource(ContextProvider Context)
		{
			RequestProvider request = Context.Request;
			ResponseProvider response = Context.Response;

			//If target is '/', send index.html if it exists
			string target = WebserverConfig.wwwroot + request.Url.LocalPath.ToLower();
			if (target == WebserverConfig.wwwroot + "/" && File.Exists(WebserverConfig.wwwroot + "/index.html"))
				target += "index.html";

			//Check if the file exists. If it doesn't, send a 404.
			if (!WebPages.Contains(target) || !File.Exists(target))
			{
				Console.WriteLine($"Refused request for {target}: File not found");
				response.Send(Redirects.GetErrorPage(HttpStatusCode.NotFound), HttpStatusCode.NotFound);
				return;
			}

			//Switch to the request's HTTP method
			switch (request.HttpMethod)
			{
				case HttpMethod.GET:
					//Send the resource to the client. Content type will be set according to the resource's file extension.
					response.Send(File.ReadAllBytes(target), HttpStatusCode.OK, Path.GetExtension(target) switch
					{
						".css" => "text/css",
						".png" => "image/png",
						".js" => "text/javascript",
						".jpg" => "image/jpeg",
						".jpeg" => "image/jpeg",
						_ => "text/html"
					});
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
					Console.WriteLine("Refused request for resource " + target + ": Method Not Allowed (" + request.HttpMethod + ")");
					response.Send(Redirects.GetErrorPage(HttpStatusCode.MethodNotAllowed), HttpStatusCode.MethodNotAllowed);
					return;
			}
		}

		/// <summary>
		/// Recursively crawls through a folder and returns a list containing filepaths of the files it contains.
		/// </summary>
		/// <param name="Path">The folder to crawl through</param>
		/// <returns></returns>
		public static List<string> Crawl(string Path)
		{
			var result = new List<string>();

			//If the folder doesn't exist, create it and return an empty list.
			if (!Directory.Exists(Path))
			{
				Directory.CreateDirectory(Path);
				return result;
			}

			//Add files to list
			foreach (string Item in Directory.GetFiles(Path))
			{
				result.Add(Item.Replace('\\', '/').ToLower());
			}

			//Crawl subfolders
			foreach (string Dir in Directory.GetDirectories(Path))
			{
				result = result.Concat(Crawl(Dir)).ToList();
			}

			return result;
		}
	}
}
