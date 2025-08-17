using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;
using Microsoft.Win32;
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
                bool isInstalled = SteamLibrary.IsGameInstalled(_steamID, out bool isUpdating);
                bool waitForInstall = Program.SystemConfig.getOptBoolean("steam.waitforinstall");

                if (!isInstalled && !waitForInstall)
                {
                    SimpleLogger.Instance.Info("[Steam] Game is not installed. Starting installation in background.");
                    Process.Start(path);
                    return 0;
                }

                bool uiExists = Process.GetProcessesByName("steam").Any();

                if ((!isInstalled || isUpdating) && waitForInstall)
                {
                    if (!isInstalled)
                        SimpleLogger.Instance.Info("[Steam] Game is not installed. Waiting for installation to complete.");
                    else
                        SimpleLogger.Instance.Info("[Steam] Game is updating. Waiting for update to complete.");

                    Process.Start(path);

                    while (true)
                    {
                        Thread.Sleep(15000);
                        isInstalled = SteamLibrary.IsGameInstalled(_steamID, out isUpdating);

                        if (isInstalled && !isUpdating)
                        {
                            SimpleLogger.Instance.Info("[Steam] Game is now installed and ready to be launched.");
                            break;
                        }

                        if (isUpdating)
                            SimpleLogger.Instance.Info("[Steam] Game is still updating...");
                        else if (!isInstalled)
                            SimpleLogger.Instance.Info("[Steam] Game is still installing...");
                    }

                    return MonitorAndExit(uiExists, path, false);
                }

                if (uiExists)
                    SimpleLogger.Instance.Info("[INFO] Steam found running already.");
                else
                    SimpleLogger.Instance.Info("[INFO] Steam not yet running.");

                if (LauncherExe != null)
                {
                    SimpleLogger.Instance.Info("[INFO] Executable name : " + LauncherExe);
                    KillExistingLauncherExes();
                    Process.Start(path);
                    var steamGame = GetLauncherExeProcess();
                    if (steamGame != null)
                    {
                        steamGame.WaitForExit();
                        KillSteam(uiExists);
                    }
                    return 0;
                }
                else
                {
                    return MonitorAndExit(uiExists, path);
                }
            }

            private int MonitorAndExit(bool uiExists, System.Diagnostics.ProcessStartInfo path, bool launch = true)
            {
                if (launch)
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
                    KillSteam(uiExists);
                }
                else
                    SimpleLogger.Instance.Info("[INFO] All fallback methods failed. Unable to monitor game process.");

                return 0;
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
