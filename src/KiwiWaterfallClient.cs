using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aloha
{

    public class KiwiWaterfallClient : IDisposable
    {
        public event Action<byte[]> FrameReceived;
        public event Action<string> Status;

        private ClientWebSocket ws;
        private CancellationTokenSource cts;
        private Task loop;
        public bool Running { get; private set; }
        public int Bins { get; private set; } = 1024;

        private int gen;

        public void Start(string host, int zoom = 0, string password = "")
        {
            Stop();
            cts = new CancellationTokenSource();
            var token = cts.Token;
            Running = true;
            int myGen = ++gen;
            loop = Task.Run(() => Run(host, zoom, password ?? "", token, myGen));
        }

        public void Stop()
        {
            Running = false;
            try { cts?.Cancel(); } catch { }
            try { ws?.Dispose(); } catch { }
            ws = null; cts = null;
        }

        public void Dispose() { Stop(); }

        private async Task Run(string host, int zoom, string password, CancellationToken ct, int myGen)
        {
            try
            {
                if (host.StartsWith("http://")) host = host.Substring(7);
                if (host.StartsWith("https://")) host = host.Substring(8);
                int slash = host.IndexOf('/');
                if (slash >= 0) host = host.Substring(0, slash);
                if (host.IndexOf(':') < 0) host += ":8073";

                {
                    int c = host.LastIndexOf(':');
                    string gh = c > 0 ? host.Substring(0, c) : host;
                    int gp = 8073; if (c > 0) int.TryParse(host.Substring(c + 1), out gp);
                    if (!Aloha.Net.EgressGate.Current.ShouldAllow(gh, gp))
                    { Emit("egress sealed — waterfall stream refused"); return; }
                }

                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                var uri = new Uri("ws://" + host + "/" + ts + "/W/F");

                ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                Emit("connecting " + uri.Host + ":" + uri.Port + " ...");
                await ws.ConnectAsync(uri, ct);

                await Send("SET auth t=kiwi p=" + password, ct);
                await Send("SET zoom=" + zoom + " start=0", ct);
                await Send("SET maxdb=-10 mindb=-110", ct);
                await Send("SET wf_speed=4", ct);
                await Send("SET wf_comp=0", ct);
                await Send("SET interp=13", ct);
                Emit("waterfall stream open");

                var buf = new byte[65536];
                var frame = new System.IO.MemoryStream();
                DateTime lastKeep = DateTime.UtcNow;

                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    if ((DateTime.UtcNow - lastKeep).TotalSeconds > 5)
                    { await Send("SET keepalive", ct); lastKeep = DateTime.UtcNow; }

                    frame.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                        if (r.MessageType == WebSocketMessageType.Close) { Emit("server closed stream"); return; }
                        frame.Write(buf, 0, r.Count);
                    } while (!r.EndOfMessage);

                    var msg = frame.ToArray();
                    if (msg.Length < 4) continue;
                    string tag = Encoding.ASCII.GetString(msg, 0, 3);

                    if (tag == "W/F" && msg.Length > 16)
                    {
                        var bins = new byte[msg.Length - 16];
                        Buffer.BlockCopy(msg, 16, bins, 0, bins.Length);
                        Bins = bins.Length;
                        FrameReceived?.Invoke(bins);
                    }
                    else if (tag == "MSG")
                    {
                        string body = Encoding.ASCII.GetString(msg, 3, msg.Length - 3);
                        int i = body.IndexOf("wf_fft_size=");
                        if (i >= 0)
                        {
                            int v = 0, j = i + 12;
                            while (j < body.Length && char.IsDigit(body[j])) v = v * 10 + (body[j++] - '0');
                            if (v > 0) Bins = v;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Emit("wf: " + ex.GetType().Name + " — " + ex.Message); }
            finally { if (myGen == gen) Running = false; }
        }

        private Task Send(string s, CancellationToken ct)
            => ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(s)),
                            WebSocketMessageType.Binary, true, ct);

        private void Emit(string s) { try { Status?.Invoke(s); } catch { } }
    }
}
