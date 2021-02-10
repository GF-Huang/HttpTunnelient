using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HttpTunnelient {
    public sealed class HttpTunnelClient : IDisposable {
        /// <summary>
        /// The underlying TcpClient.
        /// </summary>
        public TcpClient TcpClient { get; } = new TcpClient();
        /// <summary>
        /// Proxy basic authentication. Default is null.
        /// </summary>
        public NetworkCredential? Credential { get; set; }
        /// <summary>
        /// The user-agent field that used for CONNECT request. Default is null.
        /// </summary>
        public string? UserAgent { get; set; }
        /// <summary>
        /// Get or set the Proxy-Connection field to "keep-alive" or "close". Default is true.
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        private const string MethodFieldFormat = "CONNECT {0}:{1} HTTP/1.1";
        private const string HostFieldFormat = "Host: {0}:{1}";
        private const string ProxyAuthorizationFieldFormat = "Proxy-Authorization: basic {0}";
        private const string UserAgentFieldFormat = "User-Agent: {0}";
        private const string ProxyConnectionFieldFormat = "Proxy-Connection: {0}";
        private const string ConnectionKeepAlive = "keep-alive";
        private const string ConnectionClose = "close";
        private const string HttpNewLine = "\r\n";

        private static readonly UTF8Encoding UTF8NoBom = new UTF8Encoding(false, true);

        private readonly string? _serverHost;
        private readonly IPAddress? _serverAddress;
        private readonly int _serverPort;
        private NetworkStream? _stream;
        private Status _status = Status.Initial;

        public HttpTunnelClient(string host, int port) {
            if (host is null)
                throw new ArgumentNullException(nameof(host));

            if (IPAddress.TryParse(host, out var address))
                _serverAddress = address;
            else
                _serverHost = host;

            _serverPort = port;
        }

        public HttpTunnelClient(IPAddress address, int port) {
            _serverAddress = address ?? throw new ArgumentNullException(nameof(address));
            _serverPort = port;
        }

        public void Dispose() {
            if (_status != Status.Closed) {
                TcpClient.Close();

                _status = Status.Closed;
            }
        }

        public async Task ConnectAsync(string domain, int port) {
            if (domain is null)
                throw new ArgumentNullException(nameof(domain));

            await ConnectAsync(domain: domain, address: null, port: port);

            _status = Status.Connected;
        }

        public async Task ConnectAsync(IPAddress destAddress, int port) {
            if (destAddress is null)
                throw new ArgumentNullException(nameof(destAddress));

            await ConnectAsync(domain: null, address: destAddress, port: port);

            _status = Status.Connected;
        }

        public NetworkStream GetStream() {
            if (_status == Status.Closed)
                throw new ObjectDisposedException(GetType().FullName);
            if (_status != Status.Connected)
                throw new InvalidOperationException($"{GetType().FullName} is not connected.");

            return _stream!;
        }

        private async Task ConnectAsync(string? domain, IPAddress? address, int port) {
            if (_serverHost != null)
                await TcpClient.ConnectAsync(_serverHost, _serverPort);
            else
                await TcpClient.ConnectAsync(_serverAddress, _serverPort);

            _stream = TcpClient.GetStream();

            using var writer = new StreamWriter(_stream, UTF8NoBom, 1024, true) { NewLine = HttpNewLine };
            using var reader = new StreamReader(_stream, UTF8NoBom, true, 1024, true);

            var destination = domain ?? address!.ToString();

            await writer.WriteLineAsync(string.Format(MethodFieldFormat, destination, port));
            await writer.WriteLineAsync(string.Format(HostFieldFormat, destination, port));

            if (Credential != null) {
                var authorization = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Credential.UserName}:{Credential.Password}"));
                await writer.WriteLineAsync(string.Format(ProxyAuthorizationFieldFormat, authorization));
            }

            if (!string.IsNullOrWhiteSpace(UserAgent))
                await writer.WriteLineAsync(string.Format(UserAgentFieldFormat, UserAgent));

            await writer.WriteLineAsync(string.Format(ProxyConnectionFieldFormat, KeepAlive ? ConnectionKeepAlive : ConnectionClose));
            await writer.WriteLineAsync();

            await writer.FlushAsync();

            // response
            var response = await reader.ReadLineAsync();

            var match = Regex.Match(response, @"HTTP/1\.(?:1|0) (\d{3}) ([\w\s]+)");
            if (match.Success) {
                if ((HttpStatusCode)Convert.ToInt32(match.Groups[1].Value) != HttpStatusCode.OK) {
                    var code = (HttpStatusCode)Convert.ToInt32(match.Groups[1].Value);

                    throw new HttpsProxyException(code, $"{code:D} {match.Groups[2].Value}.");
                }

            } else
                throw new WebException("Unknown protocol responsed.", WebExceptionStatus.ServerProtocolViolation);
        }
    }

    [Serializable]
    public class HttpsProxyException : Exception {
        public HttpStatusCode StatusCode { get; }

        public HttpsProxyException(HttpStatusCode statusCode) { StatusCode = statusCode; }
        public HttpsProxyException(HttpStatusCode statusCode, string message) : base(message) { StatusCode = statusCode; }
        public HttpsProxyException(HttpStatusCode statusCode, string message, Exception inner) : base(message, inner) { StatusCode = statusCode; }
        protected HttpsProxyException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }

    enum Status {
        /// <summary>
        /// Before the tunnel connection initialized.
        /// </summary>
        Initial,
        /// <summary>
        /// After tunnel connected.
        /// </summary>
        Connected,
        /// <summary>
        /// Tunnel connection closed, can not reuse.
        /// </summary>
        Closed
    }
}
