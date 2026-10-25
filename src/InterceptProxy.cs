using System;
using System.Net;
using System.Threading.Tasks;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace Aloha
{

    public class InterceptProxy
    {
        private ProxyServer server;
        private ExplicitProxyEndPoint endPoint;

        public bool Running { get; private set; }
        public int  Port    { get; private set; }

        public event Action<string> OnTransaction;

        public void Start(int port, NetConfig cfg)
        {
            if (Running) return;
            Port = port;

            server = new ProxyServer();

            if (cfg != null
                && !string.Equals(cfg.ProxyScheme, "none", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(cfg.ProxyHost))
            {
                var up = new ExternalProxy { HostName = cfg.ProxyHost, Port = cfg.ProxyPort };
                if (cfg.ProxyScheme.ToLowerInvariant().StartsWith("socks"))
                    up.ProxyType = ExternalProxyType.Socks5;
                server.GetCustomUpStreamProxyFunc = _ => Task.FromResult<IExternalProxy>(up);
            }

            server.CertificateManager.EnsureRootCertificate();

            server.BeforeRequest  += OnRequest;
            server.BeforeResponse += OnResponse;

            endPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, true);
            server.AddEndPoint(endPoint);
            server.Start();

            Running = true;
            Emit("== intercept proxy listening on 127.0.0.1:" + port
                 + (server.GetCustomUpStreamProxyFunc != null ? "  (chained to upstream)" : "") + " ==");
        }

        public void Stop()
        {
            if (!Running) return;
            try
            {
                server.BeforeRequest  -= OnRequest;
                server.BeforeResponse -= OnResponse;
                server.Stop();
            }
            catch { }
            Running = false;
            Emit("== intercept proxy stopped ==");
        }

        private Task OnRequest(object sender, SessionEventArgs e)
        {
            try
            {
                var rq = e.HttpClient.Request;

                try
                {
                    var eu = new Uri(rq.Url);
                    if (!Aloha.Net.EgressGate.Current.ShouldAllow(eu.Host, eu.Port))
                    {
                        Emit("⋯ egress sealed — denied " + eu.Host);
                        e.GenericResponse("egress sealed by Aloha", HttpStatusCode.Forbidden, closeServerConnection: true);
                        return Task.CompletedTask;
                    }
                }
                catch { }

                Emit("> " + rq.Method + "  " + rq.Url);
            }
            catch { }
            return Task.CompletedTask;
        }

        private Task OnResponse(object sender, SessionEventArgs e)
        {
            try
            {
                var rq = e.HttpClient.Request;
                var rs = e.HttpClient.Response;
                string ct = rs.ContentType != null ? "  [" + rs.ContentType + "]" : "";
                Emit("< " + (int)rs.StatusCode + " " + rs.StatusDescription + "  " + rq.Url + ct);
            }
            catch { }
            return Task.CompletedTask;
        }

        private void Emit(string line)
        {
            ActivityLog.Proxy(line);
            OnTransaction?.Invoke(line);
        }
    }
}
