using System;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;
using System.Text.RegularExpressions;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        class EpicGameLauncher : GameLauncher
        {
            private bool _isInstalled;

            public EpicGameLauncher(Uri uri)
            {
                var match = Regex.Match(uri.ToString(), @"apps/([^?]+)");
                if (match.Success)
                {
                    string appName = match.Groups[1].Value;
                    _isInstalled = EpicLibrary.IsGameInstalled(appName);
                    if (_isInstalled)
                    {
                        LauncherExe = EpicLibrary.GetEpicGameExecutableName(uri);
                    }
                }
            }

            public override int RunAndWait(ProcessStartInfo path)
            {
                if (!_isInstalled)
                {
                    Process.Start(path);
                    return 0;
                }

                bool uiExists = Process.GetProcessesByName("EpicGamesLauncher").Any();
                SimpleLogger.Instance.Info("[INFO] Executable name : " + LauncherExe);
                KillExistingLauncherExes();

                Process.Start(path);

                var epicGame = GetLauncherExeProcess();
                if (epicGame != null)
                {
                    epicGame.WaitForExit();

                    if ((!uiExists && Program.SystemConfig["killsteam"] != "0") || (Program.SystemConfig.isOptSet("killsteam") && Program.SystemConfig.getOptBoolean("killsteam")))
                    {
                        foreach (var ui in Process.GetProcessesByName("EpicGamesLauncher"))
                        {
                            try { ui.Kill(); }
                            catch { }
                        }
                    }
                }

                return 0;
            }
        }
    }
}
