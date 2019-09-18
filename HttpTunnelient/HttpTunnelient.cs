using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HttpTunnelientDotNet {
    public class HttpTunnelient : IDisposable {
        /// <summary>
        /// The underlying TcpClient.
        /// </summary>
        public TcpClient TCP { get; private set; }

        /// <summary>
        /// The status of current instance.
        /// </summary>
        public TunnelStatus Status { get; private set; } = TunnelStatus.Initial;

        /// <summary>
        /// Proxy basic authentication. Default is null.
        /// </summary>
        public NetworkCredential Credential { get; set; }

        /// <summary>
        /// The user-agent field that used for CONNECT request. Default is null.
        /// </summary>
        public string UserAgent { get; set; }

        /// <summary>
        /// Get or set the Proxy-Connection field to "keep-alive" or "close". Default is true.
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        const string MethodFieldFormat = "CONNECT {0}:{1} HTTP/1.1";
        const string HostFieldFormat = "Host: {0}:{1}";
        const string ProxyAuthorizationFieldFormat = "Proxy-Authorization: basic {0}";
        const string UserAgentFieldFormat = "User-Agent: {0}";
        const string ProxyConnectionFieldFormat = "Proxy-Connection: {0}";
        const string ConnectionKeepAlive = "keep-alive";
        const string ConnectionClose = "close";
        const string HttpNewLine = "\r\n";

        private static UTF8Encoding UTF8NoBom = new UTF8Encoding(false, true);

        private string _serverHost;
        private int _serverPort;
        private IPEndPoint _serverEndPoint;
        private bool _disposed;
        private NetworkStream _stream;

        public HttpTunnelient(string serverHost, int port) {
            if (IPAddress.TryParse(serverHost, out var address)) {
                _serverEndPoint = new IPEndPoint(address, port);
                TCP = new TcpClient(address.AddressFamily);

            } else {
                _serverHost = serverHost;
                _serverPort = port;
                TCP = new TcpClient();
            }
        }

        public HttpTunnelient(IPAddress serverAddress, int port) : this(new IPEndPoint(serverAddress, port)) { }

        public HttpTunnelient(IPEndPoint server) {
            _serverEndPoint = server;
            TCP = new TcpClient(server.AddressFamily);
        }

        public void Dispose() {
            if (!_disposed) {
                TCP?.Close();
                TCP = null;

                Status = TunnelStatus.Closed;

                _disposed = true;
            }
        }

        public void Connect(string destHost, int destPort) {
            ConnectServer();

            RequestConnect(destHost: destHost, destAddress: null, destPort: destPort);

            Status = TunnelStatus.Connected;
        }

        public void Connect(IPAddress destAddress, int destPort) {
            ConnectServer();

            RequestConnect(destHost: null, destAddress: destAddress, destPort: destPort);

            Status = TunnelStatus.Connected;
        }

        public async Task ConnectAsync(string destHost, int destPort) {
            await ConnectServerAsync();

            await RequestConnectAsync(destHost: destHost, destAddress: null, destPort: destPort);

            Status = TunnelStatus.Connected;
        }

        public async Task ConnectAsync(IPAddress destAddress, int destPort) {
            await ConnectServerAsync();

            await RequestConnectAsync(destHost: null, destAddress: destAddress, destPort: destPort);

            Status = TunnelStatus.Connected;
        }

        public NetworkStream GetStream() {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);
            if (Status != TunnelStatus.Connected)
                throw new InvalidOperationException("HttpTunnelient not yet connceted.");

            return TCP.GetStream();
        }

        #region Obsolete

        private const string UseGetStreamInstead = "Use GetStream() instead to do read/write operation.";

        [Obsolete(UseGetStreamInstead)]
        public void Write(byte[] buffer, int offset, int size) => _stream.Write(buffer, offset, size);

        [Obsolete(UseGetStreamInstead)]
        public int Read(byte[] buffer, int offset, int size) => _stream.Read(buffer, offset, size);

        [Obsolete(UseGetStreamInstead)]
        public Task WriteAsync(byte[] buffer, int offset, int count) =>
            _stream.WriteAsync(buffer, offset, count);

        [Obsolete(UseGetStreamInstead)]
        public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            _stream.WriteAsync(buffer, offset, count, token);

        [Obsolete(UseGetStreamInstead)]
        public Task<int> ReadAsync(byte[] buffer, int offset, int count) =>
            _stream.ReadAsync(buffer, offset, count);

        [Obsolete(UseGetStreamInstead)]
        public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            _stream.ReadAsync(buffer, offset, count, token);

        #endregion

        private void ConnectServer() {
            if (_serverEndPoint != null)
                TCP.Connect(_serverEndPoint);
            else
                TCP.Connect(_serverHost, _serverPort);
        }

        private async Task ConnectServerAsync() {
            if (_serverEndPoint != null)
                await TCP.ConnectAsync(_serverEndPoint.Address, _serverEndPoint.Port);
            else
                await TCP.ConnectAsync(_serverHost, _serverPort);
        }

        private void RequestConnect(string destHost, IPAddress destAddress, int destPort) {
            _stream = TCP.GetStream();

            using (var writer = new StreamWriter(_stream, UTF8NoBom, 1024, true) { NewLine = HttpNewLine })
            using (var reader = new StreamReader(_stream, UTF8NoBom, true, 1024, true)) {
                // request
                var destination = string.IsNullOrWhiteSpace(destHost) ? destAddress.ToString() : destHost.ToString();

                writer.WriteLine(string.Format(MethodFieldFormat, destination, destPort));
                writer.WriteLine(string.Format(HostFieldFormat, destination, destPort));

                if (Credential != null) {
                    var authorization = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{Credential.UserName}:{Credential.Password}"));
                    writer.WriteLine(string.Format(ProxyAuthorizationFieldFormat, authorization));
                }

                if (!string.IsNullOrWhiteSpace(UserAgent))
                    writer.WriteLine(string.Format(UserAgentFieldFormat, UserAgent));

                writer.WriteLine(string.Format(ProxyConnectionFieldFormat, KeepAlive ? ConnectionKeepAlive : ConnectionClose));
                writer.WriteLine();

                writer.Flush();

                // response
                CheckResponse(reader.ReadLine());
            }
        }

        private async Task RequestConnectAsync(string destHost, IPAddress destAddress, int destPort) {
            _stream = TCP.GetStream();

            using (var writer = new StreamWriter(_stream, UTF8NoBom, 1024, true) { NewLine = HttpNewLine })
            using (var reader = new StreamReader(_stream, UTF8NoBom, true, 1024, true)) {
                // request
                var destination = string.IsNullOrWhiteSpace(destHost) ? destAddress.ToString() : destHost.ToString();

                await writer.WriteLineAsync(string.Format(MethodFieldFormat, destination, destPort));
                await writer.WriteLineAsync(string.Format(HostFieldFormat, destination, destPort));

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
                CheckResponse(await reader.ReadLineAsync());
            }
        }

        private void CheckResponse(string response) {
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

    public enum TunnelStatus {
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
