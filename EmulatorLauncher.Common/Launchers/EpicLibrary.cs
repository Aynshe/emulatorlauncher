using EmulatorLauncher.Common.FileFormats;
using EmulatorLauncher.Common.Launchers.Epic;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace EmulatorLauncher.Common.Launchers
{
    public class EpicLibrary
    {
        const string GameLaunchUrl = @"com.epicgames.launcher://apps/{0}?action=launch&silent=true";
        const string GameInstallUrl = @"com.epicgames.launcher://apps/{0}?action=install";

        public static LauncherGameInfo[] GetAllGames(string retrobatPath)
        {
            var allGames = new Dictionary<string, LauncherGameInfo>();
            var apiGames = new List<EpicLibraryItem>();

            SimpleLogger.Instance.Info("[Epic] Getting Epic games.");
            var watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var api = new EpicApi();
                EpicToken token = null;

                string tokenPath = Path.Combine(retrobatPath, "user", "apikey", "epic.token");
                string codePath = Path.Combine(retrobatPath, "user", "apikey", "epic.code");

                if (File.Exists(codePath))
                {
                    try
                    {
                        string authCode = File.ReadAllText(codePath).Trim();
                        if (!string.IsNullOrEmpty(authCode))
                        {
                            token = api.AuthenticateWithAuthorizationCode(authCode);
                            if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
                            {
                                File.WriteAllText(tokenPath, token.RefreshToken);
                            }
                            else
                            {
                                SimpleLogger.Instance.Error("[EPIC] Authentication with authorization code failed. The code might be expired or invalid.");
                            }
                        }
                    }
                    finally
                    {
                        try { File.Delete(codePath); } catch { }
                    }
                }

                if (token == null && File.Exists(tokenPath))
                {
                    string refreshToken = File.ReadAllText(tokenPath).Trim();
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        token = api.AuthenticateWithRefreshToken(refreshToken);
                        if (token != null && !string.IsNullOrEmpty(token.RefreshToken))
                            File.WriteAllText(tokenPath, token.RefreshToken);
                    }
                }

                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    SimpleLogger.Instance.Info("[Epic] Epic API key found. Getting games from API.");
                    apiGames = api.GetLibraryItems(token.AccessToken, token.AccountId);
                    SimpleLogger.Instance.Info("[Epic] Found " + apiGames.Count + " games from API.");
                }
                else
                {
                     SimpleLogger.Instance.Info("[Epic] Could not get an access token. Only installed games will be listed.");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error("[EPIC] Error getting games from API: " + ex.Message, ex);
            }

            var apiGamesInfo = apiGames.Select(g => new LauncherGameInfo { Id = g.AppName, Name = g.Metadata.DisplayName, Launcher = GameLauncherType.Epic }).ToDictionary(g => g.Id);
            var installedGames = GetInstalledGames(apiGamesInfo.Values.ToList());
            SimpleLogger.Instance.Info("[Epic] Found " + installedGames.Length + " installed games.");

            foreach (var game in installedGames)
            {
                if (!allGames.ContainsKey(game.Id))
                {
                    allGames.Add(game.Id, game);
                    game.IsInstalled = true;
                }
            }

            var nonInstalledGames = apiGamesInfo.Values.Where(g => !allGames.ContainsKey(g.Id)).ToList();
            SimpleLogger.Instance.Info("[Epic] Found " + nonInstalledGames.Count + " non-installed games.");

            foreach (var game in nonInstalledGames)
            {
                if (!allGames.ContainsKey(game.Id))
                {
                    game.LauncherUrl = string.Format(GameInstallUrl, game.Id);
                    allGames.Add(game.Id, game);
                }
            }

            watch.Stop();
            SimpleLogger.Instance.Info("[Epic] Import process finished in " + watch.ElapsedMilliseconds + " ms.");

            return allGames.Values.ToArray();
        }

        private static LauncherGameInfo[] GetInstalledGames(List<LauncherGameInfo> apiGames)
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
                if (manifest == null || manifest.AppName != manifest.MainGameAppName)
                    continue;

                if (manifest.AppCategories != null && manifest.AppCategories.Any(a => a == "plugins" || a == "plugins/engine"))
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
                        if (shorturl.Equals(manifest.AppName) || shorturl.Equals(manifest.MainGameAppName))
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

        public static bool IsGameInstalled(string appName)
        {
            var manifests = GetInstalledManifests();
            if (manifests == null)
                return false;

            return manifests.Any(m => m.AppName == appName);
        }

        private static string AllUsersPath { get { return Path.Combine(Environment.ExpandEnvironmentVariables("%PROGRAMDATA%"), "Epic"); } }
        public static bool IsInstalled { get { return File.Exists(GetExecutablePath()); } }
        private static string GetExecutablePath()
        {
            var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkCommand", null);
            return modSdkMetadataDir != null ? modSdkMetadataDir.ToString() : null;
        }
        private static string GetMetadataPath()
        {
            var modSdkMetadataDir = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Epic Games\\EOS", "ModSdkMetadataDir", null);
            return modSdkMetadataDir != null ? modSdkMetadataDir.ToString() : null;
        }
        private static List<LauncherInstalled.InstalledApp> GetInstalledAppList()
        {
            var installListPath = Path.Combine(AllUsersPath, "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installListPath))
                return new List<LauncherInstalled.InstalledApp>();
            var list = JsonSerializer.DeserializeString<LauncherInstalled>(File.ReadAllText(installListPath));
            return list.InstallationList;
        }
        private static IEnumerable<EpicGame> GetInstalledManifests()
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
            [DataMember] public string InstallLocation { get; set; }
            [DataMember] public string AppName { get; set; }
            [DataMember] public long AppID { get; set; }
            [DataMember] public string AppVersion { get; set; }
        }
        [DataMember] public List<InstalledApp> InstallationList { get; set; }
    }
    [DataContract]
    public class EpicGame
    {
        [DataMember] public string AppName { get; set; }
        [DataMember] public string CatalogNamespace { get; set; }
        [DataMember] public string LaunchExecutable { get; set; }
        [DataMember] public string InstallLocation;
        [DataMember] public string MainGameAppName;
        [DataMember] public string DisplayName;
        [DataMember] public List<string> AppCategories { get; set; }
    }
}
