﻿using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;
using EmulatorLauncher.Common.EmulationStation;
using Microsoft.Win32;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        
        class SteamGameLauncher : GameLauncher
        {
            private string _steamID;
            private LauncherGameInfo _game;

            public SteamGameLauncher(LauncherGameInfo game, Uri uri)
            {
                _game = game;

                // Call method to get Steam executable
                string steamInternalDBPath = Path.Combine(Program.AppConfig.GetFullPath("retrobat"), "system", "tools", "steamexecutables.json");
                LauncherExe = SteamLibrary.GetSteamGameExecutableName(uri, steamInternalDBPath, out _steamID);
            }

            public override int RunAndWait(System.Diagnostics.ProcessStartInfo path)
            {
                if (_game != null && !_game.IsInstalled)
                {
                    bool waitForInstall = Program.SystemConfig.getOptBoolean("steam.waitforinstall");
                    SimpleLogger.Instance.Info("[Steam] 'waitforinstall' feature is set to: " + waitForInstall);

                    if (waitForInstall)
                    {
                        if (!WaitForInstall())
                        {
                            SimpleLogger.Instance.Error("[ERROR] Steam game installation failed or was cancelled.");
                            return -1;
                        }
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo() { FileName = _game.InstallUrl, UseShellExecute = true });
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

                    // Get running game process (30 seconds delay 30x1000)
                    var steamGame = GetLauncherExeProcess();

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

                SimpleLogger.Instance.Info("[INFO] Waiting for game to be installed (AppID: " + _steamID + ").");

                Process.Start(new ProcessStartInfo() { FileName = _game.InstallUrl, UseShellExecute = true });

                // Wait for the game to be installed by checking for the 'Installed' registry key
                bool isInstalled = false;
                for (int i = 0; i < 3600; i++) // Timeout of 1 hour
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam\\Apps\\" + _steamID))
                        {
                            if (key != null && key.GetValue("Installed") != null && (int)key.GetValue("Installed") == 1)
                            {
                                isInstalled = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(1000); // Check every second
                }

                if (!isInstalled)
                {
                    SimpleLogger.Instance.Info("[INFO] Timeout: Game installation did not complete within the time limit.");
                    return false;
                }

                SimpleLogger.Instance.Info("[INFO] Game installation finished.");
                return true;
            }
        }
    }
}
