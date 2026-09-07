using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class ChildProcessEnvTest
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--child-dump")
            {
                // Child mode: print target environment variables to stdout
                Console.WriteLine("CHILD_TZ=" + Environment.GetEnvironmentVariable("TZ"));
                Console.WriteLine("CHILD_HTTP_PROXY=" + Environment.GetEnvironmentVariable("HTTP_PROXY"));
                Console.WriteLine("CHILD_ALL_PROXY=" + Environment.GetEnvironmentVariable("ALL_PROXY"));
                return 0;
            }

            Console.WriteLine("--- Testing Child Process Environment Isolation ---");

            // Set dummy proxy variables in parent process to test removal
            Environment.SetEnvironmentVariable("HTTP_PROXY", "http://parent-leak:8888");
            Environment.SetEnvironmentVariable("ALL_PROXY", "http://parent-leak:8888");

            string selfExe = Process.GetCurrentProcess().MainModule.FileName;

            // 1. Test VPN mode injection
            LauncherSettings sVpn = new LauncherSettings
            {
                NetworkMode = "vpn",
                ProxyHost = "127.0.0.1",
                ProxyPort = 7897,
                Mode = "iana",
                IanaName = "Asia/Tokyo"
            };
            Dictionary<string, string> envVpn = ProcessEnvironmentBuilder.BuildEnvironment(sVpn);

            ProcessStartInfo psiVpn = new ProcessStartInfo
            {
                FileName = selfExe,
                Arguments = "--child-dump",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            foreach (KeyValuePair<string, string> kv in envVpn)
            {
                if (kv.Value != null) psiVpn.EnvironmentVariables[kv.Key] = kv.Value;
                else if (psiVpn.EnvironmentVariables.ContainsKey(kv.Key)) psiVpn.EnvironmentVariables.Remove(kv.Key);
            }

            string outVpn;
            using (Process p = Process.Start(psiVpn))
            {
                outVpn = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            bool pass1 = outVpn.Contains("CHILD_TZ=Asia/Tokyo") &&
                         outVpn.Contains("CHILD_HTTP_PROXY=http://127.0.0.1:7897") &&
                         outVpn.Contains("CHILD_ALL_PROXY=http://127.0.0.1:7897");
            Console.WriteLine((pass1 ? "[PASS]" : "[FAIL]") + " VPN mode child process received injected TZ and Proxy");

            // 2. Test Direct mode removal (must NOT leak parent's HTTP_PROXY)
            LauncherSettings sDirect = new LauncherSettings
            {
                NetworkMode = "direct",
                DisableTz = true
            };
            Dictionary<string, string> envDirect = ProcessEnvironmentBuilder.BuildEnvironment(sDirect);

            ProcessStartInfo psiDirect = new ProcessStartInfo
            {
                FileName = selfExe,
                Arguments = "--child-dump",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            foreach (KeyValuePair<string, string> kv in envDirect)
            {
                if (kv.Value != null) psiDirect.EnvironmentVariables[kv.Key] = kv.Value;
                else if (psiDirect.EnvironmentVariables.ContainsKey(kv.Key)) psiDirect.EnvironmentVariables.Remove(kv.Key);
            }

            string outDirect;
            using (Process p = Process.Start(psiDirect))
            {
                outDirect = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
            }

            bool pass2 = outDirect.Contains("CHILD_TZ=") && !outDirect.Contains("CHILD_TZ=Asia/Tokyo") &&
                         outDirect.Contains("CHILD_HTTP_PROXY=") && !outDirect.Contains("CHILD_HTTP_PROXY=http://") &&
                         outDirect.Contains("CHILD_ALL_PROXY=") && !outDirect.Contains("CHILD_ALL_PROXY=http://");
            Console.WriteLine((pass2 ? "[PASS]" : "[FAIL]") + " Direct mode child process has NO proxy variables (parent leak prevented)");

            // Cleanup parent process environment
            Environment.SetEnvironmentVariable("HTTP_PROXY", null);
            Environment.SetEnvironmentVariable("ALL_PROXY", null);

            return (pass1 && pass2) ? 0 : 1;
        }
    }
}
