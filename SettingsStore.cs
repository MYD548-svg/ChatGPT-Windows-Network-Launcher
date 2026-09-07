using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ChatGPTAntiBanLauncher
{
    [DataContract]
    public sealed class LauncherSettings
    {
        [DataMember(Name = "mode", Order = 1)]
        public string Mode { get; set; }

        [DataMember(Name = "iana_name", Order = 2)]
        public string IanaName { get; set; }

        [DataMember(Name = "utc_offset", Order = 3)]
        public string UtcOffset { get; set; }

        [DataMember(Name = "disable_tz", Order = 4, IsRequired = false)]
        public bool DisableTz { get; set; }

        [DataMember(Name = "graceful_close", Order = 5)]
        public bool GracefulClose { get; set; }

        [DataMember(Name = "network_mode", Order = 6)]
        public string NetworkMode { get; set; }

        [DataMember(Name = "proxy_host", Order = 7)]
        public string ProxyHost { get; set; }

        [DataMember(Name = "proxy_port", Order = 8)]
        public int ProxyPort { get; set; }

        [DataMember(Name = "auto_detect_proxy", Order = 9, IsRequired = false)]
        public bool AutoDetectProxy { get; set; }

        public LauncherSettings()
        {
            Mode = "iana";
            IanaName = "America/Los_Angeles";
            UtcOffset = "UTC-8";
            DisableTz = false;
            GracefulClose = true;

            NetworkMode = "vpn";
            ProxyHost = "127.0.0.1";
            ProxyPort = 7897;
            AutoDetectProxy = true;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            AutoDetectProxy = true;
        }
    }

    public static class SettingsStore
    {
        public static string CustomSettingsFilePath { get; set; }

        public static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTAntiBanLauncher");

        public static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

        public static string ActiveSettingsFilePath
        {
            get { return !string.IsNullOrEmpty(CustomSettingsFilePath) ? CustomSettingsFilePath : SettingsFilePath; }
        }

        // Retain legacy path check for smooth upgrade migration from pre-v0.5 versions
        public static readonly string LegacySettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTTimeZoneLauncher");

        public static readonly string LegacySettingsFilePath = Path.Combine(LegacySettingsDir, "settings.json");

        public static LauncherSettings Load(out string error)
        {
            error = null;
            LauncherSettings settings = new LauncherSettings();

            try
            {
                string targetPath = ActiveSettingsFilePath;
                // Auto-migrate legacy configuration if new configuration does not exist
                if (string.IsNullOrEmpty(CustomSettingsFilePath) && !File.Exists(targetPath) && File.Exists(LegacySettingsFilePath))
                {
                    try
                    {
                        if (!Directory.Exists(SettingsDir)) Directory.CreateDirectory(SettingsDir);
                        File.Copy(LegacySettingsFilePath, targetPath, false);
                    }
                    catch (Exception ex)
                    {
                        error = "配置迁移提示: " + ex.Message;
                    }
                }

                if (!File.Exists(targetPath))
                {
                    return settings;
                }

                string jsonText = File.ReadAllText(targetPath, Encoding.UTF8);
                if (!string.IsNullOrEmpty(jsonText))
                {
                    jsonText = jsonText.TrimStart('\uFEFF', '\u200B');
                }
                byte[] cleanBytes = Encoding.UTF8.GetBytes(jsonText ?? "");

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LauncherSettings));
                using (MemoryStream ms = new MemoryStream(cleanBytes))
                {
                    LauncherSettings loaded = (LauncherSettings)serializer.ReadObject(ms);
                    if (loaded != null)
                    {
                        // Sanitize loaded fields
                        if (!string.IsNullOrEmpty(loaded.Mode)) settings.Mode = loaded.Mode;
                        if (!string.IsNullOrEmpty(loaded.IanaName)) settings.IanaName = loaded.IanaName;
                        if (!string.IsNullOrEmpty(loaded.UtcOffset)) settings.UtcOffset = loaded.UtcOffset;
                        settings.DisableTz = loaded.DisableTz;
                        settings.GracefulClose = loaded.GracefulClose;
                        if (!string.IsNullOrEmpty(loaded.NetworkMode)) settings.NetworkMode = loaded.NetworkMode;
                        if (!string.IsNullOrEmpty(loaded.ProxyHost)) settings.ProxyHost = loaded.ProxyHost;
                        if (loaded.ProxyPort > 0 && loaded.ProxyPort <= 65535) settings.ProxyPort = loaded.ProxyPort;
                        settings.AutoDetectProxy = loaded.AutoDetectProxy;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "加载配置失败: " + ex.Message;
            }

            return settings;
        }

        public static bool Save(LauncherSettings settings, out string error)
        {
            error = null;
            if (settings == null)
            {
                error = "配置对象为空";
                return false;
            }

            try
            {
                string targetPath = ActiveSettingsFilePath;
                string targetDir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string tempFile = Path.Combine(targetDir, string.Format("settings.tmp.{0}.json", Guid.NewGuid().ToString("N")));

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LauncherSettings));
                using (FileStream fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(fs, settings);
                    fs.Flush(true);
                }

                // Safe replace or create
                if (File.Exists(targetPath))
                {
                    string backupFile = targetPath + ".bak";
                    try
                    {
                        if (File.Exists(backupFile))
                        {
                            try { File.Delete(backupFile); } catch { }
                        }

                        File.Replace(tempFile, targetPath, backupFile);
                    }
                    catch (Exception ex)
                    {
                        // Clean temp file, NEVER delete targetPath!
                        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                        error = "替换配置文件失败 (旧配置已安全保留): " + ex.Message;
                        return false;
                    }

                    // Separate backup cleanup
                    try
                    {
                        if (File.Exists(backupFile))
                        {
                            File.Delete(backupFile);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Save was committed, note backup cleanup failure
                        error = "配置已保存，但清理备份文件失败: " + ex.Message;
                        return true;
                    }

                    return true;
                }
                else
                {
                    try
                    {
                        File.Move(tempFile, targetPath);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                        error = "创建新配置文件失败: " + ex.Message;
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
