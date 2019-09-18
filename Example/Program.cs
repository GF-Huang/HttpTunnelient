using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HttpTunnelientDotNet;

namespace Example {
    class Program {
        static async Task Main(string[] args) {
            using (var tunnelient = new HttpTunnelient("127.0.0.1", 6666)) {
                await tunnelient.ConnectAsync("httpbin.org", 80);

                var requestBuilder = new StringBuilder();
                requestBuilder.AppendLine("GET /ip HTTP/1.1")
                              .AppendLine("Host: httpbin.org")
                              .AppendLine();

                var bytes = Encoding.UTF8.GetBytes(requestBuilder.ToString());
                await tunnelient.GetStream().WriteAsync(bytes, 0, bytes.Length);

                var buffer = new byte[2048];
                var bytesRead = await tunnelient.GetStream().ReadAsync(buffer, 0, buffer.Length);
                var responses = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                Console.WriteLine(responses);
                Console.ReadLine();
            }
        }
    }
}
