using System;
using System.Collections.Generic;
using System.Text;

namespace Webserver.Webserver {
	public static class Resource {
		public static void ProcessResource(ContextProvider Context){
			RequestProvider Request = Context.Request;
			ResponseProvider Response = Context.Response;

			Response.Send("Hello world!", System.Net.HttpStatusCode.OK, "text/plain");
		}
	}
}
