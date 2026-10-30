using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace opentuner.ExtraFeatures.StreamDeck
{
    // WebSocket server for the Stream Deck plugin (#88): only on 127.0.0.1, own port, JSON messages.
    //
    // Plugin -> OpenTuner: {"cmd":"select","tuner":2} - commands are handed to CommandReceived (called on the socket's thread,
    //                      the receiver has to switch to the UI thread). "tuner" is 1 based, left out = the selected tuner.
    // OpenTuner -> plugin: the state as JSON (see MainForm), sent to every client when it changes and to a new client at once.
    public class StreamDeckServer
    {
        public const int DefaultPort = 8765;

        // command name and its arguments
        public event Action<string, JObject> CommandReceived;

        private WebSocketServer _server;
        private string _last_state = null;

        public bool Running { get { return _server != null; } }

        public bool Start(int port)
        {
            if (_server != null)
                return true;

            try
            {
                var server = new WebSocketServer(IPAddress.Loopback, port);
                server.AddWebSocketService<StreamDeckConnection>("/", () => new StreamDeckConnection(this));
                server.Start();
                _server = server;
                Log.Information("Stream Deck server listening on 127.0.0.1:" + port);
                return true;
            }
            catch (Exception ex)
            {
                // e.g. the port is used by another program: OpenTuner keeps running without the server
                Log.Warning(ex, "Stream Deck server could not be started on port " + port);
                _server = null;
                return false;
            }
        }

        public void Stop()
        {
            var server = _server;
            _server = null;
            _last_state = null;
            if (server == null)
                return;

            try { server.Stop(); }
            catch (Exception ex) { Log.Warning(ex, "Stream Deck server stop failed"); }
        }

        // sends the state to all clients, only if it differs from the one sent last
        public void PublishState(string state)
        {
            if (_server == null || state == _last_state)
                return;

            _last_state = state;
            try { _server.WebSocketServices.Broadcast(state); }
            catch (Exception ex) { Log.Warning(ex, "Stream Deck broadcast failed"); }
        }

        internal string LastState { get { return _last_state; } }

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

    internal class StreamDeckConnection : WebSocketBehavior
    {
        private readonly StreamDeckServer _owner;

        public StreamDeckConnection(StreamDeckServer owner)
        {
            _owner = owner;
        }

        protected override void OnOpen()
        {
            Serilog.Log.Information("Stream Deck client connected: " + ID);

            string state = _owner.LastState;
            if (state != null)
                Send(state);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            if (e.IsText)
                _owner.HandleMessage(e.Data);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Serilog.Log.Information("Stream Deck client disconnected: " + ID);
        }
    }
}
