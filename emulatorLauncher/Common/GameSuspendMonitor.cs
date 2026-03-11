using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EmulatorLauncher.Common
{
    public static class GameSuspendMonitor
    {
        // EN: P/Invoke for direct process suspension/resumption, bypassing the need for the backend process.
        // FR: P/Invoke pour suspendre/reprendre directement les processus, sans passer par le backend.
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern uint NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const uint TH32CS_SNAPPROCESS  = 0x00000002;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;
        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        private static List<int> GetChildPids(int parentPid)
        {
            var children = new List<int>();
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == new IntPtr(-1)) return children;
            try
            {
                PROCESSENTRY32 entry = new PROCESSENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (Process32First(snapshot, ref entry))
                    do { if (entry.th32ParentProcessID == parentPid) children.Add((int)entry.th32ProcessID); }
                    while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            return children;
        }

        private static void ResumeProcessTree(int pid)
        {
            IntPtr hProc = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
            if (hProc != IntPtr.Zero)
            {
                NtResumeProcess(hProc);
                CloseHandle(hProc);
            }
            foreach (int child in GetChildPids(pid))
                ResumeProcessTree(child);
        }

        private static void RestoreProcessWindows(int rootPid)
        {
            var pids = new HashSet<int>();
            var queue = new Queue<int>();
            pids.Add(rootPid); queue.Enqueue(rootPid);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int c in GetChildPids(cur))
                    if (pids.Add(c)) queue.Enqueue(c);
            }
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint wpid);
                if (pids.Contains((int)wpid) && IsWindowVisible(h))
                {
                    ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                }
                return true;
            }, IntPtr.Zero);
        }
        private static string GetRetroBatInstallPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\RetroBat"))
                {
                    if (key != null)
                    {
                        object path = key.GetValue("LatestKnownInstallPath");
                        if (path != null)
                            return path.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error($"[GameSuspendMonitor] Failed to read RetroBat registry: {ex.Message}");
            }
            return null;
        }

        private static string GetSuspendKeyPath(string exeName)
        {
            string retroBatPath = GetRetroBatInstallPath();
            if (string.IsNullOrEmpty(retroBatPath)) return null;

            string targetDir = Path.Combine(retroBatPath, "user", "SuspendedNTime");
            string exactPath = Path.Combine(targetDir, $"{exeName}.key");

            if (File.Exists(exactPath))
                return exactPath;

            // EN: If not found, try removing or adding .exe to handle slight name mismatches
            // FR: Si non trouvé, essayer avec ou sans l'extension .exe
            if (exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                string noExePath = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(exeName)}.key");
                if (File.Exists(noExePath)) return noExePath;
            }
            else
            {
                string withExePath = Path.Combine(targetDir, $"{exeName}.exe.key");
                if (File.Exists(withExePath)) return withExePath;
            }

            return exactPath;
        }

        public static bool _wasRestored = false;

        public static bool WasAnyGameSuspendedSince(DateTime time)
        {
            string retroBatPath = GetRetroBatInstallPath();
            if (string.IsNullOrEmpty(retroBatPath)) return false;

            string targetDir = Path.Combine(retroBatPath, "user", "SuspendedNTime");
            if (!Directory.Exists(targetDir)) return false;

            try
            {
                var files = new DirectoryInfo(targetDir).GetFiles("*.key");
                foreach (var file in files)
                {
                    if (file.CreationTime >= time || file.LastWriteTime >= time)
                        return true;
                }
            }
            catch { }

            return false;
        }

        public static Process CheckAndResumeSuspendedGame(string exeName)
        {
            string keyPath = GetSuspendKeyPath(exeName);
            if (string.IsNullOrEmpty(keyPath) || !File.Exists(keyPath))
                return null;
                
            try
            {
                string[] lines = File.ReadAllLines(keyPath);
                if (lines.Length >= 2 && int.TryParse(lines[0], out int pid))
                {
                    try
                    {
                        var p = Process.GetProcessById(pid);
                        if (p.HasExited) throw new Exception();
                    }
                    catch
                    {
                        // Le processus a crashe ou ete ferme manuellement pendant sa suspension
                        SimpleLogger.Instance.Info($"[GameSuspendMonitor] Suspended process for {exeName} is no longer running. Deleting key file.");
                        File.Delete(keyPath);
                        return null;
                    }
                }
            }
            catch { }

            SimpleLogger.Instance.Info($"[GameSuspendMonitor] Suspended game detected for {exeName}. Resuming...");

            // Overlay message
            Thread overlayThread = new Thread(() =>
            {
                using (Form overlay = new Form())
                {
                    overlay.FormBorderStyle = FormBorderStyle.None;
                    overlay.WindowState = FormWindowState.Maximized;
                    overlay.TopMost = true;
                    overlay.BackColor = Color.Black;
                    overlay.Opacity = 0.8;
                    overlay.ShowInTaskbar = false;

                    Label label = new Label();
                    label.Text = "Game suspended. Resuming...";
                    label.ForeColor = Color.White;
                    label.Font = new Font("Arial", 24, FontStyle.Bold);
                    label.AutoSize = true;
                    
                    overlay.Controls.Add(label);
                    
                    // Center the label
                    overlay.Load += (s, e) =>
                    {
                        label.Location = new Point(
                            (overlay.Width - label.Width) / 2,
                            (overlay.Height - label.Height) / 2
                        );
                    };

                    overlay.Show();
                    
                    // Close overlay after 3 seconds
                    var timer = new System.Windows.Forms.Timer { Interval = 3000 };
                    timer.Tick += (sender, args) =>
                    {
                        timer.Stop();
                        overlay.Close();
                    };
                    timer.Start();

                    Application.Run(overlay);
                }
            });
            
            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.Start();

            try
            {
                string[] lines = File.ReadAllLines(keyPath);
                if (lines.Length >= 1 && int.TryParse(lines[0], out int pid))
                {
                    _wasRestored = true;
                    SimpleLogger.Instance.Info($"[GameSuspendMonitor] Resuming PID {pid} directly via NtResumeProcess.");

                    // EN: Resume the process tree directly (no backend process needed).
                    // FR: Reprendre l'arbre des processus directement, sans passer par le backend.
                    ResumeProcessTree(pid);

                    // EN: Wait briefly for the process to wake up, then restore its windows.
                    // FR: Attendre brièvement que le processus se réveille, puis restaurer ses fenêtres.
                    Thread.Sleep(300);
                    RestoreProcessWindows(pid);

                    // EN: Delete the key file ourselves to signal a successful resume.
                    // FR: Supprimer le fichier clé pour signaler une reprise réussie.
                    try { File.Delete(keyPath); } catch { }

                    try
                    {
                        var gameProc = Process.GetProcessById(pid);
                        if (!gameProc.HasExited)
                            return gameProc;
                    }
                    catch { }

                    return Process.GetProcessesByName(exeName).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error($"[GameSuspendMonitor] Failed to resume process: {ex.Message}");
            }

            return Process.GetProcessesByName(exeName).FirstOrDefault();
        }

        public static bool WaitForProcessOrSuspend(Process game, string exeName)
        {
            while (!game.HasExited)
            {
                string keyPath = GetSuspendKeyPath(exeName);
                if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
                {
                    SimpleLogger.Instance.Info($"[GameSuspendMonitor] Key file detected for {exeName} at {keyPath}. Game was suspended. Treating as exit.");
                    NotifyEmulationStationReload();
                    return true;
                }

                game.WaitForExit(1000); // 1 second chunks for responsiveness
            }

            if (_wasRestored)
                NotifyEmulationStationReload();

            return false;
        }

        public static void NotifyEmulationStationReload()
        {
            try
            {
                SimpleLogger.Instance.Info("[GameSuspendMonitor] Scheduling EmulationStation reload via deferred batch script...");
                
                string batPath = Path.Combine(Path.GetTempPath(), "es_reload.bat");
                string batContent = 
@"@echo off
:loop
tasklist | find /i ""emulatorLauncher.exe"" >nul
if not errorlevel 1 (
    ping 127.0.0.1 -n 2 >nul
    goto loop
)
curl -s -m 2 http://127.0.0.1:1234/reloadgames >nul
del ""%~f0""
";
                File.WriteAllText(batPath, batContent);
                
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                SimpleLogger.Instance.Error($"[GameSuspendMonitor] Failed to schedule reload: {ex.Message}");
            }
        }
    }
}
