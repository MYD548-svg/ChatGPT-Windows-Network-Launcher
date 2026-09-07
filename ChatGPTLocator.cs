using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

namespace ChatGPTAntiBanLauncher
{
    public enum ClientInstallationType
    {
        None,
        StorePackage,
        StandaloneDesktop
    }

    public sealed class TargetClientInfo
    {
        public bool IsFound { get; set; }
        public ClientInstallationType Type { get; set; }
        public string ExecutablePath { get; set; }
        public string AppUserModelId { get; set; }
        public string PackageFamilyName { get; set; }
        public string Version { get; set; }
        public string InstallDirectory { get; set; }

        public string DisplayDescription
        {
            get
            {
                if (!IsFound) return "未检测到已安装的客户端";
                if (Type == ClientInstallationType.StorePackage)
                {
                    return string.Format("应用商店版 (OpenAI.Codex {0})", string.IsNullOrEmpty(Version) ? "" : Version);
                }
                return string.Format("独立桌面安装版 ({0})", ExecutablePath);
            }
        }
    }

    public static class ChatGPTLocator
    {
        public static TargetClientInfo LocateClient()
        {
            // 1. Check standard standalone desktop install locations
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string localExe = Path.Combine(localAppData, "Programs", "ChatGPT", "ChatGPT.exe");
                if (File.Exists(localExe))
                {
                    return new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StandaloneDesktop,
                        ExecutablePath = localExe,
                        InstallDirectory = Path.GetDirectoryName(localExe)
                    };
                }

                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pfExe = Path.Combine(progFiles, "ChatGPT", "ChatGPT.exe");
                if (File.Exists(pfExe))
                {
                    return new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StandaloneDesktop,
                        ExecutablePath = pfExe,
                        InstallDirectory = Path.GetDirectoryName(pfExe)
                    };
                }
            }
            catch { }

            // 2. Query Windows Store AppX package via PowerShell with timeout
            try
            {
                string psCommand = "$pkg = Get-AppxPackage -Name '*OpenAI.Codex*' | Sort-Object Version -Descending | Select-Object -First 1; if ($pkg) { Write-Output ($pkg.InstallLocation + '|||' + $pkg.PackageFamilyName + '|||' + $pkg.Version) }";
                string output = RunPowerShellWithTimeout(psCommand, 4000);

                if (!string.IsNullOrEmpty(output))
                {
                    string[] parts = output.Trim().Split(new string[] { "|||" }, StringSplitOptions.None);
                    if (parts.Length >= 1)
                    {
                        string installLoc = parts[0].Trim().Trim('\"');
                        string pkgFamily = parts.Length >= 2 ? parts[1].Trim() : null;
                        string version = parts.Length >= 3 ? parts[2].Trim() : null;

                        if (!string.IsNullOrEmpty(installLoc) && Directory.Exists(installLoc))
                        {
                            string exePath = Path.Combine(installLoc, "app", "ChatGPT.exe");
                            if (!File.Exists(exePath)) exePath = Path.Combine(installLoc, "app", "Codex.exe");
                            if (!File.Exists(exePath)) exePath = Path.Combine(installLoc, "ChatGPT.exe");

                            string appId = TryReadApplicationId(installLoc) ?? "App";
                            string aumid = !string.IsNullOrEmpty(pkgFamily) ? (pkgFamily + "!" + appId) : null;

                            return new TargetClientInfo
                            {
                                IsFound = true,
                                Type = ClientInstallationType.StorePackage,
                                ExecutablePath = exePath,
                                AppUserModelId = aumid,
                                PackageFamilyName = pkgFamily,
                                Version = version,
                                InstallDirectory = installLoc
                            };
                        }
                    }
                }
            }
            catch { }

            // 3. Fallback: inspect ProgramFiles\WindowsApps directly
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string windowsApps = Path.Combine(programFiles, "WindowsApps");
                if (Directory.Exists(windowsApps))
                {
                    string[] dirs = Directory.GetDirectories(windowsApps, "OpenAI.Codex*");
                    if (dirs != null && dirs.Length > 0)
                    {
                        Array.Sort(dirs);
                        string latestDir = dirs[dirs.Length - 1];

                        string exePath = Path.Combine(latestDir, "app", "ChatGPT.exe");
                        if (!File.Exists(exePath)) exePath = Path.Combine(latestDir, "app", "Codex.exe");
                        if (!File.Exists(exePath)) exePath = Path.Combine(latestDir, "ChatGPT.exe");

                        string aumid = TryBuildAppUserModelId(latestDir);

                        return new TargetClientInfo
                        {
                            IsFound = true,
                            Type = ClientInstallationType.StorePackage,
                            ExecutablePath = exePath,
                            AppUserModelId = aumid,
                            InstallDirectory = latestDir
                        };
                    }
                }
            }
            catch { }

            return new TargetClientInfo { IsFound = false, Type = ClientInstallationType.None };
        }

        private static string RunPowerShellWithTimeout(string command, int timeoutMs)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process ps = new Process())
            {
                ps.StartInfo = psi;
                StringBuilder sb = new StringBuilder();

                using (AutoResetEvent outputWaitHandle = new AutoResetEvent(false))
                {
                    ps.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data == null)
                        {
                            try { outputWaitHandle.Set(); } catch { }
                        }
                        else
                        {
                            sb.AppendLine(e.Data);
                        }
                    };

                    if (!ps.Start()) return null;

                    ps.BeginOutputReadLine();

                    if (ps.WaitForExit(timeoutMs) && outputWaitHandle.WaitOne(1000))
                    {
                        return sb.ToString();
                    }
                    else
                    {
                        try { ps.Kill(); } catch { }
                        return null;
                    }
                }
            }
        }

        private static string TryReadApplicationId(string installLocation)
        {
            try
            {
                string manifestPath = Path.Combine(installLocation, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) return null;

                XmlDocument doc = new XmlDocument();
                doc.Load(manifestPath);

                XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("m", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

                XmlNode appNode = doc.SelectSingleNode("//m:Application", nsmgr);
                if (appNode != null && appNode.Attributes != null && appNode.Attributes["Id"] != null)
                {
                    return appNode.Attributes["Id"].Value;
                }
            }
            catch { }
            return null;
        }

        private static string TryBuildAppUserModelId(string packagePath)
        {
            try
            {
                string packageName = Path.GetFileName(packagePath);
                int sep = packageName.LastIndexOf("__", StringComparison.Ordinal);
                if (sep < 0 || sep + 2 >= packageName.Length) return null;

                string publisherId = packageName.Substring(sep + 2);
                string appId = TryReadApplicationId(packagePath) ?? "App";
                return "OpenAI.Codex_" + publisherId + "!" + appId;
            }
            catch
            {
                return null;
            }
        }
    }
}
