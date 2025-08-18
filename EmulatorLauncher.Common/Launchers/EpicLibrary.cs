using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.IO;
using EmulatorLauncher.Common.FileFormats;
using Microsoft.Win32;
using EmulatorLauncher.Common.Launchers.Epic;
using EmulatorLauncher.Common.EmulationStation;
using System.Text.RegularExpressions;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicLibrary
    {
        const string GameLaunchUrl = @"com.epicgames.launcher://apps/{0}?action=launch&silent=true";

        public static string GetEpicGameExecutableName(Uri uri)
        {
            string shorturl = Regex.Replace(uri.LocalPath, @"[^a-zA-Z0-9]", "");

            var modSdkMetadataDir = GetMetadataPath();
            if (modSdkMetadataDir != null)
            {
                string manifestPath = modSdkMetadataDir.ToString();

                string gameExecutable = null;

                if (Directory.Exists(manifestPath))
                {
                    foreach (var manifest in GetInstalledManifests())
                    {
                        if (shorturl.Equals(manifest.AppName))
                        {
                            gameExecutable = manifest.LaunchExecutable;
                            break;
                        }
                        else if (shorturl.Equals(manifest.MainGameAppName))
                        {
                            gameExecutable = manifest.LaunchExecutable;
                            break;
                        }
                    }
                }

                if (gameExecutable == null)
                    throw new ApplicationException("There is a problem: The Game is not installed");

                return Path.GetFileNameWithoutExtension(gameExecutable);
            }

            throw new ApplicationException("There is a problem: Epic Launcher is not installed");
        }

        public static LauncherGameInfo[] GetInstalledGames(List<LauncherGameInfo> apiGames = null)
        {
            var games = new List<LauncherGameInfo>();

            if (!IsInstalled)
                return games.ToArray();

            var appList = GetInstalledAppList();
            var manifests = GetInstalledManifests();

            if (appList == null || manifests == null)
                return games.ToArray();

            foreach (var app in appList)
            {
                if (app.AppName.StartsWith("UE_"))
                    continue;

                var manifest = manifests.FirstOrDefault(a => a.AppName == app.AppName);
                if (manifest == null)
                    continue;

                // Skip DLCs
                if (manifest.AppName != manifest.MainGameAppName)
                    continue;

                // Skip Plugins
                if (manifest.AppCategories == null || manifest.AppCategories.Any(a => a == "plugins" || a == "plugins/engine"))
                    continue;

                var gameName = manifest.DisplayName ?? Path.GetFileName(app.InstallLocation);

                if (apiGames != null)
                {
                    var apiGame = apiGames.FirstOrDefault(g => g.Id == app.AppName);
                    if (apiGame != null && !string.IsNullOrEmpty(apiGame.Name))
                        gameName = apiGame.Name;
                }

                var installLocation = manifest.InstallLocation ?? app.InstallLocation;
                if (string.IsNullOrEmpty(installLocation))
                    continue;

                var game = new LauncherGameInfo()
                {
                    Id = app.AppName,
                    Name = gameName,
                    LauncherUrl = string.Format(GameLaunchUrl, manifest.AppName),
                    InstallDirectory = Path.GetFullPath(installLocation),
                    ExecutableName = manifest.LaunchExecutable,
                    Launcher = GameLauncherType.Epic,
                    IsInstalled = true
                };

                var exePath = Path.Combine(game.InstallDirectory, game.ExecutableName);

                if (exePath != null && File.Exists(exePath))
                    game.IconPath = exePath;

                games.Add(game);
            }

            return games.ToArray();
        }

        public static LauncherGameInfo[] GetAllGames(string retrobatPath)
        {
            SimpleLogger.Instance.Info("[EPIC] Starting to fetch all games.");

            var allGames = new Dictionary<string, LauncherGameInfo>();
            var apiGames = new List<LauncherGameInfo>();

            SimpleLogger.Instance.Info("[EPIC] Instantiating EpicApi.");
            var api = new EpicApi();
            EpicToken token = null;

            string tokenPath = Path.Combine(retrobatPath, "user", "apikey", "epic.token");
            SimpleLogger.Instance.Info("[EPIC] Token path: " + tokenPath);

            string codePath = Path.Combine(retrobatPath, "user", "apikey", "epic.code");
            SimpleLogger.Instance.Info("[EPIC] Code path: " + codePath);

            if (File.Exists(codePath))
            {
                SimpleLogger.Instance.Info("[EPIC] epic.code file found.");
                try
                {
                    string authCode = File.ReadAllText(codePath).Trim();
                    SimpleLogger.Instance.Info("[EPIC] Read authCode from file.");

                    if (!string.IsNullOrEmpty(authCode))
                    {
                        SimpleLogger.Instance.Info("[EPIC] Authenticating with authorization code.");
                        token = api.AuthenticateWithAuthorizationCode(authCode);
                        SimpleLogger.Instance.Info("[EPIC] Authentication with authorization code finished. Token is " + (token == null ? "null" : "not null"));

                        if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
                        {
                            SimpleLogger.Instance.Info("[EPIC] Got refresh token. Writing to file.");
                            File.WriteAllText(tokenPath, token.RefreshToken);
                        }
                        else
                        {
                            SimpleLogger.Instance.Error("[EPIC] Authentication with authorization code failed. The code might be expired or invalid. Please get a new one.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Instance.Error("[EPIC] Error authenticating with authorization code: " + ex.Message, ex);
                }
                finally
                {
                    SimpleLogger.Instance.Info("[EPIC] Deleting epic.code file.");
                    try { File.Delete(codePath); } catch { }
                }
            }

            if (token == null && File.Exists(tokenPath))
            {
                SimpleLogger.Instance.Info("[EPIC] epic.token file found.");
                try
                {
                    string refreshToken = File.ReadAllText(tokenPath).Trim();
                    SimpleLogger.Instance.Info("[EPIC] Read refresh token from file.");
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        SimpleLogger.Instance.Info("[EPIC] Authenticating with refresh token.");
                        token = api.AuthenticateWithRefreshToken(refreshToken);
                        SimpleLogger.Instance.Info("[EPIC] Authentication with refresh token finished. Token is " + (token == null ? "null" : "not null"));
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Instance.Error("[EPIC] Error authenticating with refresh token: " + ex.Message, ex);
                }
            }

            if (token != null && !string.IsNullOrEmpty(token.AccessToken))
            {
                SimpleLogger.Instance.Info("[EPIC] Access token found. Getting library items.");
                try
                {
                    var libraryItems = api.GetLibraryItems(token.AccessToken, token.AccountId);
                    SimpleLogger.Instance.Info("[EPIC] Got " + (libraryItems == null ? "null" : libraryItems.Count.ToString()) + " library items from API.");

                    if (libraryItems != null)
                    {
                        foreach (var item in libraryItems)
                        {
                            if (item.Metadata != null && item.Metadata.MainGameItem != null && item.Id == item.Metadata.MainGameItem.Id)
                            {
                                apiGames.Add(new LauncherGameInfo
                                {
                                    Id = item.AppName,
                                    Name = item.Metadata.DisplayName,
                                    LauncherUrl = string.Format(GameLaunchUrl, item.AppName),
                                    Launcher = GameLauncherType.Epic
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Instance.Error("[EPIC] Error getting games from API: " + ex.Message, ex);
                }
            }

            SimpleLogger.Instance.Info("[EPIC] Found " + apiGames.Count + " games from API.");
            SimpleLogger.Instance.Info("[EPIC] Getting installed games.");
            var installedGames = GetInstalledGames(apiGames);
            SimpleLogger.Instance.Info("[EPIC] Found " + installedGames.Length + " installed games.");

            foreach (var game in installedGames)
            {
                if (!allGames.ContainsKey(game.Id))
                {
                    allGames.Add(game.Id, game);
                }
            }

            var nonInstalledGames = apiGames.Where(g => !allGames.ContainsKey(g.Id)).ToList();
            SimpleLogger.Instance.Info("[EPIC] Found " + nonInstalledGames.Count + " non-installed games.");

            foreach (var game in nonInstalledGames)
            {
                if (!allGames.ContainsKey(game.Id))
                {
                    allGames.Add(game.Id, game);
                }
            }

            SimpleLogger.Instance.Info("[EPIC] Total games to return: " + allGames.Count);
            return allGames.Values.ToArray();
        }


        static string AllUsersPath { get { return Path.Combine(Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "Epic"); } }

        public static bool IsInstalled
        {
            get
            {
                return File.Exists(GetExecutablePath());
            }
        }

        static string GetExecutablePath()
        {
            var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkCommand", null);
            if (modSdkMetadataDir != null)
                return modSdkMetadataDir.ToString();

            return null;
        }

        static string GetMetadataPath()
        {
            var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkMetadataDir", null);
            if (modSdkMetadataDir != null)
                return modSdkMetadataDir.ToString();

            return null;
        }

        static List<LauncherInstalled.InstalledApp> GetInstalledAppList()
        {
            var installListPath = Path.Combine(AllUsersPath, "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installListPath))
                return new List<LauncherInstalled.InstalledApp>();

            var list = JsonSerializer.DeserializeString<LauncherInstalled>(File.ReadAllText(installListPath));
            return list.InstallationList;
        }

        static IEnumerable<EpicGame> GetInstalledManifests()
        {
            var installListPath = GetMetadataPath();
            if (Directory.Exists(installListPath))
            {
                foreach (var manFile in Directory.GetFiles(installListPath, "*.item"))
                {
                    EpicGame manifest = null;

                    try { manifest = JsonSerializer.DeserializeString<EpicGame>(File.ReadAllText(manFile)); }
                    catch { }

                    if (manifest != null)
                        yield return manifest;
                }
            }
        }

    }    
}

namespace EmulatorLauncher.Common.Launchers.Epic
{
    [DataContract]
    public class LauncherInstalled
    {
        [DataContract]
        public class InstalledApp
        {
            [DataMember]
            public string InstallLocation { get; set; }
            [DataMember]
            public string AppName { get; set; }
            [DataMember]
            public long AppID { get; set; }
            [DataMember]
            public string AppVersion { get; set; }
        }

        [DataMember]
        public List<InstalledApp> InstallationList { get; set; }
    }

    [DataContract]
    public class EpicGame
    {
        [DataMember]
        public string AppName { get; set; }

        [DataMember]
        public string CatalogNamespace { get; set; }

        [DataMember]
        public string LaunchExecutable { get; set; }

        [DataMember]
        public string InstallLocation;

        [DataMember]
        public string MainGameAppName;

        [DataMember]
        public string DisplayName;

        [DataMember]
        public List<string> AppCategories { get; set; }
    }
}
