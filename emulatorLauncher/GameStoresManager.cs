using EmulatorLauncher.Common;
using EmulatorLauncher.Common.Launchers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Forms;

namespace EmulatorLauncher
{
    class GameStoresManager
    {
        public static void UpdateGames()
        {
            if (Program.SystemConfig.isOptSet("scanStore") && Program.SystemConfig["scanStore"] == "0")
            {
                SimpleLogger.Instance.Info("[ImportStore] Option to scan installed store games is disabled.");
                return;
            }

            var retrobatPath = Program.AppConfig.GetFullPath("retrobat");

            Parallel.Invoke(
               () => ImportStore("amazon", AmazonLibrary.GetInstalledGames),
               () => ImportStore("eagames", EaGamesLibrary.GetInstalledGames),
               () => ImportStore("epic", () => EpicLibrary.GetAllGames(retrobatPath)),
               () => ImportStore("gog", GogLibrary.GetInstalledGames),
               () => ImportStore("steam", () => SteamLibrary.GetAllGames(retrobatPath)));
        }

        private static void ImportStore(string name, Func<LauncherGameInfo[]> getGames)
        {
            try
            {
                var roms = Program.AppConfig.GetFullPath("roms");

                var dir = Path.Combine(roms, name);
                Directory.CreateDirectory(dir);

                var notInstalledDir = Path.Combine(dir, "Not Installed");
                if (name == "steam" || name == "epic")
                    Directory.CreateDirectory(notInstalledDir);

                var files = new HashSet<string>(new[] { "*.url", "*.lnk" }.SelectMany(ext => Directory.GetFiles(dir, ext, SearchOption.TopDirectoryOnly)));
                var notInstalledFiles = (name == "steam" || name == "epic") ? new HashSet<string>(new[] { "*.url", "*.lnk" }.SelectMany(ext => Directory.GetFiles(notInstalledDir, ext, SearchOption.TopDirectoryOnly))) : new HashSet<string>();

                dynamic shell = null;

                foreach (var game in getGames())
                {
                    try
                    {
                        var targetDir = dir;
                        var targetFiles = files;

                        if ((name == "steam" || name == "epic") && !game.IsInstalled)
                        {
                            targetDir = notInstalledDir;
                            targetFiles = notInstalledFiles;
                        }

                        Uri uri = new Uri(game.LauncherUrl);

                        string gameName = RemoveInvalidFileNameChars(game.Name);

                        string path = Path.Combine(targetDir, gameName + ".url");
                        if (uri.Scheme == "file")
                            path = Path.Combine(targetDir, gameName + ".lnk");

                        if (targetFiles.Contains(path))
                        {
                            targetFiles.Remove(path);
                            continue;
                        }

                        if (uri.Scheme == "file")
                        {
                            try
                            {
                                if (shell == null)
                                    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));

                                dynamic shortcut = shell.CreateShortcut(path);
                                shortcut.TargetPath = game.LauncherUrl;
                                shortcut.Arguments = game.Parameters;
                                shortcut.WorkingDirectory = game.InstallDirectory;

                                if (!string.IsNullOrEmpty(game.IconPath))
                                    shortcut.IconLocation = game.IconPath;

                                shortcut.Save();

                                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                                continue;
                            }
                            catch { }
                        }

                        if (!string.IsNullOrEmpty(game.IconPath))
                        {
                            var iconline = "IconFile=" + game.IconPath + "\r\n";
                            string content = "[InternetShortcut]\r\n" + $"URL={game.LauncherUrl}\r\n" + iconline + "IconIndex=0" ;
                            File.WriteAllText(path, content);
                            
                        }
                        else
                            File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + game.LauncherUrl);
                    }
                    catch (Exception ex) { SimpleLogger.Instance.Error("[ImportStore] " + name + " : " + ex.Message, ex); }
                }

                if (shell != null)
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);

                if (!Program.SystemConfig.getOptBoolean("storekeep"))
                {
                    foreach (var file in files)
                        FileTools.TryDeleteFile(file);

                    if (name == "steam" || name == "epic")
                    {
                        foreach (var file in notInstalledFiles)
                            FileTools.TryDeleteFile(file);
                    }
                }
            }

            catch (Exception ex) { SimpleLogger.Instance.Error("[ImportStore] " + name + " : " + ex.Message, ex); }
        }

        private static string RemoveInvalidFileNameChars(string x)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return new string(x.Where(c => !invalidChars.Contains(c)).ToArray());
        }
    }
}
