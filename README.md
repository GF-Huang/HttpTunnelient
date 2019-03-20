# HttpTunnelient
A http tunnel client.

**Example**:

```cs
using (var tunnelient = new HttpTunnelient("127.0.0.1", 6666)) {
    await tunnelient.ConnectAsync("httpbin.org", 80);

    var requestBuilder = new StringBuilder();
    requestBuilder.AppendLine("GET /ip HTTP/1.1")
                  .AppendLine("Host: httpbin.org")
                  .AppendLine();

    var bytes = Encoding.UTF8.GetBytes(requestBuilder.ToString());
    await tunnelient.WriteAsync(bytes, 0, bytes.Length);

    var buffer = new byte[2048];
    var bytesRead = await tunnelient.ReadAsync(buffer, 0, buffer.Length);
    var responses = Encoding.UTF8.GetString(buffer, 0, bytesRead);

    Console.WriteLine(responses);
    Console.ReadLine();
}
```
