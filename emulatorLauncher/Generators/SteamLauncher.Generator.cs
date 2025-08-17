using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;
using Microsoft.Win32;
using EmulatorLauncher.Common.EmulationStation;
using System.Collections.Generic;
using System.Threading;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        
        class SteamGameLauncher : GameLauncher
        {
            private string _steamID;
            public SteamGameLauncher(Uri uri)
            {
                // Call method to get Steam executable
                string steamInternalDBPath = Path.Combine(Program.AppConfig.GetFullPath("retrobat"), "system", "tools", "steamexecutables.json");
                LauncherExe = SteamLibrary.GetSteamGameExecutableName(uri, steamInternalDBPath, out _steamID);
            }

            public override int RunAndWait(System.Diagnostics.ProcessStartInfo path)
            {
                bool isInstalled = SteamLibrary.GetInstalledGames().Any(g => g.Id == _steamID);

                if (!isInstalled)
                {
                    SimpleLogger.Instance.Info("[INFO] Game with SteamID " + _steamID + " is not installed.");

                    // Launch the installation. Note: There is no known way to force a specific Steam library folder via the steam://install protocol.
                    // Steam will prompt the user for a drive if multiple libraries are set up.
                    Process.Start(new ProcessStartInfo() { FileName = "steam://install/" + _steamID, UseShellExecute = true });

                    if (Program.SystemConfig.getOptBoolean("steam.waitforinstall"))
                    {
                        SimpleLogger.Instance.Info("[INFO] 'steam.waitforinstall' is enabled. Waiting for installation to complete.");

                        if (WaitForInstall(_steamID))
                        {
                            SimpleLogger.Instance.Info("[INFO] Installation complete. Launching game.");

                            var game = SteamLibrary.GetInstalledGames().FirstOrDefault(g => g.Id == _steamID);
                            if (game != null && !string.IsNullOrEmpty(game.ExecutablePath) && !string.IsNullOrEmpty(game.InstallDirectory))
                            {
                                bool uiExists = Process.GetProcessesByName("steam").Any();

                                var newPath = new ProcessStartInfo()
                                {
                                    FileName = game.ExecutablePath,
                                    WorkingDirectory = game.InstallDirectory
                                };

                                Process.Start(newPath);

                                var gameProcess = GetLauncherExeProcess(game.ExecutableName);
                                if (gameProcess != null)
                                {
                                    gameProcess.WaitForExit();
                                    KillSteam(uiExists);
                                }
                                return 0;
                            }
                            else
                            {
                                SimpleLogger.Instance.Error("[ERROR] Game was installed, but could not find executable path. Cannot launch.");
                                return -1;
                            }
                        }
                        else
                        {
                            SimpleLogger.Instance.Error("[ERROR] Steam game installation failed or was cancelled by user.");
                            return -1;
                        }
                    }
                    else
                    {
                        SimpleLogger.Instance.Info("[INFO] 'steam.waitforinstall' is disabled. Returning to UI.");
                        return 0;
                    }
                }

                // Check if steam is already running
                bool uiExistsOriginal = Process.GetProcessesByName("steam").Any();
                if (uiExistsOriginal)
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

                    // Get running game process (30 seconds delay 30x1000)
                    var steamGame = GetLauncherExeProcess();

                    if (steamGame != null)
                    {
                        steamGame.WaitForExit();

                        // Kill steam if it was not running previously or if option is set in RetroBat
                        KillSteam(uiExistsOriginal);
                    }
                    return 0;
                }
                else
                {
                    // Start game
                    Process.Start(path);

                    if (MonitorGameByRegistry(uiExistsOriginal))
                        return 0;

                    SimpleLogger.Instance.Info("[INFO] Registry monitoring failed. Falling back to window focus detection.");
                    var gameProcess = FindGameProcessByWindowFocus();
                    if (gameProcess != null)
                    {
                        SimpleLogger.Instance.Info("[INFO] Game process '" + gameProcess.ProcessName + "' identified by window focus. Monitoring process.");
                        gameProcess.WaitForExit();
                        SimpleLogger.Instance.Info("[INFO] Game process has exited.");

                        // Kill steam if it was not running previously or if option is set in RetroBat
                        KillSteam(uiExistsOriginal);
                    }
                    else
                        SimpleLogger.Instance.Info("[INFO] All fallback methods failed. Unable to monitor game process.");

                    return 0;
                }
            }

            private bool WaitForInstall(string steamID)
            {
                if (string.IsNullOrEmpty(steamID))
                    return false;

                SimpleLogger.Instance.Info("[INFO] Waiting for game installation/update to start (AppID: " + steamID + "). This may take time if user interaction is required in Steam.");

                bool isUpdating = false;
                for (int i = 0; i < 120; i++) // 2 minutes timeout for user to start the install
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + steamID))
                        {
                            if (key != null && key.GetValue("Updating") != null && (int)key.GetValue("Updating") == 1)
                            {
                                isUpdating = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    Thread.Sleep(1000);
                }

                if (!isUpdating)
                {
                    SimpleLogger.Instance.Info("[INFO] Timeout: Game did not start installing/updating within 2 minutes.");
                    return false;
                }

                SimpleLogger.Instance.Info("[INFO] Game is installing/updating. Monitoring registry for completion.");

                while (true)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + steamID))
                        {
                            if (key == null || key.GetValue("Updating") == null || (int)key.GetValue("Updating") == 0)
                                break;
                        }
                    }
                    catch { }
                    Thread.Sleep(5000);
                }

                SimpleLogger.Instance.Info("[INFO] Game installation/update finished.");
                return true;
            }

            protected Process GetLauncherExeProcess(string exeName)
            {
                Process launcherprocess = null;

                int waitttime = 30;
                if (Program.SystemConfig.isOptSet("steam_wait") && !string.IsNullOrEmpty(Program.SystemConfig["steam_wait"]))
                    waitttime = Program.SystemConfig["steam_wait"].ToInteger();

                SimpleLogger.Instance.Info("[INFO] Waiting " + waitttime.ToString() + " seconds for game process '" + exeName + "' to appear.");

                for (int i = 0; i < waitttime; i++)
                {
                    launcherprocess = Process.GetProcessesByName(exeName).FirstOrDefault();
                    if (launcherprocess != null)
                    {
                        SimpleLogger.Instance.Info("[INFO] Game process found.");
                        break;
                    }

                    Thread.Sleep(1000);
                }

                if (launcherprocess == null)
                    SimpleLogger.Instance.Info("[INFO] Game process did not appear in time.");

                return launcherprocess;
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
        }
    }
}
