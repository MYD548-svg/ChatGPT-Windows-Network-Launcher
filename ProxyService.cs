using System;
using System.Net.Sockets;

namespace ChatGPTAntiBanLauncher
{
    public sealed class ProxyConfig
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Scheme { get; set; }

        public ProxyConfig()
        {
            Host = "127.0.0.1";
            Port = 7897;
            Scheme = "http";
        }

        public ProxyConfig(string host, int port, string scheme = "http")
        {
            Host = string.IsNullOrEmpty(host) ? "127.0.0.1" : host.Trim();
            Port = port;
            Scheme = string.IsNullOrEmpty(scheme) ? "http" : scheme.Trim().ToLowerInvariant();
        }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(Host)) return false;
            if (Port <= 0 || Port > 65535) return false;
            return true;
        }

        public string ToUrl()
        {
            string s = string.IsNullOrEmpty(Scheme) ? "http" : Scheme;
            return string.Format("{0}://{1}:{2}", s, Host, Port);
        }
    }

    public static class ProxyService
    {
        public static bool IsPortListening(string host, int port, int timeoutMs = 200)
        {
            if (string.IsNullOrEmpty(host) || port <= 0 || port > 65535) return false;

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(host, port, null, null);
                    bool success = ar.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!success) return false;

                    client.EndConnect(ar);
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public static int ParsePortFromProxyServer(string serverStr)
        {
            if (string.IsNullOrEmpty(serverStr)) return 0;
            string s = serverStr.Trim();

            if (s.Contains(";"))
            {
                string[] parts = s.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    int p = ExtractPort(part);
                    if (p > 0) return p;
                }
            }
            return ExtractPort(s);
        }

        private static int ExtractPort(string entry)
        {
            if (string.IsNullOrEmpty(entry)) return 0;
            int eqIdx = entry.IndexOf('=');
            if (eqIdx >= 0 && eqIdx < entry.Length - 1)
            {
                entry = entry.Substring(eqIdx + 1).Trim();
            }

            int colonIdx = entry.LastIndexOf(':');
            if (colonIdx >= 0 && colonIdx < entry.Length - 1)
            {
                string portStr = entry.Substring(colonIdx + 1).Trim().TrimEnd('/');
                int port;
                if (int.TryParse(portStr, out port) && port > 0 && port <= 65535)
                {
                    return port;
                }
            }
            return 0;
        }

        public static int DetectActiveProxyPort(int preferredPort = 0)
        {
            // 1. Check system proxy from registry
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key != null)
                    {
                        object enableVal = key.GetValue("ProxyEnable");
                        if (enableVal != null && Convert.ToInt32(enableVal) == 1)
                        {
                            string serverStr = key.GetValue("ProxyServer") as string;
                            int regPort = ParsePortFromProxyServer(serverStr);
                            if (regPort > 0 && IsPortListening("127.0.0.1", regPort, 80))
                            {
                                return regPort;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Check preferredPort if valid
            if (preferredPort > 0 && preferredPort <= 65535 && IsPortListening("127.0.0.1", preferredPort, 80))
            {
                return preferredPort;
            }

            // 3. Scan common candidate ports
            int[] candidates = new int[] { 7897, 7890, 10808, 1080, 2080, 10809 };
            for (int i = 0; i < candidates.Length; i++)
            {
                int p = candidates[i];
                if (p != preferredPort && IsPortListening("127.0.0.1", p, 80))
                {
                    return p;
                }
            }

            return 0;
        }
    }
}
