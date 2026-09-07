using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class TestRunner
    {
        private static int passed = 0;
        private static int failed = 0;

        private static void Assert(bool condition, string testName)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + testName);
                passed++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + testName);
                failed++;
            }
        }

        public static int Main()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("   ChatGPT Windows Launcher Automated Test Suite  ");
            Console.WriteLine("=================================================");
            Console.WriteLine();

            TestDrawingHelpers();
            TestTimezoneHelper();
            TestProxyService();
            TestEnvironmentBuilder();
            TestSettingsStore();
            TestNetworkProbeErrorHandling();

            Console.WriteLine();
            Console.WriteLine("=================================================");
            Console.WriteLine(string.Format("Test Results: {0} Passed, {1} Failed", passed, failed));
            Console.WriteLine("=================================================");

            return (failed == 0) ? 0 : 1;
        }

        private static void TestDrawingHelpers()
        {
            Console.WriteLine("--- Testing DrawingHelpers & GDI+ Safety ---");

            // 1. Zero dimension
            using (GraphicsPath p1 = DrawingHelpers.GetRoundedPath(new Rectangle(0, 0, 0, 0), 8))
            {
                Assert(p1.PointCount == 0, "GetRoundedPath(0, 0, 0, 0) returns empty path without exception");
            }

            // 2. Negative dimension
            using (GraphicsPath p2 = DrawingHelpers.GetRoundedPath(new Rectangle(0, 0, -10, -5), 8))
            {
                Assert(p2.PointCount == 0, "GetRoundedPath(-10, -5) returns empty path without exception");
            }

            // 3. Radius zero
            using (GraphicsPath p3 = DrawingHelpers.GetRoundedPath(new Rectangle(0, 0, 100, 100), 0))
            {
                Assert(p3.PointCount > 0, "GetRoundedPath(100, 100, radius=0) returns standard rectangle");
            }

            // 4. Radius greater than dimensions
            using (GraphicsPath p4 = DrawingHelpers.GetRoundedPath(new Rectangle(0, 0, 10, 10), 100))
            {
                Assert(p4.PointCount > 0, "GetRoundedPath(10, 10, radius=100) clamps radius and creates valid path");
            }

            // 5. Normal rounded rectangle
            using (GraphicsPath p5 = DrawingHelpers.GetRoundedPath(new Rectangle(0, 0, 200, 100), 10))
            {
                Assert(p5.PointCount > 0, "GetRoundedPath(200, 100, radius=10) creates valid rounded path");
            }
        }

        private static void TestTimezoneHelper()
        {
            Console.WriteLine("--- Testing TimezoneHelper ---");

            // 1. Standard hour offsets
            TimeSpan? ts1 = TimezoneHelper.ParseOffset("UTC-8");
            Assert(ts1.HasValue && ts1.Value.TotalHours == -8, "ParseOffset 'UTC-8' == -8h");

            TimeSpan? ts2 = TimezoneHelper.ParseOffset("+08:00");
            Assert(ts2.HasValue && ts2.Value.TotalHours == 8, "ParseOffset '+08:00' == +8h");

            // 2. Fractional offsets
            TimeSpan? ts3 = TimezoneHelper.ParseOffset("+05:45");
            Assert(ts3.HasValue && (int)ts3.Value.TotalMinutes == 345, "ParseOffset '+05:45' == 345min (Kathmandu)");

            TimeSpan? ts4 = TimezoneHelper.ParseOffset("-03:30");
            Assert(ts4.HasValue && (int)ts4.Value.TotalMinutes == -210, "ParseOffset '-03:30' == -210min (Newfoundland)");

            // 3. Invalid offset should return null, not 0!
            TimeSpan? tsNull = TimezoneHelper.ParseOffset("invalid_offset");
            Assert(!tsNull.HasValue, "ParseOffset invalid returns null (not 0)");

            // 4. Formatting
            string fmt1 = TimezoneHelper.FormatOffset(ts1);
            Assert(fmt1 == "UTC-8", "FormatOffset -8h -> 'UTC-8'");

            string fmt3 = TimezoneHelper.FormatOffset(ts3);
            Assert(fmt3 == "UTC+5:45", "FormatOffset 345min -> 'UTC+5:45'");

            string fmtNull = TimezoneHelper.FormatOffset(null);
            Assert(fmtNull == "未知", "FormatOffset null -> '未知'");

            // 5. POSIX V8 Mapping (In POSIX, west of UTC is positive: UTC-8 -> Etc/GMT+8)
            string posix1 = TimezoneHelper.MapUtcOffsetToPosixTz("UTC-8");
            Assert(posix1 == "Etc/GMT+8", "MapUtcOffsetToPosixTz 'UTC-8' -> 'Etc/GMT+8'");

            string posix2 = TimezoneHelper.MapUtcOffsetToPosixTz("UTC+9");
            Assert(posix2 == "Etc/GMT-9", "MapUtcOffsetToPosixTz 'UTC+9' -> 'Etc/GMT-9'");

            // 6. Matching logic
            bool match1 = TimezoneHelper.IsTimezoneMatch("iana", "America/Los_Angeles", "UTC-8", "America/Los_Angeles", ts1);
            Assert(match1, "IsTimezoneMatch IANA exact match is true");

            bool match2 = TimezoneHelper.IsTimezoneMatch("iana", "America/New_York", "UTC-5", "Asia/Tokyo", ts2);
            Assert(!match2, "IsTimezoneMatch IANA mismatch is false");

            bool matchUtc = TimezoneHelper.IsTimezoneMatch("utc", "America/Los_Angeles", "UTC-8", "America/Vancouver", ts1);
            Assert(matchUtc, "IsTimezoneMatch UTC offset match is true");
        }

        private static void TestProxyService()
        {
            Console.WriteLine("--- Testing ProxyService & ProxyConfig ---");

            ProxyConfig p1 = new ProxyConfig("127.0.0.1", 7897);
            Assert(p1.IsValid(), "ProxyConfig valid IP and port");
            Assert(p1.ToUrl() == "http://127.0.0.1:7897", "ProxyConfig ToUrl http://127.0.0.1:7897");

            ProxyConfig pInvalid = new ProxyConfig("127.0.0.1", 70000);
            Assert(!pInvalid.IsValid(), "ProxyConfig port 70000 is invalid");

            // Check listening on a non-existent port (e.g. 59998)
            bool listening = ProxyService.IsPortListening("127.0.0.1", 59998, 100);
            Assert(!listening, "ProxyService.IsPortListening returns false for unused port 59998");

            // Test port extraction from proxy registry string formats
            Assert(ProxyService.ParsePortFromProxyServer("127.0.0.1:7897") == 7897, "ParsePortFromProxyServer '127.0.0.1:7897' == 7897");
            Assert(ProxyService.ParsePortFromProxyServer("http=127.0.0.1:7890;https=127.0.0.1:7890") == 7890, "ParsePortFromProxyServer multi-protocol == 7890");
            Assert(ProxyService.ParsePortFromProxyServer("socks=127.0.0.1:10808") == 10808, "ParsePortFromProxyServer socks prefix == 10808");
            Assert(ProxyService.ParsePortFromProxyServer("") == 0, "ParsePortFromProxyServer empty returns 0");
        }

        private static void TestEnvironmentBuilder()
        {
            Console.WriteLine("--- Testing ProcessEnvironmentBuilder ---");

            LauncherSettings sVpn = new LauncherSettings
            {
                NetworkMode = "vpn",
                ProxyHost = "127.0.0.1",
                ProxyPort = 7897,
                Mode = "iana",
                IanaName = "Asia/Tokyo"
            };

            Dictionary<string, string> envVpn = ProcessEnvironmentBuilder.BuildEnvironment(sVpn);
            Assert(envVpn.ContainsKey("TZ") && envVpn["TZ"] == "Asia/Tokyo", "ProcessEnvironmentBuilder sets TZ=Asia/Tokyo");
            Assert(envVpn.ContainsKey("HTTP_PROXY") && envVpn["HTTP_PROXY"] == "http://127.0.0.1:7897", "ProcessEnvironmentBuilder sets HTTP_PROXY");
            Assert(envVpn.ContainsKey("ALL_PROXY") && envVpn["ALL_PROXY"] == "http://127.0.0.1:7897", "ProcessEnvironmentBuilder sets ALL_PROXY");
            Assert(envVpn.ContainsKey("NO_PROXY"), "ProcessEnvironmentBuilder sets NO_PROXY");

            // Direct mode
            LauncherSettings sDirect = new LauncherSettings
            {
                NetworkMode = "direct",
                Mode = "utc",
                UtcOffset = "UTC-8"
            };

            Dictionary<string, string> envDirect = ProcessEnvironmentBuilder.BuildEnvironment(sDirect);
            Assert(envDirect.ContainsKey("TZ") && envDirect["TZ"] == "Etc/GMT+8", "ProcessEnvironmentBuilder maps UTC-8 -> Etc/GMT+8 in UTC mode");
            Assert(envDirect.ContainsKey("HTTP_PROXY") && envDirect["HTTP_PROXY"] == null, "ProcessEnvironmentBuilder clears HTTP_PROXY in Direct mode");
            Assert(envDirect.ContainsKey("ALL_PROXY") && envDirect["ALL_PROXY"] == null, "ProcessEnvironmentBuilder clears ALL_PROXY in Direct mode");

            // Disabled TZ
            LauncherSettings sNoTz = new LauncherSettings
            {
                DisableTz = true
            };
            Dictionary<string, string> envNoTz = ProcessEnvironmentBuilder.BuildEnvironment(sNoTz);
            Assert(envNoTz.ContainsKey("TZ") && envNoTz["TZ"] == null, "ProcessEnvironmentBuilder clears TZ when DisableTz=true");
        }

        private static void TestSettingsStore()
        {
            Console.WriteLine("--- Testing SettingsStore Serialization & Atomic Write ---");

            string testPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_settings.json");
            SettingsStore.CustomSettingsFilePath = testPath;

            try
            {
                LauncherSettings original = new LauncherSettings
                {
                    Mode = "iana",
                    IanaName = "Europe/Paris",
                    UtcOffset = "UTC+1",
                    DisableTz = false,
                    GracefulClose = true,
                    NetworkMode = "vpn",
                    ProxyHost = "127.0.0.1",
                    ProxyPort = 10808
                };

                string saveErr;
                bool saved = SettingsStore.Save(original, out saveErr);
                Assert(saved && string.IsNullOrEmpty(saveErr), "SettingsStore.Save succeeds atomically");

                string loadErr;
                LauncherSettings reloaded = SettingsStore.Load(out loadErr);
                Assert(reloaded != null && string.IsNullOrEmpty(loadErr), "SettingsStore.Load succeeds");
                Assert(reloaded.IanaName == "Europe/Paris", "SettingsStore preserved IanaName");
                Assert(reloaded.ProxyPort == 10808, "SettingsStore preserved ProxyPort");
                Assert(reloaded.Mode == "iana", "SettingsStore preserved Mode");
                Assert(reloaded.AutoDetectProxy == true, "SettingsStore loaded AutoDetectProxy default true");

                // Toggle AutoDetectProxy to false and verify roundtrip
                original.AutoDetectProxy = false;
                SettingsStore.Save(original, out saveErr);
                LauncherSettings reloadedManual = SettingsStore.Load(out loadErr);
                Assert(reloadedManual.AutoDetectProxy == false, "SettingsStore preserved AutoDetectProxy = false");

                // Test loading settings file with explicit UTF-8 BOM
                byte[] bom = Encoding.UTF8.GetPreamble();
                byte[] body = Encoding.UTF8.GetBytes("{\"mode\":\"utc\",\"utc_offset\":\"UTC+8\",\"proxy_port\":7890}");
                byte[] fullBom = new byte[bom.Length + body.Length];
                Buffer.BlockCopy(bom, 0, fullBom, 0, bom.Length);
                Buffer.BlockCopy(body, 0, fullBom, bom.Length, body.Length);
                File.WriteAllBytes(testPath, fullBom);

                LauncherSettings reloadedBom = SettingsStore.Load(out loadErr);
                Assert(reloadedBom != null && string.IsNullOrEmpty(loadErr), "SettingsStore.Load successfully handles UTF-8 BOM");
                Assert(reloadedBom.ProxyPort == 7890, "SettingsStore parsed ProxyPort 7890 from BOM file");
            }
            finally
            {
                SettingsStore.CustomSettingsFilePath = null;
                try { if (File.Exists(testPath)) File.Delete(testPath); } catch { }
            }
        }

        private static void TestNetworkProbeErrorHandling()
        {
            Console.WriteLine("--- Testing NetworkProbeService Error Handling ---");

            // Probe with non-existent proxy port
            ProxyConfig invalidProxy = new ProxyConfig("127.0.0.1", 59997);
            int token = NetworkProbeService.NextGeneration();
            ProbeResult result = NetworkProbeService.ProbeNode(invalidProxy, true, token, 500);

            Assert(!result.Success, "ProbeNode with closed proxy port fails (does not fake success)");
            Assert(result.ErrorKind == ProbeErrorKind.ProxyUnreachable || result.ErrorKind == ProbeErrorKind.Timeout,
                string.Format("ProbeNode classifies error accurately (got {0}: {1})", result.ErrorKind, result.ErrorMessage));
        }
    }
}
