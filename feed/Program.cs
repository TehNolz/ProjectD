using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;

namespace feed
{
    class Program
    {
        static void Main(string[] args)
        {
            string[] x = new string[1] { "http://localhost:8080/" };
            SimpleListenerExample(x);
        }

        public static void SimpleListenerExample(string[] prefixes)
        {
            if (!HttpListener.IsSupported)
            {
                Console.WriteLine("Windows XP SP2 or Server 2003 is required to use the HttpListener class.");
                return;
            }

            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");

            // Create a listener.
            HttpListener listener = new HttpListener();
            // Add the prefixes.
            foreach (string prefix in prefixes)
            {
                listener.Prefixes.Add(prefix);
            }
            listener.Start();
            Console.WriteLine("Listening...");

            HttpListenerContext context = listener.GetContext();
            HttpListenerRequest request = context.Request;
            ShowRequestData(request);

            // Obtain a response object.
            HttpListenerResponse response = context.Response;
            // Construct a response.
            string responseString = "<HTML><BODY> Hello world!</BODY></HTML>";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            // Get a response stream and write the response to it.
            response.ContentLength64 = buffer.Length;
            System.IO.Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);

            output.Close();

            listener.Stop();
        }

        public static void ShowRequestData(HttpListenerRequest request)
        {
            if (!request.HasEntityBody)
            {
                Console.WriteLine("No client data was sent with the request.");
                return;
            }
            System.IO.Stream body = request.InputStream;
            System.Text.Encoding encoding = request.ContentEncoding;
            System.IO.StreamReader reader = new System.IO.StreamReader(body, encoding);
            if (request.ContentType != null)
            {
                Console.WriteLine("Client data content type {0}", request.ContentType);
            }
            Console.WriteLine("Client data content length {0}", request.ContentLength64);

            Console.WriteLine("Start of client data:");
            // Convert the data to a string and display it on the console.
            string s = reader.ReadToEnd();
            Console.WriteLine(s);
            Console.WriteLine("End of client data:");
            body.Close();
            reader.Close();
        }

    }


}
