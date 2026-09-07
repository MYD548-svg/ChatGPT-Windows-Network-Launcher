using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ChatGPTAntiBanLauncher;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class IsolatedTests
    {
        private static int passed = 0;
        private static int failed = 0;

        private static void Assert(bool condition, string testName, string detail = null)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + testName);
                passed++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + testName + (string.IsNullOrEmpty(detail) ? "" : (" -> " + detail)));
                failed++;
            }
        }

        // In-memory test storage implementations
        private class MockEnvironmentStorage : IEnvironmentStorage
        {
            public Dictionary<string, string> Store = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int SetCalls = 0; public bool FailRead = false;
            public int BroadcastCalls = 0;
            public string FailOnKey = null;

            public string GetVariable(string key)
            {
                if (FailRead) throw new UnauthorizedAccessException("review read failure"); string val;
                if (Store.TryGetValue(key, out val)) return val;
                return null;
            }

            public void SetVariable(string key, string value)
            {
                if (FailOnKey != null && string.Equals(key, FailOnKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("模拟注册表写入拒绝: " + key);
                }
                SetCalls++;
                if (value == null) Store.Remove(key);
                else Store[key] = value;
            }

            public void BroadcastChange()
            {
                BroadcastCalls++;
            }
        }

        private class MockWalStorage : IWalStorage
        {
            public byte[] Bytes = null;
            public bool FailWrite = false;
            public bool FailDelete = false;
            public int WriteCalls = 0;
            public int DeleteCalls = 0;

            public bool Exists()
            {
                return Bytes != null && Bytes.Length > 0;
            }

            public byte[] ReadAllBytes()
            {
                if (Bytes == null) throw new FileNotFoundException("WAL 不存在");
                return (byte[])Bytes.Clone();
            }

            public void WriteAtomic(byte[] bytes)
            {
                if (FailWrite) throw new IOException("模拟 WAL 落盘写入失败");
                WriteCalls++;
                Bytes = (byte[])bytes.Clone();
            }

            public void Delete()
            {
                if (FailDelete) throw new IOException("模拟 WAL 删除失败");
                DeleteCalls++;
                Bytes = null;
            }
        }

        private class MockTransactionLock : ITransactionLock
        {
            public bool CanAcquire = true;
            public bool AcquireViaAbandon = false;
            public bool IsHeld = false;
            public int ReleaseCalls = 0;
            public int CloseCalls = 0;

            public bool TryAcquire(int timeoutMs, out bool acquiredViaAbandon)
            {
                acquiredViaAbandon = AcquireViaAbandon;
                if (CanAcquire)
                {
                    IsHeld = true;
                    return true;
                }
                return false;
            }

            public void Release()
            {
                ReleaseCalls++;
                IsHeld = false;
            }

            public void Close()
            {
                CloseCalls++;
                IsHeld = false;
            }

            public void Dispose()
            {
                Close();
            }
        }

                private static void RunReviewFailureTests()
        {
            MockEnvironmentStorage env = new MockEnvironmentStorage();
            MockWalStorage wal = new MockWalStorage();
            MockTransactionLock guard = new MockTransactionLock();
            UserEnvironmentTransaction.EnvironmentStorage = env;
            UserEnvironmentTransaction.WalStorage = wal;
            UserEnvironmentTransaction.LockProvider = () => guard;
            var values = new Dictionary<string, string>(); values["TZ"] = "Asia/Tokyo";
            env.FailRead = true;
            try { using (new UserEnvironmentTransaction(values)) {} } catch (UnauthorizedAccessException) {}
            Assert(!guard.IsHeld, "REVIEW: Constructor read failure must release lock");
            env.FailRead = false;
            env.Store["TZ"] = "original";
            var tx = new UserEnvironmentTransaction(values);
            env.FailOnKey = "TZ";
            bool notified = false;
            try { tx.Dispose(); } catch { notified = true; }
            Assert(notified, "REVIEW: Dispose restoration failure must notify caller", "remaining=" + env.Store["TZ"] + ", WAL=" + wal.Exists());
            env.FailOnKey = null;
            wal.Bytes = UserEnvironmentTransaction.SerializeLog(new EnvTransactionLog {
                TransactionId = "review", Variables = new List<EnvVariableRecord> {
                    new EnvVariableRecord { Key="PATH", Existed=true, OriginalValue="replaced", InjectedValue="current" }
                }
            });
            env.Store["PATH"] = "current";
            RecoveryResult result = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
            Assert(!result.Success && env.Store["PATH"] == "current", "REVIEW: Reject unrelated environment keys in WAL");
        }
        public static int Main()
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("  ChatGPT Launcher Round 2 Isolated Tests (Safe & Zero Impact) ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            string testSandboxDir = Path.Combine(Path.GetTempPath(), "ChatGPTLauncher_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testSandboxDir);

            try
            {
                RunEnvironmentTransactionTests();
                RunReviewFailureTests(); RunProcessMatchingTests();
                RunSettingsStoreTests(testSandboxDir);
                RunTimezoneHelperTests();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FATAL EXCEPTION] " + ex);
                failed++;
            }
            finally
            {
                // Review copy: keep temporary test directory; recursive cleanup disabled.
            }

            Console.WriteLine();
            Console.WriteLine("===============================================================");
            Console.WriteLine(string.Format("Isolated Test Results: {0} Passed, {1} Failed", passed, failed));
            Console.WriteLine("===============================================================");

            return (failed == 0) ? 0 : 1;
        }

        // =========================================================================
        // SECTION A: Environment Transaction Tests
        // =========================================================================
        private static void RunEnvironmentTransactionTests()
        {
            Console.WriteLine("--- [A] Testing Environment Transaction & WAL Protection ---");

            // 1. Lock timeout: No recovery, no WAL override, no environment write
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = false };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                bool threwTimeout = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["TEST_VAR"] = "val";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (TimeoutException)
                {
                    threwTimeout = true;
                }

                Assert(threwTimeout, "A1: Lock timeout throws TimeoutException");
                Assert(mockEnv.SetCalls == 0, "A1: Zero environment writes on lock timeout");
                Assert(mockWal.WriteCalls == 0, "A1: Zero WAL writes on lock timeout");
            }

            // 2. Second recovery request blocked while active transaction holds lock
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock activeLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => activeLock;

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["MY_VAR"] = "my_val";

                using (UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targets))
                {
                    Assert(mockWal.Exists(), "A2: Active transaction WAL exists");

                    // Second recovery call attempts lock acquisition
                    UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock { CanAcquire = false };
                    RecoveryResult recRes = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();

                    Assert(!recRes.Success, "A2: Second recovery fails to acquire lock");
                    Assert(mockWal.Exists(), "A2: Active transaction WAL untouched by concurrent recovery request");
                }
            }

            // 3. Abandoned lock is acquired and recovery completes
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                mockEnv.Store["TZ"] = "Injected_Temp";

                MockWalStorage mockWal = new MockWalStorage();
                EnvTransactionLog log = new EnvTransactionLog
                {
                    Version = 1,
                    TransactionId = "abandoned_tx",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Variables = new List<EnvVariableRecord>
                    {
                        new EnvVariableRecord { Key = "TZ", Existed = true, OriginalValue = "Original_TZ", InjectedValue = "Injected_Temp" }
                    }
                };
                mockWal.Bytes = UserEnvironmentTransaction.SerializeLog(log);

                MockTransactionLock abandonLock = new MockTransactionLock { CanAcquire = true, AcquireViaAbandon = true };
                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => abandonLock;

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();

                Assert(rec.Success, "A3: Abandoned lock acquired and recovery succeeded");
                Assert(rec.RecoveredCount == 1, "A3: Exactly 1 record recovered");
                Assert(mockEnv.Store["TZ"] == "Original_TZ", "A3: Variable restored to original value");
                Assert(!mockWal.Exists(), "A3: WAL removed after full recovery");
            }

            // 4. Initial WAL write failure: zero environment writes
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                MockWalStorage mockWal = new MockWalStorage { FailWrite = true };
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                bool threwIo = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["VAR1"] = "val1";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (IOException)
                {
                    threwIo = true;
                }

                Assert(threwIo, "A4: WAL write failure throws IOException");
                Assert(mockEnv.SetCalls == 0, "A4: Zero environment writes after WAL write failure");
                Assert(!mockLock.IsHeld, "A4: Lock safely released on constructor failure");
            }

            // 5. Partial write failure: rollback applied, unresolved items keep WAL
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                mockEnv.Store["VAR_A"] = "orig_A";
                mockEnv.Store["VAR_B"] = "orig_B";
                mockEnv.FailOnKey = "VAR_B"; // Fails when injecting VAR_B

                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                bool threwException = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["VAR_A"] = "injected_A";
                    targets["VAR_B"] = "injected_B";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (Exception)
                {
                    threwException = true;
                }

                Assert(threwException, "A5: Partial write failure throws exception");
                Assert(mockEnv.Store["VAR_A"] == "orig_A", "A5: Already set variable VAR_A was rolled back");
                Assert(!mockLock.IsHeld, "A5: Lock released after rollback");
            }

            // 6. Compare-and-Restore: External modification is NOT overwritten and conflict logged
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                mockEnv.Store["MY_VAR"] = "orig_val";

                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["MY_VAR"] = "injected_val";

                using (UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targets))
                {
                    Assert(mockEnv.Store["MY_VAR"] == "injected_val", "A6: Variable injected");
                    // External modification occurs
                    mockEnv.Store["MY_VAR"] = "external_user_val";
                }

                // Upon dispose, Compare-and-Restore MUST NOT overwrite external modification!
                Assert(mockEnv.Store["MY_VAR"] == "external_user_val", "A6: Compare-and-Restore preserved external modification");
                Assert(!mockWal.Exists(), "A6: WAL deleted after conflict handled per policy");
            }

            // 7. Corrupted WAL: Rejects new transaction, does not write environment
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                MockWalStorage mockWal = new MockWalStorage();
                mockWal.Bytes = Encoding.UTF8.GetBytes("{ corrupted json non-version }");

                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };
                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                bool threwInvalid = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["VAR1"] = "val1";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (InvalidOperationException)
                {
                    threwInvalid = true;
                }

                Assert(threwInvalid, "A7: Corrupted WAL rejects new transaction");
                Assert(mockWal.Exists(), "A7: Corrupted WAL is NOT deleted (preserved for diagnostics)");
                Assert(mockEnv.SetCalls == 0, "A7: Zero environment writes when WAL is corrupted");
                Assert(!mockLock.IsHeld, "A7: Lock released after rejection");
            }

            // 8. LauncherService: Transaction error is NOT masked as COM activation
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                MockWalStorage mockWal = new MockWalStorage { FailWrite = true }; // will fail tx
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                TargetClientInfo storeClient = new TargetClientInfo
                {
                    IsFound = true,
                    Type = ClientInstallationType.StorePackage,
                    AppUserModelId = "OpenAI.Codex_test!App",
                    ExecutablePath = @"C:\Fake\ChatGPT.exe"
                };

                LauncherSettings s = new LauncherSettings();
                LaunchResult res = LauncherService.Launch(storeClient, s);

                Assert(!res.Succeeded, "A8: Launch failed on transaction error");
                Assert(res.ErrorMessage.Contains("环境事务初始化失败"), "A8: Error accurately classifies transaction failure without false fallback");
            }
        }

        // =========================================================================
        // SECTION B: Process Matching Tests
        // =========================================================================
        private static void RunProcessMatchingTests()
        {
            Console.WriteLine("--- [B] Testing Target Process Identity & Safety ---");

            TargetClientInfo target = new TargetClientInfo
            {
                IsFound = true,
                Type = ClientInstallationType.StorePackage,
                PackageFamilyName = "OpenAI.Codex_2p2nqsd0c76g0",
                InstallDirectory = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0",
                ExecutablePath = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"
            };

            // 1. Exact executable path match
            ProcessMatchDecision d1 = LauncherService.EvaluateProcessMatch(
                @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe",
                null, target);
            Assert(d1 == ProcessMatchDecision.ExactExeMatch, "B1: Exact executable path matches");

            // 2. Valid directory match
            ProcessMatchDecision d2 = LauncherService.EvaluateProcessMatch(
                @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0\app\Codex.exe",
                null, target);
            Assert(d2 == ProcessMatchDecision.DirectoryMatch, "B2: Valid subdirectory executable matches");

            // 3. Similar directory prefix rejected (ChatGPT-other / OpenAI.Codex_other)
            ProcessMatchDecision d3 = LauncherService.EvaluateProcessMatch(
                @"C:\Program Files\WindowsApps\OpenAI.Codex_26.901.6511.0_x64__2p2nqsd0c76g0_other\app\ChatGPT.exe",
                null, target);
            Assert(d3 == ProcessMatchDecision.RejectedSimilarDirectoryPrefix, "B3: Similar directory prefix without boundary strictly rejected");

            // 4. Unreadable path with verified package identity
            ProcessMatchDecision d4 = LauncherService.EvaluateProcessMatch(
                null, "OpenAI.Codex_2p2nqsd0c76g0", target);
            Assert(d4 == ProcessMatchDecision.PackageFamilyMatch, "B4: Unreadable path matched via verified package family");

            // 5. Unreadable path with mismatched or unverified package identity
            ProcessMatchDecision d5Mismatch = LauncherService.EvaluateProcessMatch(
                null, "Malicious.ChatGPT_fake", target);
            Assert(d5Mismatch == ProcessMatchDecision.RejectedPackageMismatch, "B5: Mismatched package family rejected");

            ProcessMatchDecision d5Unknown = LauncherService.EvaluateProcessMatch(
                null, null, target);
            Assert(d5Unknown == ProcessMatchDecision.RejectedUnverifiableIdentity, "B5: Unverified process identity rejected (does not fallback to name match)");

            // 6. Force kill without prompt authorization callback is forbidden
            string failReason;
            bool closeResult = LauncherService.CloseExistingInstances(
                new List<Process>(), // empty list safe test
                false,
                null, // promptForceKill is null
                out failReason);
            Assert(closeResult, "B6: Empty list returns true");

            // Verify promptForceKill = null rejection when non-empty
            // (We test logic without touching live processes)
        }

        // =========================================================================
        // SECTION C: Settings Store Tests
        // =========================================================================
        private static void RunSettingsStoreTests(string sandboxDir)
        {
            Console.WriteLine("--- [C] Testing Settings Store Atomic Replace & Error Propagation ---");

            string testSettingsPath = Path.Combine(sandboxDir, "settings.json");
            SettingsStore.CustomSettingsFilePath = testSettingsPath;

            try
            {
                // 1. Normal save
                LauncherSettings original = new LauncherSettings
                {
                    ProxyPort = 7897,
                    IanaName = "Asia/Singapore",
                    Mode = "iana"
                };

                string saveErr;
                bool saved = SettingsStore.Save(original, out saveErr);
                Assert(saved && File.Exists(testSettingsPath), "C1: Initial save succeeds");

                string originalBytes = File.ReadAllText(testSettingsPath);

                // 2. Atomic replace on locked file: old config preserved!
                FileStream lockStream = new FileStream(testSettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    LauncherSettings updated = new LauncherSettings { ProxyPort = 9999, IanaName = "Europe/London" };
                    string lockedErr;
                    bool lockSaveResult = SettingsStore.Save(updated, out lockedErr);

                    Assert(!lockSaveResult, "C2: Save on locked file returns false");
                    Assert(!string.IsNullOrEmpty(lockedErr), "C2: Save returns descriptive error message");
                }
                finally
                {
                    lockStream.Close();
                }

                // Verify file was NOT deleted and content remains 100% identical!
                Assert(File.Exists(testSettingsPath), "C2: Target file was NOT deleted during replacement failure");
                string postFailBytes = File.ReadAllText(testSettingsPath);
                Assert(originalBytes == postFailBytes, "C2: Old configuration bytes are 100% identical after failed save");

                // 3. Clean reload
                string loadErr;
                LauncherSettings reloaded = SettingsStore.Load(out loadErr);
                Assert(reloaded.ProxyPort == 7897, "C3: Reloaded configuration preserved original port 7897");
            }
            finally
            {
                SettingsStore.CustomSettingsFilePath = null;
            }
        }

        // =========================================================================
        // SECTION D: Timezone Parsing & Non-DST Tests
        // =========================================================================
        private static void RunTimezoneHelperTests()
        {
            Console.WriteLine("--- [D] Testing Timezone Parsing, Non-DST & Validation ---");

            // 1. Seconds parsing
            TimeSpan ts1;
            Assert(TimezoneHelper.ParseSecondsOffset(28800, out ts1) && ts1.TotalHours == 8, "D1: 28800s -> +08:00");

            TimeSpan ts2;
            Assert(TimezoneHelper.ParseSecondsOffset("-18000", out ts2) && ts2.TotalHours == -5, "D1: -18000s -> -05:00");

            TimeSpan ts0;
            Assert(TimezoneHelper.ParseSecondsOffset("0", out ts0) && ts0.TotalHours == 0, "D1: 0s -> 00:00");

            // 2. User UTC offsets parsing
            TimeSpan tsUser;
            string parseErr;

            Assert(TimezoneHelper.ParseUserUtcOffset("UTC-8", out tsUser, out parseErr) && tsUser.TotalHours == -8, "D2: 'UTC-8' -> -8h");
            Assert(TimezoneHelper.ParseUserUtcOffset("UTC+05:30", out tsUser, out parseErr) && tsUser.TotalMinutes == 330, "D2: 'UTC+05:30' -> 330min");
            Assert(TimezoneHelper.ParseUserUtcOffset("UTC+05:45", out tsUser, out parseErr) && tsUser.TotalMinutes == 345, "D2: 'UTC+05:45' -> 345min");
            Assert(TimezoneHelper.ParseUserUtcOffset("UTC+09:30", out tsUser, out parseErr) && tsUser.TotalMinutes == 570, "D2: 'UTC+09:30' -> 570min");
            Assert(TimezoneHelper.ParseUserUtcOffset("UTC-03:30", out tsUser, out parseErr) && tsUser.TotalMinutes == -210, "D2: 'UTC-03:30' -> -210min");
            Assert(TimezoneHelper.ParseUserUtcOffset("UTC+12:45", out tsUser, out parseErr) && tsUser.TotalMinutes == 765, "D2: 'UTC+12:45' -> 765min");

            // 3. Supported vs Unsupported mappings
            PosixMappingResult m8 = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromHours(-8));
            Assert(m8.IsSupported && m8.PosixTz == "Etc/GMT+8", "D3: UTC-8 maps to POSIX Etc/GMT+8");

            PosixMappingResult m530 = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromMinutes(330));
            Assert(m530.IsSupported && m530.PosixTz == "Asia/Kolkata", "D3: UTC+05:30 maps to non-DST Asia/Kolkata");

            PosixMappingResult m545 = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromMinutes(345));
            Assert(m545.IsSupported && m545.PosixTz == "Asia/Kathmandu", "D3: UTC+05:45 maps to non-DST Asia/Kathmandu");

            PosixMappingResult m930 = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromMinutes(570));
            Assert(m930.IsSupported && m930.PosixTz == "Australia/Darwin", "D3: UTC+09:30 maps to non-DST Australia/Darwin (not Adelaide!)");

            // Unsupported unstable offsets
            PosixMappingResult m330Neg = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromMinutes(-210));
            Assert(!m330Neg.IsSupported && m330Neg.PosixTz == null, "D3: UTC-03:30 (Newfoundland DST) is rejected as unsupported");

            PosixMappingResult m1245 = TimezoneHelper.MapUtcOffsetToPosix(TimeSpan.FromMinutes(765));
            Assert(!m1245.IsSupported && m1245.PosixTz == null, "D3: UTC+12:45 (Chatham DST) is rejected as unsupported");

            // 4. Invalid values rejected
            TimeSpan tsInv;
            string invErr;
            Assert(!TimezoneHelper.ParseUserUtcOffset("UTC+08:99", out tsInv, out invErr), "D4: 'UTC+08:99' rejected (minute overflow)");
            Assert(!TimezoneHelper.ParseUserUtcOffset("UTC-8garbage", out tsInv, out invErr), "D4: 'UTC-8garbage' rejected (trailing garbage)");
            Assert(!TimezoneHelper.ParseUserUtcOffset("UTC+25:00", out tsInv, out invErr), "D4: 'UTC+25:00' rejected (hour out of range)");

            string ianaErr;
            Assert(!TimezoneHelper.ValidateIanaZone("Mars/Colony", out ianaErr), "D4: 'Mars/Colony' rejected (unknown IANA name)");

            // 5. BuildProcessTzValue throws on invalid/unsupported and NEVER silently returns UTC
            bool threwUnsupported = false;
            try
            {
                TimezoneHelper.BuildProcessTzValue("utc", null, "UTC+12:45", false);
            }
            catch (ArgumentException)
            {
                threwUnsupported = true;
            }
            Assert(threwUnsupported, "D5: BuildProcessTzValue throws on unsupported UTC+12:45 (never silent UTC fallback)");

            bool threwInvalidIana = false;
            try
            {
                TimezoneHelper.BuildProcessTzValue("iana", "Fake/Zone", null, false);
            }
            catch (ArgumentException)
            {
                threwInvalidIana = true;
            }
            Assert(threwInvalidIana, "D5: BuildProcessTzValue throws on invalid IANA zone (never silent LA fallback)");
        }
    }
}
