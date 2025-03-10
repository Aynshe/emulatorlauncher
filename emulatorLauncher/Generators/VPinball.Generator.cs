﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using EmulatorLauncher.VPinballLauncher;
using System.Xml;
using System.Xml.Linq;
using System.Drawing;
using Microsoft.Win32;
using System.Windows.Forms;
using EmulatorLauncher.Common;
using EmulatorLauncher.PadToKeyboard;
using EmulatorLauncher.Common.EmulationStation;
using EmulatorLauncher.Common.FileFormats;
using System.Drawing.Imaging;
using EmulatorLauncher.Common.Joysticks;
using System.Configuration;

namespace EmulatorLauncher
{
    class VPinballGenerator : Generator
    {
        public VPinballGenerator()
        {
            DependsOnDesktopResolution = true;
        }

        private LoadingForm _splash;       
        private Version _version;
        private string _processName;
        private string _exe;
        private string _gamePath;

        public override System.Diagnostics.ProcessStartInfo Generate(string system, string emulator, string core, string rom, string playersControllers, ScreenResolution resolution)
        {
            string path = AppConfig.GetFullPath("vpinball");
            if (path == null)
                return null;

            string exe = Path.Combine(path, Environment.Is64BitOperatingSystem ? "VPinballX64.exe" : "VPinballX.exe");
            if (!File.Exists(exe) && Environment.Is64BitOperatingSystem)
                exe = Path.Combine(path, "VPinballX.exe");
            if (!File.Exists(exe))
                return null;

            _exe = exe;
            _processName = Path.GetFileNameWithoutExtension(exe);
            _version = new Version(10, 0, 0, 0);
            _gamePath = Path.GetDirectoryName(rom);

            // Get version from executable
            var versionInfo = FileVersionInfo.GetVersionInfo(exe);
            string versionString = versionInfo.FileMajorPart + "." + versionInfo.FileMinorPart + "." + versionInfo.FileBuildPart + "." + versionInfo.FilePrivatePart;
            Version.TryParse(versionString, out _version);

            rom = this.TryUnZipGameIfNeeded(system, rom, true, false);
            if (Directory.Exists(rom))
            {
                rom = Directory.GetFiles(rom, "*.vpx").FirstOrDefault();
                if (string.IsNullOrEmpty(rom))
                    return null;
            }

            _splash = ShowSplash(rom);

            ScreenResolution.SetHighDpiAware(exe);
            EnsureUltraDMDRegistered(path);
            EnsureBackglassServerRegistered(path);
            EnsureVPinMameRegistered(path);
            EnsurePinupPlayerRegistered(path);
            EnsurePinupDOFRegistered(path);
            EnsurePupServerRegistered(path);
            EnsurePupDMDControlRegistered(path);

            string romPath = Path.Combine(Path.GetDirectoryName(rom), "roms");
            if (!Directory.Exists(romPath))
                romPath = Path.Combine(Path.GetDirectoryName(rom), ".roms");
            if (!Directory.Exists(romPath))
                romPath = Path.Combine(AppConfig.GetFullPath("roms"), "vpinball", "roms");
            if (!Directory.Exists(romPath))
                romPath = Path.Combine(AppConfig.GetFullPath("roms"), "vpinball", ".roms");
            if (!Directory.Exists(romPath))
                romPath = Path.Combine(AppConfig.GetFullPath("vpinball"), "VPinMAME", "roms");
            if (!Directory.Exists(romPath))
            {
                romPath = Path.Combine(AppConfig.GetFullPath("roms"), "vpinball", "roms");
                try { Directory.CreateDirectory(romPath); } catch { SimpleLogger.Instance.Error("[ERROR] Missing roms subfolder in roms\vpinball folder."); }
            }

            SimpleLogger.Instance.Info("[INFO] using rompath: " + romPath);

            ScreenRes sr = ScreenRes.Load(Path.GetDirectoryName(rom));
            if (sr != null)
            {
                sr.ScreenResX = resolution == null ? Screen.PrimaryScreen.Bounds.Width : resolution.Width;
                sr.ScreenResY = resolution == null ? Screen.PrimaryScreen.Bounds.Height : resolution.Height;

                Screen secondary = Screen.AllScreens.FirstOrDefault(s => !s.Primary);
                if (secondary != null)
                {
                    sr.Screen2ResX = secondary.Bounds.Width;
                    sr.Screen2ResY = secondary.Bounds.Height;
                }
                sr.Monitor = Screen.AllScreens.Length == 1 ? 1 : 2;

                sr.Save();
            }

            SetupOptions(path, romPath, resolution);
            SetupB2STableSettings(path);

            var commands = new List<string>();

            if (_version >= new Version(10, 7, 0, 0))
                commands.Add("-ExtMinimized");

            if (_version >= new Version(10, 8, 0, 0))
            {
                commands.Add("-Ini");
                commands.Add(Path.Combine(path, "VPinballX.ini"));

            }
            commands.Add("-play");
            commands.Add(rom);

            string args = string.Join(" ", commands.Select(a => a.Contains(" ") ? "\"" + a + "\"" : a).ToArray());

            return new ProcessStartInfo()
            {
                FileName = exe,
                Arguments = args,
                WindowStyle = _splash != null ? ProcessWindowStyle.Minimized : ProcessWindowStyle.Normal,
                UseShellExecute = true
            };
        }

        public override PadToKey SetupCustomPadToKeyMapping(PadToKey mapping)
        {
            return PadToKey.AddOrUpdateKeyMapping(mapping, _processName, InputKey.hotkey | InputKey.r3, () => SaveScreenshot());
        }
         
        public override int RunAndWait(ProcessStartInfo path)
        {
            try
            {
                var px = Process.Start(path);

                using (var kb = new KeyboardManager(() => KillProcess(px)))
                {
                    kb.RegisterKeyboardAction(() => SaveScreenshot(), (vkCode, scanCode) => vkCode == 44 && scanCode == 55);

                    while (!px.HasExited)
                    {
                        if (px.WaitForExit(10))
                            break;

                        Application.DoEvents();
                    }
                }
                
                try
                {
                    Process[] backGlasses = Process.GetProcessesByName("B2SBackglassServerEXE");
                    foreach (Process backGlass in backGlasses)
                        backGlass.Kill();

                    Process[] ultraDMDs = Process.GetProcessesByName("UltraDMD");
                    foreach (Process ultraDMD in ultraDMDs)
                        ultraDMD.Kill();

                    Process[] pupDisplays = Process.GetProcessesByName("PinUpDisplay");
                    foreach (Process pupDisplay in pupDisplays)
                        pupDisplay.Kill();

                    Process[] pupPlayers = Process.GetProcessesByName("PinUpPlayer");
                    foreach (Process pupPlayer in pupPlayers)
                        pupPlayer.Kill();
                }
                catch { }

                int exitCode = px.ExitCode;

                // vpinball always returns -1 when exiting
                if (exitCode == -1)
                    return 0;

                return exitCode;
            }
            catch 
            { 

            }

            return -1;
        }

        public override void Cleanup()
        {
            if (_splash != null)
            {
                _splash.Dispose();
                _splash = null;
            }

            base.Cleanup();
        }

        private static void SaveScreenshot()
        {
            if (!ScreenCapture.AddScreenCaptureToGameList(Program.SystemConfig["system"], Program.SystemConfig["rom"]))
            {
                string path = Program.AppConfig.GetFullPath("screenshots");
                if (!Directory.Exists(path))
                    return;

                int index = 0;
                string fn;

                do
                {
                    fn = Path.Combine(path, Path.GetFileNameWithoutExtension(Program.SystemConfig["rom"]) + (index == 0 ? "" : "_" + index.ToString()) + ".jpg");
                    index++;
                } 
                while (File.Exists(fn));

                ScreenCapture.CaptureScreen(fn);
            }
        }

        private static void KillProcess(Process px)
        {
            try { px.Kill(); }
            catch { }
        }

        private static LoadingForm ShowSplash(string rom)
        {
            if (rom == null)
                return null;

            string fn = Path.ChangeExtension(rom, ".directb2s");

            var data = DirectB2sData.FromFile(fn);
            if (data != null)
            {
                int last = Environment.TickCount;
                int index = 0;

                var frm = new LoadingForm
                {
                    Image = data.RenderBackglass(index)
                };
                frm.Timer += (a, b) =>
                    {
                        int now = Environment.TickCount;
                        if (now - last > 1000)
                        {
                            index++;
                            if (index >= 4)
                                index = 0;

                            frm.Image = data.RenderBackglass(index);
                            frm.Invalidate();
                            last = now;
                        }

                    };
                frm.Show();

                return frm;
            }

            return null;
        }

        private static bool FileUrlValueExists(object value)
        {
            if (value == null)
                return false;

            try
            {
                string localPath = new Uri(value.ToString()).LocalPath;
                if (File.Exists(localPath))
                    return true;
            }
            catch { }

            return false;
        }

        private static bool IsComServerAvailable(string name)
        {
            RegistryKey key = Registry.ClassesRoot.OpenSubKey(name, false);
            if (key == null)
                return false;

            object defaultValue = key.GetValue(null);

            if (!"mscoree.dll".Equals(defaultValue) && FileUrlValueExists(key.GetValue(null)))
            {
                key.Close();
                return true;
            }

            if ("mscoree.dll".Equals(defaultValue) && FileUrlValueExists(key.GetValue("CodeBase")))
            {
                key.Close();
                return true;
            }

            key.Close();
            return false;
        }

        private static void EnsureUltraDMDRegistered(string path)
        {
            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring UltraDMD is registered.");

                // Check for valid out-of-process COM server ( UltraDMD ) 
                if (IsComServerAvailable(@"CLSID\{E1612654-304A-4E07-A236-EB64D6D4F511}\LocalServer32"))
                    return;

                // Check for valid in-process COM server ( FlexDMD )
                if (IsComServerAvailable(@"CLSID\{E1612654-304A-4E07-A236-EB64D6D4F511}\InprocServer32"))
                    return;
                
                string ultraDMD = Path.Combine(path, "UltraDMD", "UltraDMD.exe");
                if (!File.Exists(ultraDMD))
                    ultraDMD = Path.Combine(path, "XDMD", "UltraDMD.exe");

                if (File.Exists(ultraDMD))
                {
                    Process px = new Process
                    {
                        EnableRaisingEvents = true
                    };
                    px.StartInfo.Verb = "RunAs";
                    px.StartInfo.FileName = ultraDMD;
                    px.StartInfo.Arguments = " /i";
                    px.StartInfo.UseShellExecute = true;
                    px.StartInfo.CreateNoWindow = true;
                    px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                    px.Start();
                    px.WaitForExit();
                }
            }
            catch { }
        }

        private static string ReadRegistryValue(RegistryKeyEx key, string path, string value, RegistryViewEx view = RegistryViewEx.Registry32)
        {
            var regKeyc = key.OpenSubKey(path, view);
            if (regKeyc != null)
            {
                object pth = regKeyc.GetValue(value);
                    if (pth != null)
                        return pth.ToString();

                regKeyc.Close();
            }

            return null;
        }

        private bool ShouldRegisterBackglassServer(string path, RegistryViewEx view)
        {
            try            
            {
                var clsid = ReadRegistryValue(RegistryKeyEx.ClassesRoot, @"B2S.B2SPlayer\CLSID", null);
                if (string.IsNullOrEmpty(clsid))
                    return true;

                var codeBase = ReadRegistryValue(RegistryKeyEx.ClassesRoot, @"CLSID\" + clsid + @"\InprocServer32", "CodeBase", view);
                if (string.IsNullOrEmpty(codeBase))
                    return true;
                
                string localPath = new Uri(codeBase).LocalPath;
                if (!File.Exists(localPath))
                    return true;

                // Path has changed ?
                if (!localPath.Equals(path, StringComparison.InvariantCultureIgnoreCase))
                    return true;

                // Version changed ?
                var assembly = ReadRegistryValue(RegistryKeyEx.ClassesRoot, @"CLSID\" + clsid + @"\InprocServer32", "Assembly", view);
                var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(localPath).FullName;

                return assembly != assemblyName;
            }
            catch
            {
                return true;
            }
        }

        private void EnsureBackglassServerRegistered(string path)
        {
            var view = Kernel32.IsX64(_exe) ? RegistryViewEx.Registry64 : RegistryViewEx.Registry32;

            string dllPath = Path.Combine(path, "BackglassServer", "B2SBackglassServer.dll");
            if (!ShouldRegisterBackglassServer(dllPath, view))
                return;

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring BackGlass Server is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = Path.Combine(GetRegAsmPath(view), "regasm.exe");
                px.StartInfo.Arguments = "\"" + dllPath + "\" /codebase";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private void EnsurePinupPlayerRegistered(string path)
        {
            string keyPath = @"TypeLib\{D50F2477-84E8-4CED-9409-3735CA67FDE3}\1.0\0\win32";
            string PinupPlayerPath = Path.Combine(path, "PinUPSystem", "PinUpPlayer.exe");

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(null) as string;
                        if (value != null)
                        {
                            if (value != PinupPlayerPath)
                                RegisterPinupPlayer(PinupPlayerPath);
                            else
                                return;
                        }
                        else
                            RegisterPinupPlayer(PinupPlayerPath);
                    }
                    else
                        RegisterPinupPlayer(PinupPlayerPath);
                }
            }
            catch
            { }
        }

        private void RegisterPinupPlayer(string path)
        {
            if (!File.Exists(path))
            {
                SimpleLogger.Instance.Warning("[WARNING] PinUpPlayer.exe not found.");
                return;
            }

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring PinupPlayer is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = path;
                px.StartInfo.Arguments = "/regserver";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private void EnsurePinupDOFRegistered(string path)
        {
            string keyPath = @"TypeLib\{02B4C318-12D3-48C6-AA69-CEE342FF9D15}\1.0\0\win32";
            string PinupDOFPath = Path.Combine(path, "PinUPSystem", "PinUpDOF.exe");

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(null) as string;
                        if (value != null)
                        {
                            if (value != PinupDOFPath)
                                RegisterPinupDOF(PinupDOFPath);
                            else
                                return;
                        }
                        else
                            RegisterPinupDOF(PinupDOFPath);
                    }
                    else
                        RegisterPinupDOF(PinupDOFPath);
                }
            }
            catch
            { }
        }

        private void RegisterPinupDOF(string path)
        {
            if (!File.Exists(path))
            {
                SimpleLogger.Instance.Warning("[WARNING] PinUpDOF.exe not found.");
                return;
            }

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring PinUpDOF is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = path;
                px.StartInfo.Arguments = "/regserver";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private void EnsurePupServerRegistered(string path)
        {
            string keyPath = @"TypeLib\{5EC048E8-EF55-40B8-902D-D6ECD1C8FF4E}\1.0\0\win32";
            string PinupDOFPath = Path.Combine(path, "PinUPSystem", "PuPServer.exe");

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(null) as string;
                        if (value != null)
                        {
                            if (value != PinupDOFPath)
                                RegisterPupServer(PinupDOFPath);
                            else
                                return;
                        }
                        else
                            RegisterPupServer(PinupDOFPath);
                    }
                    else
                        RegisterPupServer(PinupDOFPath);
                }
            }
            catch
            { }
        }

        private void RegisterPupServer(string path)
        {
            if (!File.Exists(path))
            {
                SimpleLogger.Instance.Warning("[WARNING] PuPServer.exe not found.");
                return;
            }

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring PuPServer is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = path;
                px.StartInfo.Arguments = "/regserver";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private void EnsurePupDMDControlRegistered(string path)
        {
            string keyPath = @"TypeLib\{5049E487-2802-46B0-A511-8B198B274E1B}\1.0\0\win32";
            string PUPDMDControl = Path.Combine(path, "VPinMAME", "PUPDMDControl.exe");

            try
            {
                using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(null) as string;
                        if (value != null)
                        {
                            if (value != PUPDMDControl)
                                RegisterPupDMDControl(PUPDMDControl);
                            else
                                return;
                        }
                        else
                            RegisterPupDMDControl(PUPDMDControl);
                    }
                    else
                        RegisterPupDMDControl(PUPDMDControl);
                }
            }
            catch
            { }
        }

        private void RegisterPupDMDControl(string path)
        {
            if (!File.Exists(path))
            {
                SimpleLogger.Instance.Warning("[WARNING] PUPDMDControl.exe not found.");
                return;
            }

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring PUPDMDControl is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = path;
                px.StartInfo.Arguments = "/regserver";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.StartInfo.WindowStyle = ProcessWindowStyle.Minimized;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private static string GetRegAsmPath(RegistryViewEx view = RegistryViewEx.Registry32)
        {
            string installRoot = string.Empty;
            string str2 = null;

            var key = RegistryKeyEx.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\.NetFramework", view);
            if (key != null)
            {
                object oInstallRoot = key.GetValue("InstallRoot");
                if (oInstallRoot != null)
                    installRoot = oInstallRoot.ToString();

                key.Close();
            }

            if (string.IsNullOrEmpty(installRoot))
                return null;

            key = RegistryKeyEx.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\.NetFramework\Policy\v4.0", view);
            if (key != null)
            {
                string str3 = "v4.0";
                foreach (string str4 in key.GetValueNames())
                {
                    string path = Path.Combine(installRoot, str3 + "." + str4);
                    if (Directory.Exists(path))
                    {
                        str2 = path;
                        break;
                    }
                }

                key.Close();
            }

            return str2;
        }

        private static bool ShouldRegisterVPinMame(string path, RegistryViewEx view)
        {
            try
            {
                var dll = RegistryKeyEx.GetRegistryValue( 
                    view == RegistryViewEx.Registry64 ?
                    @"HKEY_CLASSES_ROOT\TypeLib\{57270B76-C846-4B1E-88D4-53C8337A0623}\1.0\0\win64" :
                    @"HKEY_CLASSES_ROOT\TypeLib\{57270B76-C846-4B1E-88D4-53C8337A0623}\1.0\0\win32", null, view);

                if (dll == null)
                    return true;

                var dllPath = dll.ToString();
                if (string.IsNullOrEmpty(dllPath))
                    return true;

                string localPath = new Uri(dllPath).LocalPath;
                if (!File.Exists(localPath))
                    return true;

                // Path has changed ?
                if (!localPath.Equals(path, StringComparison.InvariantCultureIgnoreCase))
                    return true;

                return false;
            }
            catch
            {
                return true;
            }
        }

        private void EnsureVPinMameRegistered(string path)
        {
            RegistryViewEx view = Kernel32.IsX64(_exe) ? RegistryViewEx.Registry64 : RegistryViewEx.Registry32;

            string dllPath = Path.Combine(path, "VPinMame", view == RegistryViewEx.Registry64 ? "VPinMAME64.dll" : "VPinMAME.dll");
            if (!ShouldRegisterVPinMame(dllPath, view))
                return;

            try
            {
                SimpleLogger.Instance.Info("[Generator] Ensuring VpinMame is registered.");

                Process px = new Process
                {
                    EnableRaisingEvents = true
                };
                px.StartInfo.Verb = "RunAs";
                px.StartInfo.FileName = Path.Combine(FileTools.GetSystemDirectory(view), "regsvr32.exe");
                px.StartInfo.Arguments = "/s \"" + dllPath + "\"";
                px.StartInfo.UseShellExecute = true;
                px.StartInfo.CreateNoWindow = true;
                px.Start();
                px.WaitForExit();
            }
            catch { }
        }

        private void SetupOptions(string path, string romPath, ScreenResolution resolution)
        {
            if (_version >= new Version(10, 8, 0, 0))
                SetupOptionsIniFile(path, resolution);
            else
                SetupOptionsRegistry(resolution);

            SetupVPinMameOptions(path, romPath);
            SetupDmdDevice(path);
        }

        private void SetupOptionsIniFile(string path, ScreenResolution resolution)
        {
            string iniFile = Path.Combine(path, "VPinballX.ini");

            using (var ini = new IniFile(iniFile, IniOptions.UseSpaces | IniOptions.KeepEmptyValues | IniOptions.KeepEmptyLines))
            {
                SimpleLogger.Instance.Info("[Generator] Writing config to VPinballX.ini file.");

                if (SystemConfig.isOptSet("enableb2s") && !SystemConfig.getOptBoolean("enableb2s"))
                    ini.WriteValue("Controller", "ForceDisableB2S", "1");
                else
                    ini.WriteValue("Controller", "ForceDisableB2S", "0");

                if (string.IsNullOrEmpty(ini.GetValue("Controller", "DOFContactors")))
                {
                    ini.WriteValue("Controller", "DOFContactors", "2");
                    ini.WriteValue("Controller", "DOFKnocker", "2");
                    ini.WriteValue("Controller", "DOFChimes", "2");
                    ini.WriteValue("Controller", "DOFBell", "2");
                    ini.WriteValue("Controller", "DOFGear", "2");
                    ini.WriteValue("Controller", "DOFShaker", "2");
                    ini.WriteValue("Controller", "DOFFlippers", "2");
                    ini.WriteValue("Controller", "DOFTargets", "2");
                    ini.WriteValue("Controller", "DOFDropTargets", "2");
                }

                ini.WriteValue("Player", "DisableESC", "1");

                // Get monitor index 
                int monitorIndex = SystemConfig.isOptSet("MonitorIndex") && !string.IsNullOrEmpty(SystemConfig["MonitorIndex"]) ? SystemConfig["MonitorIndex"].ToInteger() - 1 : 0;
                if (monitorIndex >= Screen.AllScreens.Length)
                    monitorIndex = 0;

                // Get bounds based on screen or resolution
                Size bounds = resolution == null ? Screen.PrimaryScreen.Bounds.Size : new Size(resolution.Width, resolution.Height);
                if (monitorIndex != 0 && resolution == null && monitorIndex < Screen.AllScreens.Length)
                    bounds = Screen.AllScreens[monitorIndex].Bounds.Size;

                // Resolution and fullscreen
                ini.WriteValue("Player", "Width", bounds.Width.ToString());
                ini.WriteValue("Player", "Height", bounds.Height.ToString());
                ini.WriteValue("Player", "FullScreen", "0"); // resolution == null ? "0" : "1" -> Let desktop resolution handle
                ini.WriteValue("Player", "Display", monitorIndex.ToString());

                // Vertical sync
                if (SystemConfig.isOptSet("vp_vsync") && !string.IsNullOrEmpty(SystemConfig["vp_vsync"]))
                    ini.WriteValue("Player", "SyncMode", SystemConfig["vp_vsync"]);
                else
                    ini.WriteValue("Player", "SyncMode", "3");

                // Video options
                if (SystemConfig.isOptSet("vp_ambient_occlusion") && SystemConfig["vp_ambient_occlusion"] == "dynamic")
                {
                    ini.WriteValue("Player", "DisableAO", "0");
                    ini.WriteValue("Player", "DynamicAO", "1");
                }
                else
                {
                    ini.WriteValue("Player", "DisableAO", SystemConfig["vp_ambient_occlusion"] == "0" ? "1" : "0");
                    ini.WriteValue("Player", "DynamicAO", "0");
                }

                ini.WriteValue("Player", "AAFactor", SystemConfig.GetValueOrDefault("vp_supersampling", "1.000000"));
                ini.WriteValue("Player", "FXAA", SystemConfig.GetValueOrDefault("vp_antialiasing", "0"));
                ini.WriteValue("Player", "Sharpen", SystemConfig.GetValueOrDefault("vp_sharpen", "0"));
                
                ini.WriteValue("Player", "BGSet", SystemConfig.getOptBoolean("arcademode") ? "1" : "0");

                bool aniFilter = !SystemConfig.isOptSet("vp_anisotropic_filtering") || SystemConfig.getOptBoolean("vp_anisotropic_filtering");
                ini.WriteValue("Player", "ForceAnisotropicFiltering", aniFilter ? "1" : "0");
                ini.WriteValue("Player", "UseNVidiaAPI", SystemConfig.getOptBoolean("vp_nvidia") ? "1" : "0");
                ini.WriteValue("Player", "SoftwareVertexProcessing", SystemConfig.getOptBoolean("vp_vertex") ? "1" : "0");

                // level of details
                if (SystemConfig.isOptSet("vp_details") && !string.IsNullOrEmpty(SystemConfig["vp_details"]))
                    ini.WriteValue("Player", "AlphaRampAccuracy", SystemConfig["vp_details"].ToIntegerString());
                else
                    ini.WriteValue("Player", "AlphaRampAccuracy", "10");

                // Audio
                ini.WriteValue("Player", "PlayMusic", SystemConfig.getOptBoolean("vp_music_off") ? "0" : "1");
                BindIniFeature(ini, "Player", "Sound3D", "vp_audiochannels", "0");

                // Controls
                Controller controller = null;
                bool isXinput = false;
                string LRAxis = "1";
                string UDAxis = "2";
                string PlungerAxis = "4";

                if (Controllers != null && Controllers.Count > 0)
                {
                    controller = Controllers.FirstOrDefault(c => c.PlayerIndex == 1);
                    if (controller != null && controller.IsXInputDevice)
                        isXinput = true;
                    else if (controller != null)
                    {
                        SdlToDirectInput dinputController = getDInputController(controller);
                        if (dinputController != null)
                        {
                            if (dinputController.ButtonMappings.ContainsKey("leftx"))
                                LRAxis = getDinputID(dinputController.ButtonMappings, "leftx");
                            if (dinputController.ButtonMappings.ContainsKey("lefty"))
                                UDAxis = getDinputID(dinputController.ButtonMappings, "lefty");
                            if (dinputController.ButtonMappings.ContainsKey("righty"))
                                PlungerAxis = getDinputID(dinputController.ButtonMappings, "righty");
                        }
                        if (LRAxis == null)
                            LRAxis = "1";
                        if (UDAxis == null)
                            UDAxis = "2";
                        if (PlungerAxis == null)
                            PlungerAxis = "4";
                    }
                }

                if (SystemConfig.isOptSet("vp_inputdriver") && !string.IsNullOrEmpty(SystemConfig["vp_inputdriver"]))
                    ini.WriteValue("Player", "InputApi", SystemConfig["vp_inputdriver"]);
                else
                    ini.WriteValue("Player", "InputApi", isXinput ? "1" : "0");

                if (!SystemConfig.getOptBoolean("disableautocontrollers"))
                {
                    ini.WriteValue("Player", "LRAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? "0" : LRAxis);
                    ini.WriteValue("Player", "UDAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? "0" : UDAxis);
                    ini.WriteValue("Player", "PlungerAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? "0" : PlungerAxis);
                    ini.WriteValue("Player", "ReversePlungerAxis", "1");
                    BindIniFeatureSlider(ini, "Player", "DeadZone", "joy_deadzone", "15");
                }

                ini.WriteValue("Editor", "WindowTop", (Screen.PrimaryScreen.Bounds.Height / 2 - 300).ToString());
                ini.WriteValue("Editor", "WindowBottom", (Screen.PrimaryScreen.Bounds.Height / 2 + 300).ToString());
                ini.WriteValue("Editor", "WindowLeft", (Screen.PrimaryScreen.Bounds.Width / 2 - 400).ToString());
                ini.WriteValue("Editor", "WindowRight", (Screen.PrimaryScreen.Bounds.Width / 2 + 400).ToString());
                ini.WriteValue("Editor", "WindowMaximized", "0");
                ini.WriteValue("Editor", "SelectTableOnStart", "");
                ini.WriteValue("Editor", "SelectTableOnPlayerClose", "");

                WriteKBconfig(ini);
                
                ini.Save();
            }
        }

        private void WriteKBconfig(IniFile ini)
        {
            if (SystemConfig.getOptBoolean("disableautocontrollers"))
                return;

            ini.WriteValue("Player", "LFlipKey", "42");
            ini.WriteValue("Player", "RFlipKey", "54");
            ini.WriteValue("Player", "StagedLFlipKey", "219");
            ini.WriteValue("Player", "StagedRFlipKey", "184");
            ini.WriteValue("Player", "LTiltKey", "44");
            ini.WriteValue("Player", "RTiltKey", "53");
            ini.WriteValue("Player", "CTiltKey", "57");
            ini.WriteValue("Player", "PlungerKey", "28");
            ini.WriteValue("Player", "FrameCount", "87");
            ini.WriteValue("Player", "DebugBalls", "24");
            ini.WriteValue("Player", "Debugger", "32");
            ini.WriteValue("Player", "AddCreditKey", "6");
            ini.WriteValue("Player", "AddCreditKey2", "5");
            ini.WriteValue("Player", "StartGameKey", "2");
            ini.WriteValue("Player", "MechTilt", "20");
            ini.WriteValue("Player", "RMagnaSave", "157");
            ini.WriteValue("Player", "LMagnaSave", "29");
            ini.WriteValue("Player", "ExitGameKey", "16");
            ini.WriteValue("Player", "VolumeUp", "13");
            ini.WriteValue("Player", "VolumeDown", "12");
            ini.WriteValue("Player", "LockbarKey", "56");
            ini.WriteValue("Player", "PauseKey", "25");
            ini.WriteValue("Player", "TweakKey", "88");
            ini.WriteValue("Player", "JoyCustom1Key", "200");
            ini.WriteValue("Player", "JoyCustom2Key", "208");
            ini.WriteValue("Player", "JoyCustom3Key", "203");
            ini.WriteValue("Player", "JoyCustom4Key", "205");
        }

        private void SetupOptionsRegistry(ScreenResolution resolution)
        {
            //HKEY_CURRENT_USER\Software\Visual Pinball\VP10\Player

            RegistryKey regKeyc = Registry.CurrentUser.OpenSubKey(@"Software", true);

            RegistryKey vp = regKeyc.CreateSubKey("Visual Pinball");
            if (vp == null)
                return;

            regKeyc = vp.CreateSubKey("Controller");
            if (regKeyc != null)
            {
                SimpleLogger.Instance.Info("[Generator] Writing config to registry.");

                if (Screen.AllScreens.Length > 1 && (!SystemConfig.isOptSet("enableb2s") || SystemConfig.getOptBoolean("enableb2s")) && !SystemInformation.TerminalServerSession)
                    SetOption(regKeyc, "ForceDisableB2S", 0);
                else
                    SetOption(regKeyc, "ForceDisableB2S", 1);

                SetupOptionIfNotExists(regKeyc, "DOFContactors", 2);
                SetupOptionIfNotExists(regKeyc, "DOFKnocker", 2);
                SetupOptionIfNotExists(regKeyc, "DOFChimes", 2);
                SetupOptionIfNotExists(regKeyc, "DOFBell", 2);
                SetupOptionIfNotExists(regKeyc, "DOFGear", 2);
                SetupOptionIfNotExists(regKeyc, "DOFShaker", 2);
                SetupOptionIfNotExists(regKeyc, "DOFFlippers", 2);
                SetupOptionIfNotExists(regKeyc, "DOFTargets", 2);
                SetupOptionIfNotExists(regKeyc, "DOFDropTargets", 2);

                regKeyc.Close();
            }

            RegistryKey vp10 = vp.CreateSubKey("VP10");
            if (vp10 == null)
                return;

            regKeyc = vp10.CreateSubKey("Player");
            if (regKeyc != null)
            {
                SetOption(regKeyc, "DisableESC", 1);

                // Resolution and fullscreen
                SetOption(regKeyc, "Width", resolution == null ? Screen.PrimaryScreen.Bounds.Width : resolution.Width);
                SetOption(regKeyc, "Height", resolution == null ? Screen.PrimaryScreen.Bounds.Height : resolution.Height);
                SetOption(regKeyc, "FullScreen", resolution == null ? 0 : 1);
                
                // Vertical sync
                if (SystemConfig.isOptSet("video_vsync") && SystemConfig["video_vsync"] == "adaptative")
                    SetOption(regKeyc, "AdaptiveVSync", 2);
                else if (SystemConfig.isOptSet("video_vsync") && SystemConfig["video_vsync"] == "false")
                    SetOption(regKeyc, "AdaptiveVSync", 0);
                else
                    SetOption(regKeyc, "AdaptiveVSync", 1);

                // Monitor index is 1-based
                if (SystemConfig.isOptSet("MonitorIndex") && !string.IsNullOrEmpty(SystemConfig["MonitorIndex"]))
                {
                    int monitor = SystemConfig["MonitorIndex"].ToInteger() - 1;
                    SetOption(regKeyc, "Display", monitor);
                }
                else
                    SetOption(regKeyc, "Display", 0);

                // Video options
                SetOption(regKeyc, "BallReflection", SystemConfig["vp_ballreflection"] == "1" ? 1 : 0);

                if (SystemConfig.isOptSet("vp_ambient_occlusion") && SystemConfig["vp_ambient_occlusion"] == "dynamic")
                {
                    SetOption(regKeyc, "DisableAO", 0);
                    SetOption(regKeyc, "DynamicAO", 1);
                }
                else
                {
                    SetOption(regKeyc, "DisableAO", SystemConfig["vp_ambient_occlusion"] == "0" ? 1 : 0);
                    SetOption(regKeyc, "DynamicAO", 0);
                }

                if (SystemConfig.isOptSet("vp_antialiasing") && !string.IsNullOrEmpty(SystemConfig["vp_antialiasing"]))
                {
                    int fxaa = SystemConfig["vp_antialiasing"].ToInteger();
                    SetOption(regKeyc, "FXAA", fxaa);
                }
                else
                    SetOption(regKeyc, "FXAA", 0);

                if (SystemConfig.isOptSet("vp_sharpen") && !string.IsNullOrEmpty(SystemConfig["vp_sharpen"]))
                {
                    int sharpen = SystemConfig["vp_sharpen"].ToInteger();
                    SetOption(regKeyc, "Sharpen", sharpen);
                }
                else
                    SetOption(regKeyc, "FXAA", 0);

                SetOption(regKeyc, "BGSet", SystemConfig.getOptBoolean("arcademode") ? 1 : 0);

                bool aniFilter = !SystemConfig.isOptSet("vp_anisotropic_filtering") || SystemConfig.getOptBoolean("vp_anisotropic_filtering");
                SetOption(regKeyc, "ForceAnisotropicFiltering", aniFilter ? 1 : 0);
                SetOption(regKeyc, "UseNVidiaAPI", SystemConfig.getOptBoolean("vp_nvidia") ? 1 : 0);
                SetOption(regKeyc, "SoftwareVertexProcessing", SystemConfig.getOptBoolean("vp_vertex") ? 1 : 0);

                // Audio
                SetOption(regKeyc, "PlayMusic", SystemConfig.getOptBoolean("vp_music_off") ? 0 : 1);

                // Controls
                if (!SystemConfig.getOptBoolean("disableautocontrollers"))
                {
                    SetOption(regKeyc, "LRAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? 0 : 1);
                    SetOption(regKeyc, "UDAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? 0 : 2);
                    SetOption(regKeyc, "PlungerAxis", SystemConfig.getOptBoolean("nouse_joyaxis") ? 0 : 3);

                    int deadzone = 15;

                    if (SystemConfig.isOptSet("joy_deadzone") && !string.IsNullOrEmpty(SystemConfig["joy_deadzone"]))
                        deadzone = SystemConfig["joy_deadzone"].ToIntegerString().ToInteger();

                    SetOption(regKeyc, "DeadZone", deadzone);
                }
                regKeyc.Close();
            }

            regKeyc = vp10.CreateSubKey("Editor");
            if (regKeyc != null)
            {
                SetOption(regKeyc, "WindowTop", Screen.PrimaryScreen.Bounds.Height / 2 - 300);
                SetOption(regKeyc, "WindowBottom", Screen.PrimaryScreen.Bounds.Height / 2 + 300);
                SetOption(regKeyc, "WindowLeft", Screen.PrimaryScreen.Bounds.Width / 2 - 400);
                SetOption(regKeyc, "WindowRight", Screen.PrimaryScreen.Bounds.Width / 2 + 400);
                SetOption(regKeyc, "WindowMaximized", 0);

                regKeyc.Close();
            }

            vp10.Close();
            vp.Close();
        }

        private static void SetupVPinMameOptions(string path, string romPath)
        {
            var softwareKey = Registry.CurrentUser.OpenSubKey(@"Software", true);
            if (softwareKey == null)
                return;
            
            var visualPinMame = softwareKey.CreateSubKey("Freeware").CreateSubKey("Visual PinMame");
            if (visualPinMame != null)
            {
                SimpleLogger.Instance.Info("[Generator] Writing VPinMame config to Registry.");

                DisableVPinMameLicenceDialogs(romPath, visualPinMame);

                var globalKey = visualPinMame.CreateSubKey("globals");
                var defaultKey = visualPinMame.CreateSubKey("default");

                // global key
                if (globalKey != null)
                {
                    string vPinMamePath = Path.Combine(path, "VPinMAME");

                    SetOption(globalKey, "rompath", string.IsNullOrEmpty(romPath) ? Path.Combine(vPinMamePath, "roms") : romPath);

                    SetOption(globalKey, "artwork_directory", Path.Combine(vPinMamePath, "artwork"));
                    SetOption(globalKey, "cfg_directory", Path.Combine(vPinMamePath, "cfg"));
                    SetOption(globalKey, "cheat_file", Path.Combine(vPinMamePath, "cheat.dat"));
                    SetOption(globalKey, "cpu_affinity_mask", 0);
                    SetOption(globalKey, "diff_directory", Path.Combine(vPinMamePath, "diff"));
                    SetOption(globalKey, "hiscore_directory", Path.Combine(vPinMamePath, "hi"));
                    SetOption(globalKey, "history_file", Path.Combine(vPinMamePath, "history.dat"));
                    SetOption(globalKey, "input_directory", Path.Combine(vPinMamePath, "inp"));
                    SetOption(globalKey, "joystick", 0);
                    SetOption(globalKey, "low_latency_throttle", 0);
                    SetOption(globalKey, "mameinfo_file", Path.Combine(vPinMamePath, "mameinfo.dat"));
                    SetOption(globalKey, "memcard_directory", Path.Combine(vPinMamePath, "memcard"));
                    SetOption(globalKey, "mouse", 0);
                    SetOption(globalKey, "nvram_directory", Path.Combine(vPinMamePath, "nvram"));
                    SetOption(globalKey, "samplepath", Path.Combine(vPinMamePath, "samples"));
                    SetOption(globalKey, "screen", "");
                    SetOption(globalKey, "snapshot_directory", Path.Combine(vPinMamePath, "snap"));
                    SetOption(globalKey, "state_directory", Path.Combine(vPinMamePath, "sta"));
                    SetOption(globalKey, "steadykey", 1);
                    SetOption(globalKey, "wave_directory", Path.Combine(vPinMamePath, "wave"));
                    SetOption(globalKey, "window", 1);

                    globalKey.Close();
                }

                // default key
                if (defaultKey != null)
                {
                    if (Program.SystemConfig.getOptBoolean("vpmame_dmd"))
                    {
                        SetOption(defaultKey, "showpindmd", 1);
                        SetOption(defaultKey, "showwindmd", 0);
                    }
                    else
                    {
                        SetOption(defaultKey, "showpindmd", 0);
                        SetOption(defaultKey, "showwindmd", 1);
                    }

                    BindBoolRegistryFeature(defaultKey, "cabinet_mode", "vpmame_cabinet", 1, 0, true);
                    BindBoolRegistryFeature(defaultKey, "dmd_colorize", "vpmame_colordmd", 1, 0, false);

                    defaultKey.Close();
                }

                // per rom config
                if (romPath != null)
                {
                    string[] romList = Directory.GetFiles(romPath, "*.zip").Select(r => Path.GetFileNameWithoutExtension(r)).Distinct().ToArray();
                    foreach (var rom in romList)
                    {
                        var romKey = visualPinMame.OpenSubKey(rom, true);

                        if (romKey == null)
                            romKey = visualPinMame.CreateSubKey(rom);

                        if (Program.SystemConfig.getOptBoolean("vpmame_dmd"))
                        {
                            SetOption(romKey, "showpindmd", 1);
                            SetOption(romKey, "showwindmd", 0);
                        }
                        else
                        {
                            SetOption(romKey, "showpindmd", 0);
                            SetOption(romKey, "showwindmd", 1);
                        }

                        BindBoolRegistryFeature(romKey, "cabinet_mode", "vpmame_cabinet", 1, 0, true);
                        BindBoolRegistryFeature(romKey, "dmd_colorize", "vpmame_colordmd", 1, 0, false);

                        romKey.Close();
                    }
                }
            }

            softwareKey.Close();
        }

        private void SetupDmdDevice(string path)
        {
            string iniFile = Path.Combine(path, "VPinMAME", "DmdDevice.ini");

            using (var ini = new IniFile(iniFile, IniOptions.UseSpaces | IniOptions.KeepEmptyValues | IniOptions.KeepEmptyLines))
            {
                BindBoolIniFeatureOn(ini, "virtualdmd", "enabled", "vpmame_virtualdmd", "true", "false");
                BindBoolIniFeature(ini, "zedmd", "enabled", "vpmame_zedmd", "true", "false");

                ini.Save();
            }
        }

        private static void DisableVPinMameLicenceDialogs(string romPath, RegistryKey visualPinMame)
        {
            if (romPath == null || !Directory.Exists(romPath))
                return;

            SimpleLogger.Instance.Info("[Generator] Disabling VPinMame Licence prompts for all available table roms.");

            string[] romList = Directory.GetFiles(romPath, "*.zip").Select(r => Path.GetFileNameWithoutExtension(r)).Distinct().ToArray();
            foreach (var rom in romList)
            {
                var romKey = visualPinMame.OpenSubKey(rom, true);
                if (romKey == null)
                {
                    romKey = visualPinMame.CreateSubKey(rom);
                    romKey?.SetValue(null, 1);
                }
                
                if (romKey != null)
                {
                    romKey.SetValue("cabinet_mode", 1);
                    romKey.SetValue("skip_disclaimer", 1);
                    romKey.SetValue("skip_gameinfo", 1);

                    romKey.Close();
                }
            }
        }

        private static void SetOption(RegistryKey regKeyc, string name, object value)
        {
            object o = regKeyc.GetValue(name);
            if (object.Equals(value, o))
                return;

            regKeyc.SetValue(name, value);
        }

        private static void SetupOptionIfNotExists(RegistryKey regKeyc, string name, object value)
        {
            object o = regKeyc.GetValue(name);
            if (o != null)
                return;

            regKeyc.SetValue(name, value);
        }

        private void SetupB2STableSettings(string path)
        {
            string b2STableSettingsPath = Path.Combine(path, "BackglassServer", "B2STableSettings.xml");
            string b2STableSettingsPathRom = Path.Combine(_gamePath, "B2STableSettings.xml");

            if (!File.Exists(b2STableSettingsPathRom))
            {
                try
                {
                    XDocument xmlDoc = new XDocument(
                    new XElement("B2STableSettings",
                    new XElement("ArePluginsOn", 1),
                    new XElement("DefaultStartMode", 2),
                    new XElement("DisableFuzzyMatching", 1),
                    new XElement("HideGrill", SystemConfig.getOptBoolean("vpbg_hidegrill") ? 1 : 0),
                    new XElement("LogPath", ""),  // Empty value
                    new XElement("IsLampsStateLogOn", 0),
                    new XElement("IsSolenoidsStateLogOn", 0),
                    new XElement("IsGIStringsStateLogOn", 0),
                    new XElement("IsLEDsStateLogOn", 0),
                    new XElement("IsPaintingLogOn", 0),
                    new XElement("IsStatisticsBackglassOn", 0),
                    new XElement("FormToFront", 1),
                    new XElement("ShowStartupError", 0),
                    new XElement("ScreenshotPath", ""),  // Empty value
                    new XElement("ScreenshotFileType", 0)
                    ));

                    xmlDoc.Save(b2STableSettingsPathRom);
                    xmlDoc.Save(b2STableSettingsPath);
                }
                catch { }
            }
            else
            {
                try
                {
                    XDocument xmlDoc = XDocument.Load(b2STableSettingsPathRom);
                    XElement root = xmlDoc.Element("B2STableSettings");

                    if (root != null)
                    {
                        // Plugins
                        XElement element = root.Element("ArePluginsOn");

                        if (element != null)
                            element.Value = "1";
                        else
                            root.Add(new XElement("ArePluginsOn", "1"));

                        // Hide grill
                        XElement hidegrill = root.Element("HideGrill");

                        if (element != null)
                            element.Value = SystemConfig.getOptBoolean("vpbg_hidegrill") ? "1" : "0";
                        else
                            root.Add(new XElement("HideGrill", SystemConfig.getOptBoolean("vpbg_hidegrill") ? "1" : "0"));

                        xmlDoc.Save(b2STableSettingsPathRom);
                        xmlDoc.Save(b2STableSettingsPath);
                    }
                    else
                        SimpleLogger.Instance.Warning("[WARNING] File B2STableSettings.xml is corrupted.");
                }
                catch {}
            }
        }

        private SdlToDirectInput getDInputController(Controller ctrl)
        {
            string gamecontrollerDB = Path.Combine(Program.AppConfig.GetFullPath("tools"), "gamecontrollerdb.txt");
            if (!File.Exists(gamecontrollerDB))
                return null;

            string guid = (ctrl.Guid.ToString()).ToLowerInvariant();
            if (string.IsNullOrEmpty(guid))
                return null;

            SdlToDirectInput dinputController = GameControllerDBParser.ParseByGuid(gamecontrollerDB, guid);
            if (dinputController == null)
                return null;
            else
                return dinputController;
        }

        private string getDinputID(Dictionary<string, string> mapping, string key)
        {
            if (!mapping.ContainsKey(key))
                return null;

            string button = mapping[key];

            if (button.StartsWith("-a") || button.StartsWith("+a"))
            {
                int axisID = button.Substring(2).ToInteger();
                axisID++;
                return axisID.ToString();
            }
            else if (button.StartsWith("a"))
            {
                int axisID = button.Substring(1).ToInteger();
                axisID++;
                return axisID.ToString();
            }

            return null;
        }

        private static void BindBoolRegistryFeature(RegistryKey key, string name, string featureName, object truevalue, object falsevalue, bool defaultOn)
        {
            if (Program.Features.IsSupported(featureName))
            {
                if (Program.SystemConfig.isOptSet(featureName) && Program.SystemConfig.getOptBoolean(featureName))
                    SetOption(key, name, truevalue);
                else
                {
                    if (Program.SystemConfig.isOptSet(featureName) && !Program.SystemConfig.getOptBoolean(featureName))
                        SetOption(key, name, falsevalue);
                    else if (defaultOn)
                        SetOption(key, name, truevalue);
                    else
                        SetOption(key, name, falsevalue);
                }
            }
        }

        class DirectB2sData
        {
            public static DirectB2sData FromFile(string file)
            {
                if (!File.Exists(file))
                    return null;


                XmlDocument document = new XmlDocument();
                document.Load(file);

                XmlElement element = (XmlElement)document.SelectSingleNode("DirectB2SData");
                if (element == null)
                    return null;

                var bulbs = new List<Bulb>();

                foreach (XmlElement bulb in element.SelectNodes("Illumination/Bulb"))
                {
                    if (!bulb.HasAttribute("Parent") || bulb.GetAttribute("Parent") != "Backglass")
                        continue;

                    try
                    {
                        Bulb item = new Bulb
                        {
                            ID = bulb.GetAttribute("ID").ToInteger(),
                            LightColor = bulb.GetAttribute("LightColor"),
                            LocX = bulb.GetAttribute("LocX").ToInteger(),
                            LocY = bulb.GetAttribute("LocY").ToInteger(),
                            Width = bulb.GetAttribute("Width").ToInteger(),
                            Height = bulb.GetAttribute("Height").ToInteger(),
                            Visible = bulb.GetAttribute("Visible") == "1",
                            IsImageSnippit = bulb.GetAttribute("IsImageSnippit") == "1",
                            Image = Misc.Base64ToImage(bulb.GetAttribute("Image"))
                        };

                        if (item.Visible && item.Image != null)
                            bulbs.Add(item);
                    }
                    catch { }
                }


                XmlElement element13 = (XmlElement)element.SelectSingleNode("Images/BackglassImage");
                if (element13 != null)
                {
                    try
                    {
                        var image = Misc.Base64ToImage(element13.Attributes["Value"].InnerText);
                        if (image != null)
                        {
                            return new DirectB2sData()
                            {
                                Bulbs = bulbs.ToArray(),
                                Image = image,
                            };
                        }
                    }
                    catch { }
                }

                return null;
            }

            public Image RenderBackglass(int index = 0)
            {
                var bitmap = new Bitmap(Image);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    foreach (var bulb in Bulbs)
                    {
                        if (bulb.IsImageSnippit)
                            continue;

                        if (index == 0 && (bulb.ID & 1) == 0)
                            continue;

                        if (index == 1 && (bulb.ID & 1) == 1)
                            continue;

                        if (index == 3)
                            continue;

                        Color lightColor = Color.White;
                        var split = bulb.LightColor.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                        if (split.Length == 3)
                            lightColor = Color.FromArgb(split[0].ToInteger(), split[1].ToInteger(), split[2].ToInteger());

                        using (ImageAttributes imageAttrs = new ImageAttributes())
                        {
                            var colorMatrix = new ColorMatrix(new float[][]
                                    {
                                        new float[] { lightColor.R / 255f, 0, 0, 0, 0 },
                                        new float[] { 0, lightColor.G / 255f, 0, 0, 0 },
                                        new float[] { 0, 0, lightColor.B / 255f, 0, 0 },
                                        new float[] { 0, 0, 0, 1, 0 },
                                        new float[] { 0, 0, 0, 0, 1 }
                                    });

                            imageAttrs.SetColorMatrix(colorMatrix);

                            Rectangle dest = new Rectangle(bulb.LocX, bulb.LocY, bulb.Width, bulb.Height);
                            g.DrawImage(bulb.Image, dest, 0, 0, bulb.Image.Width, bulb.Image.Height, GraphicsUnit.Pixel, imageAttrs, null, IntPtr.Zero);
                        }
                    }
                }

                return bitmap;
            }

            public Bulb[] Bulbs { get; private set; }
            public Image Image { get; private set; }

            public class Bulb
            {
                public int ID { get; set; }

                public string LightColor { get; set; }
                public int LocX { get; set; }
                public int LocY { get; set; }
                public int Width { get; set; }
                public int Height { get; set; }
                public bool Visible { get; set; }
                public bool IsImageSnippit { get; set; }

                public Image Image { get; set; }
            }
        }
    }
}
