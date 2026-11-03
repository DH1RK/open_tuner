using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebSocketSharp;
using WebSocketSharp.Server;
using Serilog;

namespace opentuner
{
    public class WSClientConnection : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            //Send(e.Data);

        }

        protected override void OnClose(CloseEventArgs e)
        {
            //base.OnClose(e);
            Serilog.Log.Information("Ws closed: " + e.ToString());
        }

        protected override void OnOpen()
        {
            //base.OnOpen();
            Serilog.Log.Information("Ws open: " + this.ID);
        }

    }
    public class OTWebsocketServer
    {
        WebSocketServer ws_server;

        public OTWebsocketServer(int port) 
        { 
            ws_server = new WebSocketServer(port);
            ws_server.AddWebSocketService<WSClientConnection>("/");
            ws_server.Start();
        }

        public void Broadcast(string msg)
        {
            if (ws_server != null)
            {
                ws_server.WebSocketServices.Broadcast(msg);
            }
        }
    }

    [Serializable]
    public class monitorMessage_packet
    {
        public monitorMessage_packet_rx rx;
        public monitorMessage_packet_ts ts;
    }


    [Serializable]
    public class monitorMessage_packet_rx
    {
        public int rfport;
        public int demod_state;
        public int ber;
        public int frequency;
        public int symbolrate;
        public int mer;
        public int modcod;
        // more fields of the Longmynd WebSocket (web.c), read for the Longmynd view (#186)
        public int lna;                      // raw LNA readout, (SWLNAGAIN << 5) | VGO, like the MiniTiouner
        public int agc1;
        public int agc2;
        public bool lnb_voltage_enabled;
        public bool lnb_voltage_polarisation_h;
        public int vber;
        public int errors_bch_uncorrected;   // flag, inverted: 1 = the chip's BCH ERRORFLAG is clear (normal), 0 = it is set
        public int errors_bch_count;
        public int errors_ldpc_count;
        public bool short_frame;
        public bool pilot_symbols;
        public int[][] constellation;        // I/Q points
        public string ts_ip_addr;
        public int ts_ip_port;
    }

    [Serializable]
    public class monitorMessage_packet_ts
    {
        public string service_name;
        public string service_provider_name;
        public int null_ratio;
        public int[][] PIDs;
    }

    [Serializable]
    public class monitorMessage
    {
        public string type;
        public double timestamp;
        public monitorMessage_packet packet;
    }
}
