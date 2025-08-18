using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicOAuthClient
    {
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string LoginUrl = "https://www.epicgames.com/id/login?redirectUrl={0}&prompt=login&clientId={1}";

        public string PerformInteractiveLogin()
        {
            int port = GetRandomUnusedPort();
            if (port == -1)
            {
                SimpleLogger.Instance.Error("[EPIC] Could not find a free TCP port for interactive login.");
                return null;
            }

            string redirectUri = $"http://localhost:{port}/callback/";
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add(redirectUri);

            try
            {
                httpListener.Start();
                string finalLoginUrl = string.Format(LoginUrl, Uri.EscapeDataString(redirectUri), ClientId);
                Process.Start(finalLoginUrl);

                var context = httpListener.GetContext();
                httpListener.Stop();

                var response = context.Response;
                string responseString = "<html><head><title>Success</title></head><body>Authentication successful! You can close this window.</body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                var responseOutput = response.OutputStream;
                responseOutput.Write(buffer, 0, buffer.Length);
                responseOutput.Close();

                return context.Request.QueryString.Get("code");
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] Interactive login failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (httpListener != null && httpListener.IsListening)
                {
                    httpListener.Stop();
                }
            }
        }

        private static int GetRandomUnusedPort()
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
            catch { }

            return -1;
        }
    }
}
