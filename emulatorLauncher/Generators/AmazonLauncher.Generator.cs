using System;
using System.Linq;
using System.Diagnostics;
using EmulatorLauncher.Common.Launchers;
using EmulatorLauncher.Common;

namespace EmulatorLauncher
{
    partial class ExeLauncherGenerator : Generator
    {
        class LocalFileGameLauncher : GameLauncher
        {
            public LocalFileGameLauncher(Uri uri)
            {
                LauncherExe = uri.LocalPath;
            }

            public override int RunAndWait(ProcessStartInfo path)
            {
                try
                {
                    var resumedGame = GameSuspendMonitor.CheckAndResumeSuspendedGame(LauncherExe);
                    if (resumedGame != null)
                    {
                        SimpleLogger.Instance.Info("Process : " + LauncherExe + " found, waiting to exit (Resume)");
                        Job.Current.AddProcess(resumedGame);
                        if (GameSuspendMonitor.WaitForProcessOrSuspend(resumedGame, LauncherExe))
                        {
                            Job.Current.CancelKillOnJobClose();
                        }
                        return 0;
                    }

                    var process = Process.Start(LauncherExe);
                    Job.Current.AddProcess(process);
                    if (GameSuspendMonitor.WaitForProcessOrSuspend(process, LauncherExe))
                    {
                        Job.Current.CancelKillOnJobClose();
                    }
                    return 0;
                }
                catch { }

                return -1;
            }
        }

        class AmazonGameLauncher : GameLauncher
        {
            public AmazonGameLauncher(Uri uri)
            {
                LauncherExe = AmazonLibrary.GetAmazonGameExecutableName(uri);
            }
           
            public override int RunAndWait(ProcessStartInfo path)
            {
                bool uiExists = Process.GetProcessesByName("Amazon Games UI").Any();
                SimpleLogger.Instance.Info("[INFO] Executable name : " + LauncherExe);

                var resumedGame = GameSuspendMonitor.CheckAndResumeSuspendedGame(LauncherExe);
                if (resumedGame != null)
                {
                    SimpleLogger.Instance.Info("Process : " + LauncherExe + " found, waiting to exit (Resume)");
                    Job.Current.AddProcess(resumedGame);
                    if (GameSuspendMonitor.WaitForProcessOrSuspend(resumedGame, LauncherExe))
                    {
                        Job.Current.CancelKillOnJobClose();
                    }
                    return 0;
                }

                KillExistingLauncherExes();

                Process.Start(path);

                var amazonGame = GetLauncherExeProcess();
                if (amazonGame != null)
                {
                    Job.Current.AddProcess(amazonGame);
                    if (GameSuspendMonitor.WaitForProcessOrSuspend(amazonGame, LauncherExe))
                    {
                        Job.Current.CancelKillOnJobClose();
                    }

                    if ((!uiExists && Program.SystemConfig["killsteam"] != "0") || (Program.SystemConfig.isOptSet("killsteam") && Program.SystemConfig.getOptBoolean("killsteam")))
                    {
                        foreach (var ui in Process.GetProcessesByName("Amazon Games UI"))
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
