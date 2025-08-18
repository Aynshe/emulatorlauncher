using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicApi
    {
        private const string TokenUrl = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";
        private const string LibraryUrl = "https://library-service.live.use1a.on.epicgames.com/library/api/public/items";

        // Using a public client ID from the documentation
        private const string ClientId = "34a02cf8f4414e29b15921876da36f9a";
        private const string ClientSecret = "daafbccc737745039d5256d3e6b86427";

        private HttpClient _httpClient;

        public EpicApi()
        {
            _httpClient = new HttpClient();
        }

        public async Task<EpicToken> AuthenticateWithAuthorizationCode(string authCode)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);

            var body = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", authCode },
                { "token_type", "eg1" }
            };

            request.Content = new FormUrlEncodedContent(body);

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            request.Headers.Add("Authorization", $"basic {credentials}");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<EpicToken>(json);
            }

            return null;
        }

        public async Task<EpicToken> AuthenticateWithRefreshToken(string refreshToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);

            var body = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "token_type", "eg1" }
            };

            request.Content = new FormUrlEncodedContent(body);

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
            request.Headers.Add("Authorization", $"basic {credentials}");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<EpicToken>(json);
            }

            return null;
        }

        public async Task<List<EpicLibraryItem>> GetLibraryItems(string accessToken, string accountId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{LibraryUrl}?accountIds={accountId}&includeMetadata=true");
            request.Headers.Add("Authorization", $"bearer {accessToken}");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<EpicLibraryItem>>(json);
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
