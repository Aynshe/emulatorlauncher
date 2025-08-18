using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicApi
    {
        private const string TokenUrl = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";
        private const string LibraryUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items";

        // Using a public client ID from the documentation
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string ClientSecret = "daafbccc737745039d5256d3e6b86427";

        public EpicApi()
        {
            try
            {
                // Force TLS 1.2 for .NET 4.0
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] Failed to set SecurityProtocol: " + ex.Message);
            }
        }

        private EpicToken PostTokenRequest(Dictionary<string, string> body)
        {
            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Starting.");
            var request = (HttpWebRequest)WebRequest.Create(TokenUrl);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: WebRequest created.");

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            request.Headers.Add("Authorization", $"basic {credentials}");

            var postData = string.Join("&", body.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            var data = Encoding.ASCII.GetBytes(postData);

            request.ContentLength = data.Length;

            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Getting request stream.");
            using (var stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }
            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Request stream written.");

            try
            {
                SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Getting response.");
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Response received with status: " + response.StatusCode);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            var json = reader.ReadToEnd();
                            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Success.");
                            return JsonConvert.DeserializeObject<EpicToken>(json);
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                SimpleLogger.Instance.Error("[EPIC] PostTokenRequest failed with WebException: " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] PostTokenRequest failed with Exception: " + ex.Message);
                return null;
            }

            SimpleLogger.Instance.Info("[EPIC] PostTokenRequest: Failed with no exception.");
            return null;
        }

        public EpicToken AuthenticateWithAuthorizationCode(string authCode)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", authCode },
                { "token_type", "eg1" }
            };

            return PostTokenRequest(body);
        }

        public EpicToken AuthenticateWithRefreshToken(string refreshToken)
        {
            var body = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "token_type", "eg1" }
            };

            return PostTokenRequest(body);
        }

        public List<EpicLibraryItem> GetLibraryItems(string accessToken, string accountId)
        {
            var url = $"{LibraryUrl}?accountIds={accountId}&includeMetadata=true";
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Headers.Add("Authorization", $"bearer {accessToken}");

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            var json = reader.ReadToEnd();
                            return JsonConvert.DeserializeObject<List<EpicLibraryItem>>(json);
                        }
                    }
                }
            }
            catch (WebException)
            {
                return new List<EpicLibraryItem>();
            }

            return new List<EpicLibraryItem>();
        }
    }

    public class EpicToken
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("refresh_expires")]
        public int RefreshExpires { get; set; }

        [JsonProperty("refresh_expires_at")]
        public DateTime RefreshExpiresAt { get; set; }

        [JsonProperty("account_id")]
        public string AccountId { get; set; }

        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        [JsonProperty("internal_client")]
        public bool InternalClient { get; set; }

        [JsonProperty("client_service")]
        public string ClientService { get; set; }
    }

    public class EpicLibraryItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("app_name")]
        public string AppName { get; set; }

        [JsonProperty("metadata")]
        public EpicGameMetadata Metadata { get; set; }
    }

    public class EpicGameMetadata
    {
        [JsonProperty("customAttributes")]
        public Dictionary<string, EpicCustomAttribute> CustomAttributes { get; set; }

        [JsonProperty("mainGameItem")]
        public EpicMainGameItem MainGameItem { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }

    public class EpicCustomAttribute
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }
    }

    public class EpicMainGameItem
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("namespace")]
        public string Namespace { get; set; }

        [JsonProperty("unrealEngine")]
        public bool UnrealEngine { get; set; }
    }
}
