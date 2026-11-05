using Serilog;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace opentuner.Utilities
{
    public class OTMessageEventArgs : EventArgs
    {
        public byte[] RawData { get; }
        public bool IsText { get; }

        // the payload as text (for a text message the message itself)
        public string Data => Encoding.UTF8.GetString(RawData);

        public OTMessageEventArgs(byte[] raw_data, bool is_text)
        {
            RawData = raw_data;
            IsText = is_text;
        }
    }

    public class OTCloseEventArgs : EventArgs
    {
        public int Code { get; }
        public string Reason { get; }

        public OTCloseEventArgs(int code, string reason)
        {
            Code = code;
            Reason = reason;
        }

        public override string ToString()
        {
            return "Code " + Code + (string.IsNullOrEmpty(Reason) ? "" : ", " + Reason);
        }
    }

    public class OTErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public Exception Exception { get; }

        public OTErrorEventArgs(string message, Exception exception)
        {
            Message = message;
            Exception = exception;
        }

        public override string ToString()
        {
            return Exception == null ? Message : Message + " (" + Exception.GetType().Name + ": " + Exception.Message + ")";
        }
    }

    /// <summary>
    /// WebSocket client on top of System.Net.WebSockets.ClientWebSocket with the events and the blocking Connect / Send / Close
    /// of the former WebSocketSharp client (#108), so that the callers keep their structure:
    /// - a failed Connect() raises OnError and then OnClose (the reconnect logic of the callers hangs on that),
    /// - Close() raises OnClose before it returns,
    /// - every connection raises OnClose exactly once,
    /// - Connect() can be called again on the same object after the close.
    /// The events come from a thread pool thread, the messages one after the other in the order they arrive.
    /// </summary>
    public class OTWebSocket
    {
        // everything that belongs to one connection, so that a late event of an old connection cannot touch a new one
        private class Connection
        {
            public ClientWebSocket Socket;
            public CancellationTokenSource Cancel;
            public int CloseRaised;
            public volatile bool CloseRequested;
        }

        private readonly Uri _uri;
        private readonly string _sub_protocol;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _send_lock = new SemaphoreSlim(1, 1);
        private Connection _connection;

        // how long Connect() waits for the handshake, how long Close() waits for the answer of the server
        public int ConnectTimeoutMs { get; set; } = 10000;
        public int CloseTimeoutMs { get; set; } = 3000;

        public event EventHandler OnOpen;
        public event EventHandler<OTMessageEventArgs> OnMessage;
        public event EventHandler<OTCloseEventArgs> OnClose;
        public event EventHandler<OTErrorEventArgs> OnError;

        public OTWebSocket(string url, string sub_protocol = null)
        {
            _uri = new Uri(url.Trim());
            _sub_protocol = sub_protocol;
        }

        public WebSocketState ReadyState
        {
            get
            {
                var socket = _connection?.Socket;
                return socket == null ? WebSocketState.None : socket.State;
            }
        }

        public bool IsAlive
        {
            get { return ReadyState == WebSocketState.Open; }
        }

        /// <summary>Connects and returns when the connection is open or the attempt has failed (then OnError and OnClose have been raised).</summary>
        public void Connect()
        {
            Connection connection;
            lock (_lock)
            {
                var state = ReadyState;
                if (state == WebSocketState.Open || state == WebSocketState.Connecting)
                    return;

                var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.Zero;   // no pings of our own, like WebSocketSharp
                if (!string.IsNullOrEmpty(_sub_protocol))
                    socket.Options.AddSubProtocol(_sub_protocol);

                connection = new Connection { Socket = socket, Cancel = new CancellationTokenSource() };
                _connection = connection;
            }

            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Cancel.Token))
                {
                    timeout.CancelAfter(ConnectTimeoutMs);
                    connection.Socket.ConnectAsync(_uri, timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                if (!connection.CloseRequested)
                    RaiseError("An exception has occurred while connecting.", ex);
                RaiseClose(connection, 1006, "connect failed");
                return;
            }

            if (connection.CloseRequested)
            {
                // Close() came in while the handshake was finishing
                try { connection.Socket.Abort(); } catch { }
                RaiseClose(connection, 1001, "closed while connecting");
                return;
            }

            try { OnOpen?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Warning(ex, "WebSocket: OnOpen handler failed"); }

            Task.Run(() => ReceiveLoop(connection));
        }

        private async Task ReceiveLoop(Connection connection)
        {
            var socket = connection.Socket;
            var buffer = new byte[16384];
            int close_code = 1006;
            string close_reason = "";

            try
            {
                using (var message = new MemoryStream())
                {
                    while (socket.State == WebSocketState.Open)
                    {
                        message.SetLength(0);
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), connection.Cancel.Token).ConfigureAwait(false);
                            if (result.MessageType == WebSocketMessageType.Close)
                                break;
                            message.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            close_code = result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : 1005;
                            close_reason = result.CloseStatusDescription ?? "";
                            break;
                        }

                        try
                        {
                            OnMessage?.Invoke(this, new OTMessageEventArgs(message.ToArray(), result.MessageType == WebSocketMessageType.Text));
                        }
                        catch (Exception ex)
                        {
                            // a failing handler must not end the connection
                            Log.Warning(ex, "WebSocket: OnMessage handler failed");
                        }
                    }
                }

                // the server closed: answer the close frame
                if (socket.State == WebSocketState.CloseReceived)
                {
                    using (var answer = new CancellationTokenSource(CloseTimeoutMs))
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", answer.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (!connection.CloseRequested)
                    RaiseError("The connection was lost.", ex);
            }

            RaiseClose(connection, close_code, close_reason);
        }

        /// <summary>Sends a text message. A socket that is not open raises OnError and sends nothing, like WebSocketSharp.</summary>
        public void Send(string data)
        {
            var socket = _connection?.Socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                RaiseError("The current state of the connection is not Open.", null);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(data);
            _send_lock.Wait();   // ClientWebSocket allows one send at a time
            try
            {
                using (var timeout = new CancellationTokenSource(5000))
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                RaiseError("The message could not be sent.", ex);
            }
            finally
            {
                _send_lock.Release();
            }
        }

        /// <summary>Closes the connection (a connect attempt that is still running is cancelled) and raises OnClose before it returns.</summary>
        public void Close()
        {
            var connection = _connection;
            if (connection == null)
                return;

            connection.CloseRequested = true;
            var socket = connection.Socket;

            try
            {
                var state = socket.State;
                if (state == WebSocketState.Open || state == WebSocketState.CloseReceived)
                {
                    using (var timeout = new CancellationTokenSource(CloseTimeoutMs))
                        socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception)
            {
                // the server did not answer in time: the connection is dropped
                try { socket.Abort(); } catch { }
            }

            try { connection.Cancel.Cancel(); } catch { }   // ends a pending connect and the receive loop
            RaiseClose(connection, 1000, "closed by the client");
        }

        private void RaiseClose(Connection connection, int code, string reason)
        {
            if (System.Threading.Interlocked.Exchange(ref connection.CloseRaised, 1) != 0)
                return;

            try { connection.Socket.Dispose(); } catch { }

            try { OnClose?.Invoke(this, new OTCloseEventArgs(code, reason)); }
            catch (Exception ex) { Log.Warning(ex, "WebSocket: OnClose handler failed"); }
        }

        private void RaiseError(string message, Exception exception)
        {
            try { OnError?.Invoke(this, new OTErrorEventArgs(message, exception)); }
            catch (Exception ex) { Log.Warning(ex, "WebSocket: OnError handler failed"); }
        }
    }
}
