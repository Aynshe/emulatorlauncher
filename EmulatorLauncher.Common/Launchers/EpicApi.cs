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
        private const string AssetsUrl = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/assets/Windows?label=Live";
        private const string CatalogUrl = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/{0}/bulk/items?id={1}&country=US&locale=en-US&includeMainGameDetails=true";
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

        public List<EpicLibraryItem> GetLibraryItems(string accessToken, string accountId, string cachePath)
        {
            var games = new List<EpicLibraryItem>();

            var assets = GetAssets(accessToken);
            if (assets == null || assets.Count == 0)
            {
                SimpleLogger.Instance.Info("[EPIC] Found no assets on Epic account.");
                return games;
            }

            foreach (var asset in assets)
            {
                if (asset.@namespace == "ue") continue;

                var catalogItem = GetCatalogItem(accessToken, asset.@namespace, asset.catalogItemId, cachePath);
                if (catalogItem == null) continue;

                if (catalogItem.categories?.Any(a => a.path == "applications") != true) continue;
                if ((catalogItem.mainGameItem != null) && (catalogItem.categories?.Any(a => a.path == "addons/launchable") == false)) continue;
                if (catalogItem.categories?.Any(a => a.path == "digitalextras" || a.path == "plugins" || a.path == "plugins/engine") == true) continue;

                var newGame = new EpicLibraryItem
                {
                    AppName = asset.appName,
                    Metadata = new EpicGameMetadata
                    {
                        DisplayName = catalogItem.title
                    }
                };

                games.Add(newGame);
            }

            return games;
        }

        private List<Asset> GetAssets(string accessToken)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(AssetsUrl);
                request.Method = "GET";
                request.Headers.Add("Authorization", $"bearer {accessToken}");
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            return JsonConvert.DeserializeObject<List<Asset>>(reader.ReadToEnd());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] GetAssets failed: " + ex.Message, ex);
            }

            return new List<Asset>();
        }

        private CatalogItem GetCatalogItem(string accessToken, string nameSpace, string id, string cachePath)
        {
            string cacheFile = null;
            if (!string.IsNullOrEmpty(cachePath))
            {
                cacheFile = Path.Combine(cachePath, $"{nameSpace}_{id}.json");
                if (File.Exists(cacheFile))
                {
                    try
                    {
                        var result = JsonConvert.DeserializeObject<Dictionary<string, CatalogItem>>(File.ReadAllText(cacheFile));
                        if (result.TryGetValue(id, out var catalogItem))
                        {
                            return catalogItem;
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Instance.Error($"[EPIC] Failed to read cache file {cacheFile}: " + ex.Message, ex);
                    }
                }
            }

            try
            {
                var url = string.Format(CatalogUrl, nameSpace, id);
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Headers.Add("Authorization", $"bearer {accessToken}");
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            string json = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(cacheFile))
                            {
                                File.WriteAllText(cacheFile, json);
                            }

                            var result = JsonConvert.DeserializeObject<Dictionary<string, CatalogItem>>(json);
                            if (result.TryGetValue(id, out var catalogItem))
                            {
                                return catalogItem;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error($"[EPIC] GetCatalogItem for {id} failed: " + ex.Message, ex);
            }

            return null;
        }
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
        [JsonProperty("appName")] public string AppName { get; set; }
        [JsonProperty("metadata")] public EpicGameMetadata Metadata { get; set; }
    }

    public class EpicGameMetadata
    {
        [JsonProperty("displayName")] public string DisplayName { get; set; }
    }

    public class Asset
    {
        [JsonProperty("appName")]
        public string appName { get; set; }

        [JsonProperty("catalogItemId")]
        public string catalogItemId { get; set; }

        [JsonProperty("namespace")]
        public string @namespace { get; set; }

        [JsonProperty("buildVersion")]
        public string buildVersion { get; set; }
    }

    public class CatalogItem
    {
        [JsonProperty("title")]
        public string title { get; set; }

        [JsonProperty("categories")]
        public List<Category> categories { get; set; }

        [JsonProperty("mainGameItem")]
        public MainGameItem mainGameItem { get; set; }
    }

    public class Category
    {
        [JsonProperty("path")]
        public string path { get; set; }
    }

    public class MainGameItem
    {
        [JsonProperty("id")]
        public string id { get; set; }
    }
}
