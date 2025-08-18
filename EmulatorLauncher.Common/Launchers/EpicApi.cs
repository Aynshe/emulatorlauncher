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
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string ClientSecret = "daafbccc737745039dffe53d94fc76cf";

        public EpicApi()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
            catch (Exception ex) { SimpleLogger.Instance.Error("[EPIC] Failed to set SecurityProtocol: " + ex.Message); }
        }

        private EpicToken PostTokenRequest(Dictionary<string, string> body)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(TokenUrl);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.Headers.Add("Authorization", $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"))}");
                var postData = string.Join("&", body.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                var data = Encoding.ASCII.GetBytes(postData);
                request.ContentLength = data.Length;
                using (var stream = request.GetRequestStream()) { stream.Write(data, 0, data.Length); }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream())) { return JsonConvert.DeserializeObject<EpicToken>(reader.ReadToEnd()); }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] PostTokenRequest failed: " + ex.Message);
                return null;
            }
            return null;
        }

        public EpicToken AuthenticateWithAuthorizationCode(string authCode)
        {
            var body = new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", authCode }, { "token_type", "eg1" } };
            return PostTokenRequest(body);
        }

        public EpicToken AuthenticateWithRefreshToken(string refreshToken)
        {
            var body = new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", refreshToken }, { "token_type", "eg1" } };
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
                            // The response is an object with a "records" property which is the array of games
                            var result = JsonConvert.DeserializeObject<EpicLibraryResponse>(reader.ReadToEnd());
                            return result?.Records ?? new List<EpicLibraryItem>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] GetLibraryItems failed: " + ex.Message);
                return new List<EpicLibraryItem>();
            }
            return new List<EpicLibraryItem>();
        }
    }

    public class EpicLibraryResponse
    {
        [JsonProperty("records")]
        public List<EpicLibraryItem> Records { get; set; }
    }

    public class EpicToken
    {
        [JsonProperty("access_token")] public string AccessToken { get; set; }
        [JsonProperty("expires_in")] public int ExpiresIn { get; set; }
        [JsonProperty("expires_at")] public DateTime ExpiresAt { get; set; }
        [JsonProperty("token_type")] public string TokenType { get; set; }
        [JsonProperty("refresh_token")] public string RefreshToken { get; set; }
        [JsonProperty("refresh_expires")] public int RefreshExpires { get; set; }
        [JsonProperty("refresh_expires_at")] public DateTime RefreshExpiresAt { get; set; }
        [JsonProperty("account_id")] public string AccountId { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("internal_client")] public bool InternalClient { get; set; }
        [JsonProperty("client_service")] public string ClientService { get; set; }
    }

    public class EpicLibraryItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("namespace")] public string Namespace { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("app_name")] public string AppName { get; set; }
        [JsonProperty("metadata")] public EpicGameMetadata Metadata { get; set; }
    }

    public class EpicGameMetadata
    {
        [JsonProperty("customAttributes")] public Dictionary<string, EpicCustomAttribute> CustomAttributes { get; set; }
        [JsonProperty("mainGameItem")] public EpicMainGameItem MainGameItem { get; set; }
        [JsonProperty("displayName")] public string DisplayName { get; set; }
    }

    public class EpicCustomAttribute
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("value")] public string Value { get; set; }
    }

    public class EpicMainGameItem
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("namespace")] public string Namespace { get; set; }
        [JsonProperty("unrealEngine")] public bool UnrealEngine { get; set; }
    }
}
