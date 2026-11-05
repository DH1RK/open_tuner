using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace opentuner.ExtraFeatures.StreamDeck
{
    // WebSocket server for the Stream Deck plugin (#88): only on 127.0.0.1, own port, JSON messages.
    //
    // Plugin -> OpenTuner: {"cmd":"select","tuner":2} - commands are handed to CommandReceived (called on a thread pool thread,
    //                      the receiver has to switch to the UI thread). "tuner" is 1 based, left out = the selected tuner.
    // OpenTuner -> plugin: the state as JSON (see MainForm), sent to every client when it changes and to a new client at once.
    //
    // Security: there is no login, and the Origin header is not checked. Any program on this computer can connect and send the
    // commands, and a web page in a browser on this computer can as well (the browser allows WebSocket connections to 127.0.0.1).
    // The commands only change the view, the volume and the mute state, nothing is read out or transmitted. A check of the Origin
    // header would only keep out the web pages, a program sets the header as it likes; against programs only a token would help.
    //
    // The server is a TcpListener with the WebSocket handshake of RFC 6455 done here and System.Net.WebSockets for the frames (#108):
    // HttpListener would need a URL reservation (administrator) for the prefix, a plain listener on the loopback address does not.
    public class StreamDeckServer
    {
        public const int DefaultPort = 8765;

        private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int MaxHeaderBytes = 8192;
        private const int MaxMessageBytes = 65536;

        // command name and its arguments
        public event Action<string, JObject> CommandReceived;

        private TcpListener _listener;
        private CancellationTokenSource _cancel;
        private string _last_state = null;
        private readonly ConcurrentDictionary<Guid, StreamDeckClient> _clients = new ConcurrentDictionary<Guid, StreamDeckClient>();

        public bool Running { get { return _listener != null; } }

        private class StreamDeckClient
        {
            public Guid Id = Guid.NewGuid();
            public WebSocket Socket;
            public SemaphoreSlim SendLock = new SemaphoreSlim(1, 1);
        }

        public bool Start(int port)
        {
            if (_listener != null)
                return true;

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                _listener = listener;
                _cancel = new CancellationTokenSource();
                var cancel = _cancel.Token;
                Task.Run(() => AcceptLoop(listener, cancel));
                Log.Information("Stream Deck server listening on 127.0.0.1:" + port);
                return true;
            }
            catch (Exception ex)
            {
                // e.g. the port is used by another program: OpenTuner keeps running without the server
                Log.Warning(ex, "Stream Deck server could not be started on port " + port);
                _listener = null;
                return false;
            }
        }

        public void Stop()
        {
            var listener = _listener;
            var cancel = _cancel;
            _listener = null;
            _cancel = null;
            _last_state = null;
            if (listener == null)
                return;

            try { cancel?.Cancel(); } catch (Exception) { }
            try { listener.Stop(); }
            catch (Exception ex) { Log.Warning(ex, "Stream Deck server stop failed"); }

            foreach (var client in _clients.Values)
            {
                try { client.Socket.Abort(); } catch (Exception) { }
            }
            _clients.Clear();
        }

        // sends the state to all clients, only if it differs from the one sent last
        public void PublishState(string state)
        {
            if (_listener == null || state == _last_state)
                return;

            _last_state = state;
            foreach (var client in _clients.Values)
                _ = SendAsync(client, state);
        }

        private async Task SendAsync(StreamDeckClient client, string text)
        {
            try
            {
                await client.SendLock.WaitAsync().ConfigureAwait(false);   // one send at a time per socket
                try
                {
                    if (client.Socket.State != WebSocketState.Open)
                        return;

                    using (var timeout = new CancellationTokenSource(5000))
                        await client.Socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    client.SendLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Stream Deck send failed");
                try { client.Socket.Abort(); } catch (Exception) { }
            }
        }

        private async Task AcceptLoop(TcpListener listener, CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;   // stopped
                }

                _ = Task.Run(() => HandleClient(tcp, cancel));
            }
        }

        private async Task HandleClient(TcpClient tcp, CancellationToken cancel)
        {
            StreamDeckClient client = null;
            try
            {
                using (tcp)
                {
                    tcp.NoDelay = true;
                    var stream = tcp.GetStream();

                    var socket = await Handshake(stream, cancel).ConfigureAwait(false);
                    if (socket == null)
                        return;

                    client = new StreamDeckClient { Socket = socket };
                    _clients[client.Id] = client;
                    Log.Information("Stream Deck client connected: " + client.Id.ToString("N"));

                    string state = _last_state;
                    if (state != null)
                        await SendAsync(client, state).ConfigureAwait(false);

                    await ReceiveLoop(client, cancel).ConfigureAwait(false);
                }
            }
            catch (WebSocketException)
            {
                // the plugin was ended or the connection dropped without the close handshake: nothing to report
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    Log.Warning("Stream Deck: connection ended (" + ex.Message + ")");
            }
            finally
            {
                if (client != null)
                {
                    _clients.TryRemove(client.Id, out _);
                    try { client.Socket.Dispose(); } catch (Exception) { }
                    Log.Information("Stream Deck client disconnected: " + client.Id.ToString("N"));
                }
            }
        }

        // reads the HTTP request, answers it with "101 Switching Protocols" and returns the WebSocket (null: not a WebSocket request)
        private static async Task<WebSocket> Handshake(NetworkStream stream, CancellationToken cancel)
        {
            string request;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(5000);

                var buffer = new byte[MaxHeaderBytes];
                int length = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, length, buffer.Length - length, timeout.Token).ConfigureAwait(false);
                    if (read == 0)
                        return null;
                    length += read;

                    string text = Encoding.ASCII.GetString(buffer, 0, length);
                    int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        request = text.Substring(0, end);
                        break;
                    }
                    if (length >= buffer.Length)
                    {
                        await Reject(stream, "431 Request Header Fields Too Large").ConfigureAwait(false);
                        return null;
                    }
                }
            }

            string[] lines = request.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] request_line = lines[0].Split(' ');
            if (request_line.Length < 3 || request_line[0] != "GET")
            {
                await Reject(stream, "400 Bad Request").ConfigureAwait(false);
                return null;
            }

            // the service is on "/" only
            if (request_line[1] != "/" && !request_line[1].StartsWith("/?", StringComparison.Ordinal))
            {
                await Reject(stream, "404 Not Found").ConfigureAwait(false);
                return null;
            }

            string key = null, upgrade = null, connection = null, version = null;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                    continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (name.Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)) key = value;
                else if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) upgrade = value;
                else if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) connection = value;
                else if (name.Equals("Sec-WebSocket-Version", StringComparison.OrdinalIgnoreCase)) version = value;
            }

            if (string.IsNullOrEmpty(key) || !string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase)
                || connection == null || connection.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) < 0)
            {
                await Reject(stream, "400 Bad Request").ConfigureAwait(false);
                return null;
            }

            if (version != "13")
            {
                await Reject(stream, "426 Upgrade Required", "Sec-WebSocket-Version: 13\r\n").ConfigureAwait(false);
                return null;
            }

            string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + HandshakeGuid)));
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n");
            await stream.WriteAsync(response, 0, response.Length, cancel).ConfigureAwait(false);

            return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
        }

        private static async Task Reject(NetworkStream stream, string status, string extra_headers = "")
        {
            try
            {
                byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\n" + extra_headers + "Content-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
            }
            catch (Exception) { }
        }

        private async Task ReceiveLoop(StreamDeckClient client, CancellationToken cancel)
        {
            var socket = client.Socket;
            var buffer = new byte[4096];
            using (var message = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open && !cancel.IsCancellationRequested)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if (socket.State == WebSocketState.CloseReceived)
                            {
                                using (var answer = new CancellationTokenSource(3000))
                                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", answer.Token).ConfigureAwait(false);
                            }
                            return;
                        }

                        message.Write(buffer, 0, result.Count);
                        if (message.Length > MaxMessageBytes)
                        {
                            Log.Warning("Stream Deck: message too long, connection closed");
                            socket.Abort();
                            return;
                        }
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                        HandleMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                }
            }
        }

        internal void HandleMessage(string data)
        {
            try
            {
                JObject message = JObject.Parse(data);
                string command = (string)message["cmd"];
                if (string.IsNullOrEmpty(command))
                    return;

                CommandReceived?.Invoke(command, message);
            }
            catch (Exception ex)
            {
                Log.Warning("Stream Deck: message ignored (" + ex.Message + "): " + data);
            }
        }
    }
}
