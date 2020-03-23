using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Webserver.Webserver {
	public static class Resource {
		/// <summary>
		/// A list of filepaths to all resources in the wwwroot folder
		/// </summary>
		public static List<string> WebPages = Crawl(WebserverConfig.wwwroot);

		/// <summary>
		/// Processes an incoming request
		/// </summary>
		/// <param name="Context"></param>
		public static void ProcessResource(ContextProvider Context) {
			RequestProvider Request = Context.Request;
			ResponseProvider Response = Context.Response;

			//If target is '/', send index.html if it exists
			string Target = WebserverConfig.wwwroot + Request.Url.LocalPath.ToLower();
			if(Target == WebserverConfig.wwwroot + "/" && File.Exists(WebserverConfig.wwwroot + "/index.html"))
				Target += "index.html";

			//Check if the file exists. If it doesn't, send a 404.
			if(!WebPages.Contains(Target) || !File.Exists(Target)) {
				Console.WriteLine($"Refused request for {Target}: File not found");
				Response.Send(Redirects.GetErrorPage(HttpStatusCode.NotFound), HttpStatusCode.NotFound);
				return;
			}

			//Switch to the request's HTTP method
			switch(Request.HttpMethod) {
				case HttpMethod.GET:
					//Send the resource to the client. Content type will be set according to the resource's file extension.
					Response.Send(File.ReadAllBytes(Target), HttpStatusCode.OK, Path.GetExtension(Target) switch
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
					Response.Send(HttpStatusCode.OK);
					return;

				case HttpMethod.OPTIONS:
					//TODO: CORS support
					Response.Send(HttpStatusCode.OK);
					return;

				default:
					//Resources only support the three methods defined above, so send back a 405 Method Not Allowed.
					Console.WriteLine("Refused request for resource " + Target + ": Method Not Allowed (" + Request.HttpMethod + ")");
					Response.Send(Redirects.GetErrorPage(HttpStatusCode.MethodNotAllowed), HttpStatusCode.MethodNotAllowed);
					return;
			}
		}

		/// <summary>
		/// Crawls through a folder and returns a list containing filepaths of the files it contains.
		/// </summary>
		/// <param name="Path">The folder to crawl through</param>
		/// <returns></returns>
		public static List<string> Crawl(string Path) {
			List<string> Result = new List<string>();

			//If the folder doesn't exist, create it and return an empty list.
			if(!Directory.Exists(Path)) {
				Directory.CreateDirectory(Path);
				return Result;
			}

			//Add files to list
			foreach(string Item in Directory.GetFiles(Path)) {
				Result.Add(Item.Replace('\\', '/').ToLower());
			}

			//Crawl subfolders
			foreach(string Dir in Directory.GetDirectories(Path)) {
				Result = Result.Concat(Crawl(Dir)).ToList();
			}

			return Result;
		}
	}
}
