using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicOAuthClient
    {
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string LoginUrl = "https://www.epicgames.com/id/login?redirectUrl={0}&prompt=login&clientId={1}";

        public string PerformInteractiveLogin()
        {
            string redirectUri = "http://localhost:54321/callback/"; // A port that is unlikely to be in use
            var httpListener = new HttpListener();
            httpListener.Prefixes.Add(redirectUri);

            try
            {
                httpListener.Start();

                string finalLoginUrl = string.Format(LoginUrl, Uri.EscapeDataString(redirectUri), ClientId);
                Process.Start(finalLoginUrl);

                // Synchronously wait for a request
                var context = httpListener.GetContext();

                // Got a request, stop listening
                httpListener.Stop();

                // Send a response to the browser
                var response = context.Response;
                string responseString = "<html><head><title>Success</title></head><body>Authentication successful! You can close this window.</body></html>";
                var buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                var responseOutput = response.OutputStream;
                responseOutput.Write(buffer, 0, buffer.Length);
                responseOutput.Close();

                // Extract the authorization code from the query string
                var code = context.Request.QueryString.Get("code");
                return code;
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] Interactive login failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (httpListener.IsListening)
                {
                    httpListener.Stop();
                }
            }
        }
    }
}
