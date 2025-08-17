using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;
using Microsoft.Win32;
using System.Collections.Generic;
using Steam_Library_Manager.Framework;
using EmulatorLauncher.Common.EmulationStation;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        
        class SteamGameLauncher : GameLauncher
        {
            private string _steamID;
            private bool _gameWasJustInstalled = false;

            public SteamGameLauncher(Uri uri)
            {
                // Call method to get Steam executable
                string steamInternalDBPath = Path.Combine(Program.AppConfig.GetFullPath("retrobat"), "system", "tools", "steamexecutables.json");
                LauncherExe = SteamLibrary.GetSteamGameExecutableName(uri, steamInternalDBPath, out _steamID);
            }

            public override int RunAndWait(System.Diagnostics.ProcessStartInfo path)
            {
                bool isInstalled = IsGameInstalled(_steamID);
                if (isInstalled)
                    SimpleLogger.Instance.Info("[INFO] Game is already installed.");
                else
                    SimpleLogger.Instance.Info("[INFO] Game is not installed.");

                if (!isInstalled)
                {
                    if (Program.SystemConfig.getOptBoolean("steam.waitforinstall"))
                    {
                        SimpleLogger.Instance.Info("[INFO] 'waitforinstall' is enabled. Waiting for installation to complete.");
                        if (!WaitForInstall())
                        {
                            SimpleLogger.Instance.Error("[ERROR] Steam game installation failed or was cancelled.");
                            return -1;
                        }
                        _gameWasJustInstalled = true;

                        // After installation, find the executable name
                        var game = SteamLibrary.GetInstalledGames().FirstOrDefault(g => g.Id == _steamID);
                        if (game != null)
                            LauncherExe = game.ExecutableName;
                    }
                    else
                    {
                        // Launch the installation and return immediately.
                        // Note: Steam will still prompt for an installation directory if multiple libraries are set up.
                        // There is no known way to force a specific library via the steam://install protocol.
                        Process.Start(new ProcessStartInfo() { FileName = "steam://install/" + _steamID, UseShellExecute = true });
                        return 0;
                    }
                }

                // Check if steam is already running
                bool uiExists = Process.GetProcessesByName("steam").Any();
                if (uiExists)
                    SimpleLogger.Instance.Info("[INFO] Steam found running already.");
                else
                    SimpleLogger.Instance.Info("[INFO] Steam not yet running.");

                if (LauncherExe != null)
                {
                    SimpleLogger.Instance.Info("[INFO] Executable name : " + LauncherExe);

                    // Kill game if already running
                    KillExistingLauncherExes();

                    // Start game
                    Process.Start(path);

                    Process steamGame;
                    if (_gameWasJustInstalled)
                    {
                        SimpleLogger.Instance.Info("[INFO] Waiting indefinitely for game process to start after installation.");
                        steamGame = null;
                        while (steamGame == null)
                        {
                            steamGame = Process.GetProcessesByName(LauncherExe).FirstOrDefault();
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    else
                    {
                        // Get running game process (30 seconds delay 30x1000)
                        steamGame = GetLauncherExeProcess();
                    }

                    if (steamGame != null)
                    {
                        steamGame.WaitForExit();

                        // Kill steam if it was not running previously or if option is set in RetroBat
                        KillSteam(uiExists);
                    }
                    return 0;
                }
                else
                {
                    // Start game
                    Process.Start(path);

                    if (MonitorGameByRegistry(uiExists))
                        return 0;

                    SimpleLogger.Instance.Info("[INFO] Registry monitoring failed. Falling back to window focus detection.");
                    var gameProcess = FindGameProcessByWindowFocus();
                    if (gameProcess != null)
                    {
                        SimpleLogger.Instance.Info("[INFO] Game process '" + gameProcess.ProcessName + "' identified by window focus. Monitoring process.");
                        gameProcess.WaitForExit();
                        SimpleLogger.Instance.Info("[INFO] Game process has exited.");

                        // Kill steam if it was not running previously or if option is set in RetroBat
                        KillSteam(uiExists);
                    }
                    else
                        SimpleLogger.Instance.Info("[INFO] All fallback methods failed. Unable to monitor game process.");

                    return 0;
                }
            }

            private void KillSteam(bool uiExists)
            {
                if (Program.SystemConfig.getOptBoolean("killsteam"))
                    SimpleLogger.Instance.Info("[INFO] Option set to always kill Steam.");
                else if (Program.SystemConfig.isOptSet("killsteam"))
                    SimpleLogger.Instance.Info("[INFO] Option set to never kill Steam.");
                else
                    SimpleLogger.Instance.Info("[INFO] Steam will be killed if not running before.");

                // Kill steam if it was not running previously or if option is set in RetroBat
                if (Program.SystemConfig.getOptBoolean("killsteam"))
                {
                    foreach (var ui in Process.GetProcessesByName("steam"))
                    {
                        try { ui.Kill(); }
                        catch { }
                        SimpleLogger.Instance.Info("[INFO] Killing Steam.");
                    }
                }
                else if (!Program.SystemConfig.isOptSet("killsteam"))
                {
                    if (!uiExists)
                    {
                        foreach (var ui in Process.GetProcessesByName("steam"))
                        {
                            try { ui.Kill(); }
                            catch { }
                            SimpleLogger.Instance.Info("[INFO] Killing Steam.");
                        }
                    }
                }
            }

            private bool MonitorGameByRegistry(bool uiExists = false)
            {
                if (string.IsNullOrEmpty(_steamID))
                    return false;

                SimpleLogger.Instance.Info("[INFO] Monitoring registry for game start (AppID: " + _steamID + ").");

                // Wait for the game to be marked as running, with a 60-second timeout
                bool gameStarted = false;
                for (int i = 0; i < 60; i++)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + _steamID))
                        {
                            if (key != null && key.GetValue("Running") != null && (int)key.GetValue("Running") == 1)
                            {
                                gameStarted = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(1000);
                }

                if (!gameStarted)
                {
                    SimpleLogger.Instance.Info("[INFO] Timeout: Game did not appear as 'Running' in the registry.");
                    // Kill steam if it was not running previously or if option is set in RetroBat
                    KillSteam(uiExists);
                    return false;
                }

                SimpleLogger.Instance.Info("[INFO] Game detected as running. Monitoring registry for exit.");

                // Wait for the game to exit
                while (true)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + _steamID))
                        {
                            if (key == null || (key.GetValue("Running") != null && (int)key.GetValue("Running") == 0))
                                break;
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(1000);
                }

                SimpleLogger.Instance.Info("[INFO] Game has exited.");

                // Kill steam if it was not running previously or if option is set in RetroBat
                KillSteam(uiExists);

                return true;
            }

            private bool WaitForInstall()
            {
                if (string.IsNullOrEmpty(_steamID))
                    return false;

                SimpleLogger.Instance.Info("[INFO] Waiting for game installation/update to complete (AppID: " + _steamID + ").");

                Process.Start(new ProcessStartInfo() { FileName = "steam://install/" + _steamID, UseShellExecute = true });

                // Wait for the game to start updating/installing
                bool isUpdating = false;
                for (int i = 0; i < 120; i++) // 2 minutes timeout
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + _steamID))
                        {
                            if (key != null && key.GetValue("Updating") != null && (int)key.GetValue("Updating") == 1)
                            {
                                isUpdating = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(1000);
                }

                if (!isUpdating)
                {
                    SimpleLogger.Instance.Info("[INFO] Timeout: Game did not start installing/updating.");
                    return false;
                }

                SimpleLogger.Instance.Info("[INFO] Game is installing/updating. Monitoring registry for completion.");

                // Wait for the update to finish
                while (true)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + _steamID))
                        {
                            if (key == null || key.GetValue("Updating") == null || (int)key.GetValue("Updating") == 0)
                                break;
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(5000); // Check every 5 seconds
                }

                SimpleLogger.Instance.Info("[INFO] Game installation/update finished.");
                return true;
            }

            private bool IsGameInstalled(string steamID)
            {
                string steamPath = GetInstallPath();
                if (string.IsNullOrEmpty(steamPath))
                    return false;

                var libraryFolders = GetLibraryFolders();
                if (libraryFolders == null)
                    return false;

                foreach (var library in libraryFolders)
                {
                    string manifestPath = Path.Combine(library, "steamapps", "appmanifest_" + steamID + ".acf");
                    if (File.Exists(manifestPath))
                        return true;
                }

                return false;
            }

            private List<string> GetLibraryFolders()
            {
                string libraryfoldersPath = Path.Combine(GetInstallPath(), "config", "libraryfolders.vdf");

                try
                {
                    var libraryfolders = new KeyValue();
                    libraryfolders.ReadFileAsText(libraryfoldersPath);

                    var dbs = new List<string>();
                    foreach (var child in libraryfolders.Children)
                    {
                        int val;
                        if (int.TryParse(child.Name, out val))
                        {
                            if (!string.IsNullOrEmpty(child.Value) && Directory.Exists(child.Value))
                                dbs.Add(child.Value);
                            else if (child.Children != null && child.Children.Count > 0)
                            {
                                var path = child.Children.FirstOrDefault(a => a.Name != null && a.Name.Equals("path", StringComparison.OrdinalIgnoreCase) == true);
                                if (!string.IsNullOrEmpty(path.Value) && Directory.Exists(path.Value))
                                    dbs.Add(path.Value);
                            }
                        }
                    }
                    return dbs;
                }
                catch { }

                return new List<string>();
            }

            private string GetInstallPath()
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam"))
                    {
                        if (key != null)
                        {
                            var o = key.GetValue("InstallPath");
                            if (o != null)
                                return o as string;
                        }
                    }
                }
                catch { }

                return null;
            }
        }
    }
}
