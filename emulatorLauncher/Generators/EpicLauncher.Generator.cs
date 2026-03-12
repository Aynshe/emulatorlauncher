using System;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        class EpicGameLauncher : GameLauncher
        {
            public EpicGameLauncher(Uri uri)
            {
                LauncherExe = EpicLibrary.GetEpicGameExecutableName(uri);
            }

            public override int RunAndWait(ProcessStartInfo path)
            {
                bool uiExists = Process.GetProcessesByName("EpicGamesLauncher").Any();
                SimpleLogger.Instance.Info("[INFO] Executable name : " + LauncherExe);

                Process epicGame = GameSuspendMonitor.CheckAndResumeSuspendedGame(LauncherExe);
                if (epicGame != null)
                {
                    goto WaitAndExit;
                }

                KillExistingLauncherExes();

                Process.Start(path);

                epicGame = GetLauncherExeProcess();
                WaitAndExit:
                if (epicGame != null)
                {
                    Job.Current.AddProcess(epicGame);
                    Job.Current.CancelKillOnJobClose();
                    GameSuspendMonitor.WaitForProcessOrSuspend(epicGame, LauncherExe);

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
