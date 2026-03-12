using System;
using System.Diagnostics;
using System.IO;
using EmulatorLauncher.Common;
using EmulatorLauncher.Common.EmulationStation;
using System.Linq;
using EmulatorLauncher.PadToKeyboard;

namespace EmulatorLauncher
{
    class SuspendedNTimeGenerator : Generator
    {
        private string _romPath;

        public override System.Diagnostics.ProcessStartInfo Generate(string system, string emulator, string core, string rom, string playersControllers, ScreenResolution resolution)
        {
            if (string.IsNullOrEmpty(rom) || !File.Exists(rom))
                return null;

            _romPath = rom;

            // EN: Return a fake ProcessStartInfo so emulatorLauncher doesn't crash saying "Emulator missing"
            // FR: Renvoyer un faux ProcessStartInfo pour que emulatorLauncher ne plante pas avec "Emulateur manquant"
            return new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = "/c exit",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
        }

        public override int RunAndWait(ProcessStartInfo path)
        {
            if (string.IsNullOrEmpty(_romPath)) return 1;

            string exeName = Path.GetFileNameWithoutExtension(_romPath);
            
            // Check if it's already running/resumed
            Process resumedGame = GameSuspendMonitor.CheckAndResumeSuspendedGame(exeName);
            if (resumedGame != null)
            {
                SimpleLogger.Instance.Info($"[SuspendedNTime] Successfully resumed {exeName} (PID: {resumedGame.Id})");
                
                // Add to job object to monitor execution
                Job.Current.AddProcess(resumedGame);
                
                // EN: Always cancel kill on close for resumed games, as we want them to persist
                // FR: Toujours annuler le kill à la fermeture pour les jeux repris, car on veut qu'ils persistent
                Job.Current.CancelKillOnJobClose();

                // Keep EmulatorLauncher alive until game exits or is suspended again
                GameSuspendMonitor.WaitForProcessOrSuspend(resumedGame, exeName);

                return 0; // Return success to emulatorLauncher
            }

            SimpleLogger.Instance.Error($"[SuspendedNTime] Could not find or resume a suspended game matching: {exeName}");
            return 1;
        }

        public override PadToKey SetupCustomPadToKeyMapping(PadToKey mapping)
        {
            if (string.IsNullOrEmpty(_romPath)) return mapping;

            string exeName = Path.GetFileNameWithoutExtension(_romPath);
            return PadToKey.AddOrUpdateKeyMapping(mapping, exeName, InputKey.hotkey | InputKey.start, "(%{KILL})");
        }
    }
}
