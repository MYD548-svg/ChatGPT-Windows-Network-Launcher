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
            public int SetCalls = 0;
            public int BroadcastCalls = 0;
            public string FailOnKey = null;
            public bool FailRead = false;
            public int FailAfterSetCount = -1;

            public string GetVariable(string key)
            {
                if (FailRead) throw new UnauthorizedAccessException("模拟注册表读取拒绝: " + key);
                string val;
                if (Store.TryGetValue(key, out val)) return val;
                return null;
            }

            public void SetVariable(string key, string value)
            {
                if (FailAfterSetCount >= 0 && SetCalls >= FailAfterSetCount)
                {
                    throw new UnauthorizedAccessException("模拟注册表写入拒绝 (恢复阶段): " + key);
                }
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
            public bool FailRead = false;
            public bool FailExists = false;
            public int WriteCalls = 0;
            public int DeleteCalls = 0;

            public bool Exists()
            {
                if (FailExists) throw new IOException("模拟 WAL 探测失败");
                return Bytes != null && Bytes.Length > 0;
            }

            public byte[] ReadAllBytes()
            {
                if (FailRead) throw new IOException("模拟 WAL 读取失败");
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

        public static int Main()
        {
            Console.WriteLine("===============================================================");
            Console.WriteLine("  ChatGPT Launcher Round 3 Isolated Tests (Safe & Zero Impact) ");
            Console.WriteLine("===============================================================");
            Console.WriteLine();

            string testSandboxDir = Path.Combine(Path.GetTempPath(), "ChatGPTLauncher_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testSandboxDir);

            try
            {
                RunEnvironmentTransactionTests();
                RunReviewFailureTests();
                RunTargetHardeningTests();
                RunFinalAuditTests();
                RunProcessMatchingTests();
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
                // Compliance: Do NOT batch or recursively delete test directories.
                // Retain directory and report absolute path to user.
                Console.WriteLine("[NOTICE] Test sandbox directory retained: " + testSandboxDir);
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
                    targets["TZ"] = "val";
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
                targets["HTTP_PROXY"] = "http://127.0.0.1:8080";

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
                    TransactionId = "11111111-1111-1111-1111-111111111111",
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
                    targets["TZ"] = "val1";
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
                mockEnv.Store["HTTP_PROXY"] = "orig_A";
                mockEnv.Store["HTTPS_PROXY"] = "orig_B";
                mockEnv.FailOnKey = "HTTPS_PROXY"; // Fails when injecting HTTPS_PROXY

                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                bool threwException = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["HTTP_PROXY"] = "injected_A";
                    targets["HTTPS_PROXY"] = "injected_B";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (Exception)
                {
                    threwException = true;
                }

                Assert(threwException, "A5: Partial write failure throws exception");
                Assert(mockEnv.Store["HTTP_PROXY"] == "orig_A", "A5: Already set variable HTTP_PROXY was rolled back");
                Assert(!mockLock.IsHeld, "A5: Lock released after rollback");
            }

            // 6. Compare-and-Restore: External modification is NOT overwritten and conflict logged
            {
                MockEnvironmentStorage mockEnv = new MockEnvironmentStorage();
                mockEnv.Store["ALL_PROXY"] = "orig_val";

                MockWalStorage mockWal = new MockWalStorage();
                MockTransactionLock mockLock = new MockTransactionLock { CanAcquire = true };

                UserEnvironmentTransaction.EnvironmentStorage = mockEnv;
                UserEnvironmentTransaction.WalStorage = mockWal;
                UserEnvironmentTransaction.LockProvider = () => mockLock;

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["ALL_PROXY"] = "injected_val";

                using (UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targets))
                {
                    Assert(mockEnv.Store["ALL_PROXY"] == "injected_val", "A6: Variable injected");
                    // External modification occurs
                    mockEnv.Store["ALL_PROXY"] = "external_user_val";
                }

                // Upon dispose, Compare-and-Restore MUST NOT overwrite external modification!
                Assert(mockEnv.Store["ALL_PROXY"] == "external_user_val", "A6: Compare-and-Restore preserved external modification");
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
                    targets["NO_PROXY"] = "val1";
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
        // SECTION REVIEW: Review Failure Reproductions (Round 3 Baseline)
        // =========================================================================
        private static void RunReviewFailureTests()
        {
            Console.WriteLine("--- [Review] Testing Round 3 Review Failure Reproductions ---");

            // Review 1: Constructor read failure must release lock
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                Dictionary<string, string> values = new Dictionary<string, string>();
                values["TZ"] = "Asia/Tokyo";
                env.FailRead = true;

                bool threw = false;
                try
                {
                    using (new UserEnvironmentTransaction(values)) { }
                }
                catch (UnauthorizedAccessException)
                {
                    threw = true;
                }
                Assert(threw, "REVIEW 1: Constructor read failure throws UnauthorizedAccessException");
                Assert(!guard.IsHeld, "REVIEW 1: Constructor read failure must release lock");
                Assert(guard.CloseCalls > 0 || guard.ReleaseCalls > 0, "REVIEW 1: Lock handle was closed/released");
            }

            // Review 2: Dispose restoration failure must notify caller
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                env.FailRead = false;
                env.Store["TZ"] = "original";

                Dictionary<string, string> values = new Dictionary<string, string>();
                values["TZ"] = "Asia/Tokyo";
                UserEnvironmentTransaction tx = new UserEnvironmentTransaction(values);

                env.FailOnKey = "TZ"; // Fail when restoring
                bool notified = false;
                try
                {
                    tx.Dispose();
                }
                catch
                {
                    notified = true;
                }

                Assert(notified, "REVIEW 2: Dispose restoration failure must notify caller");
                Assert(wal.Exists(), "REVIEW 2: WAL retained when restoration fails");
                Assert(!guard.IsHeld, "REVIEW 2: Lock released even when restoration fails");
            }

            // Review 3: Reject unrelated environment keys in WAL (PATH)
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                wal.Bytes = UserEnvironmentTransaction.SerializeLog(new EnvTransactionLog
                {
                    Version = 1,
                    TransactionId = "22222222-2222-2222-2222-222222222222",
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Variables = new List<EnvVariableRecord>
                    {
                        new EnvVariableRecord { Key = "PATH", Existed = true, OriginalValue = @"C:\OldPath", InjectedValue = @"C:\InjectedPath" }
                    }
                });
                env.Store["PATH"] = @"C:\InjectedPath";

                RecoveryResult result = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!result.Success, "REVIEW 3: Reject unrelated environment keys in WAL (PATH)");
                Assert(env.Store["PATH"] == @"C:\InjectedPath", "REVIEW 3: PATH environment unmodified (zero writes)");
                Assert(env.SetCalls == 0, "REVIEW 3: Zero environment writes on rejected foreign key");
                Assert(wal.Exists(), "REVIEW 3: Foreign WAL preserved for manual inspection");
            }
        }

        // =========================================================================
        // SECTION HARDENING: Round 3 P1 Requirements (A-D)
        // =========================================================================
        private static void RunTargetHardeningTests()
        {
            Console.WriteLine("--- [Hardening] Testing Round 3 P1 Requirements (A-D) ---");

            // Hardening A1: Wal Exists/Read failure in constructor releases lock
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage { FailExists = true };
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                bool threw = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["TZ"] = "Europe/London";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (IOException)
                {
                    threw = true;
                }
                Assert(threw, "A-H1: Wal Exists failure throws IOException");
                Assert(!guard.IsHeld, "A-H1: Lock released on Wal Exists failure in constructor");
                Assert(env.SetCalls == 0, "A-H1: Zero environment writes");
            }

            // Hardening A2: Partial write failure with rollback applied
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["HTTP_PROXY"] = "http://127.0.0.1:8080";
                env.Store["HTTPS_PROXY"] = "http://127.0.0.1:8080";
                env.FailOnKey = "HTTPS_PROXY"; // Fail when injecting HTTPS_PROXY

                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                bool threw = false;
                try
                {
                    Dictionary<string, string> targets = new Dictionary<string, string>();
                    targets["HTTP_PROXY"] = "http://127.0.0.1:9090";
                    targets["HTTPS_PROXY"] = "http://127.0.0.1:9090";
                    using (new UserEnvironmentTransaction(targets)) { }
                }
                catch (UnauthorizedAccessException)
                {
                    threw = true;
                }
                Assert(threw, "A-H2: Partial write failure throws UnauthorizedAccessException");
                Assert(!guard.IsHeld, "A-H2: Lock released on partial write failure");
                Assert(env.Store["HTTP_PROXY"] == "http://127.0.0.1:8080", "A-H2: Successfully injected variable rolled back to original");
            }

            // Hardening B1: Explicit Restore() result differentiation on WAL delete failure
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["TZ"] = "UTC";
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["TZ"] = "America/New_York";
                UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targets);

                wal.FailDelete = true; // Fail WAL deletion
                TransactionRestoreResult res = tx.Restore();
                Assert(!res.Success, "B-H1: Restore reports failure when WAL delete fails");
                Assert(res.AllRestored, "B-H1: All variables were restored to environment");
                Assert(!res.WalCleaned, "B-H1: WalCleaned is false");
                Assert(res.Message.Contains("删除 WAL 日志失败"), "B-H1: Descriptive message contains WAL delete failure");

                // Calling Dispose after explicit restore should release lock without masking
                tx.Dispose();
                Assert(!guard.IsHeld, "B-H1: Lock released in Dispose");
            }

            // Hardening B2: Store Launch - COM activation succeeded but environment restore failed
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["TZ"] = "UTC";
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                TargetClientInfo storeClient = new TargetClientInfo
                {
                    IsFound = true,
                    Type = ClientInstallationType.StorePackage,
                    AppUserModelId = "OpenAI.Codex_test!App",
                    PackageFamilyName = "OpenAI.Codex_test",
                    ExecutablePath = @"C:\Test\ChatGPT.exe"
                };

                LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Shanghai" };

                // Mock activator succeeds with PID 8888
                LauncherService.PackagedAppActivator = appid => 8888;
                // Fail on restore phase (after 5 variables are injected during initialization)
                env.FailAfterSetCount = 5;

                LaunchResult launchRes = LauncherService.Launch(storeClient, settings);
                Assert(!launchRes.Succeeded, "B-H2: Launch does NOT report plain success when restore fails");
                Assert(launchRes.TargetPid == 8888, "B-H2: TargetPid accurately reports activated process PID");
                Assert(launchRes.ProcessStatus.Contains("8888"), "B-H2: ProcessStatus reports PID 8888");
                Assert(launchRes.ErrorMessage.Contains("8888") && launchRes.ErrorMessage.Contains("恢复"), "B-H2: ErrorMessage accurately reports both launch PID and restore error");

                // Restore activator
                LauncherService.PackagedAppActivator = PackagedAppLauncher.Activate;
                env.FailAfterSetCount = -1;
            }

            // Hardening B3: Store Launch - COM activation failed and environment restore succeeded
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["TZ"] = "UTC";
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                TargetClientInfo storeClient = new TargetClientInfo
                {
                    IsFound = true,
                    Type = ClientInstallationType.StorePackage,
                    AppUserModelId = "OpenAI.Codex_test!App",
                    ExecutablePath = @"C:\Test\ChatGPT.exe"
                };

                LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Shanghai" };
                LauncherService.PackagedAppActivator = appid => { throw new InvalidOperationException("模拟 COM 激活超时"); };

                LaunchResult launchRes = LauncherService.Launch(storeClient, settings);
                Assert(!launchRes.Succeeded, "B-H3: Launch reports failure on COM failure");
                Assert(launchRes.TargetPid == 0, "B-H3: TargetPid is 0");
                Assert(launchRes.ErrorMessage.Contains("模拟 COM 激活超时"), "B-H3: ErrorMessage contains COM error");
                Assert(env.Store["TZ"] == "UTC", "B-H3: Environment restored cleanly to original value");

                // Restore activator
                LauncherService.PackagedAppActivator = PackagedAppLauncher.Activate;
            }

            // Hardening B4: Store Launch - COM activation failed AND environment restore failed
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["TZ"] = "UTC";
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                TargetClientInfo storeClient = new TargetClientInfo
                {
                    IsFound = true,
                    Type = ClientInstallationType.StorePackage,
                    AppUserModelId = "OpenAI.Codex_test!App",
                    ExecutablePath = @"C:\Test\ChatGPT.exe"
                };

                LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Shanghai" };
                LauncherService.PackagedAppActivator = appid => { throw new InvalidOperationException("模拟 COM 激活崩溃"); };
                env.FailAfterSetCount = 5; // Also fail on restore phase (after 5 init injections)

                LaunchResult launchRes = LauncherService.Launch(storeClient, settings);
                Assert(!launchRes.Succeeded, "B-H4: Launch reports failure when both COM and restore fail");
                Assert(launchRes.TargetPid == 0, "B-H4: TargetPid is 0");
                Assert(launchRes.ErrorMessage.Contains("模拟 COM 激活崩溃") && launchRes.ErrorMessage.Contains("环境恢复异常"), "B-H4: ErrorMessage reports both COM and restore failures");
                Assert(launchRes.EnvironmentAdoptionStatus.Contains("环境恢复失败"), "B-H4: EnvironmentAdoptionStatus indicates restore failure");

                // Restore activator
                LauncherService.PackagedAppActivator = PackagedAppLauncher.Activate;
                env.FailAfterSetCount = -1;
            }

            // Hardening C1: Strict whitelist validation of target variables
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                // Non-whitelisted variable rejected before acquiring lock
                bool threwArg = false;
                try
                {
                    Dictionary<string, string> badTargets = new Dictionary<string, string>();
                    badTargets["JAVA_HOME"] = @"C:\Java";
                    using (new UserEnvironmentTransaction(badTargets)) { }
                }
                catch (ArgumentException)
                {
                    threwArg = true;
                }
                Assert(threwArg, "C-H1: Non-whitelisted variable JAVA_HOME rejected with ArgumentException");
                Assert(env.SetCalls == 0, "C-H1: Zero environment writes on rejected key");
                Assert(!guard.IsHeld, "C-H1: Lock never held on pre-validation error");

                // Whitelisted keys all accepted: TZ, HTTP_PROXY, HTTPS_PROXY, ALL_PROXY, NO_PROXY
                Dictionary<string, string> validTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                validTargets["TZ"] = "UTC";
                validTargets["HTTP_PROXY"] = "http://127.0.0.1:8080";
                validTargets["HTTPS_PROXY"] = "http://127.0.0.1:8080";
                validTargets["ALL_PROXY"] = "socks5://127.0.0.1:1080";
                validTargets["NO_PROXY"] = "localhost";

                using (new UserEnvironmentTransaction(validTargets))
                {
                    Assert(env.Store.ContainsKey("TZ") && env.Store.ContainsKey("ALL_PROXY"), "C-H1: All whitelisted variables accepted");
                }
            }

            // Hardening C2: Key validation edge cases
            {
                string errMsg;
                Assert(!UserEnvironmentTransaction.ValidateVariableKey("", out errMsg), "C-H2: Empty key rejected");
                Assert(!UserEnvironmentTransaction.ValidateVariableKey("SystemRoot", out errMsg), "C-H2: SystemRoot rejected");
                Assert(UserEnvironmentTransaction.ValidateVariableKey("TZ", out errMsg), "C-H2: TZ accepted");
                Assert(UserEnvironmentTransaction.ValidateVariableKey("NO_PROXY", out errMsg), "C-H2: NO_PROXY accepted");
            }

            // Hardening C3: Legitimate null injected value (clearing variable)
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                env.Store["HTTP_PROXY"] = "http://old-proxy:8080";
                MockWalStorage wal = new MockWalStorage();
                MockTransactionLock guard = new MockTransactionLock();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => guard;

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["HTTP_PROXY"] = null; // Direct mode: clear proxy

                using (new UserEnvironmentTransaction(targets))
                {
                    Assert(!env.Store.ContainsKey("HTTP_PROXY"), "C-H3: Legitimate null value clears environment variable");
                }
                Assert(env.Store["HTTP_PROXY"] == "http://old-proxy:8080", "C-H3: Cleared variable restored on dispose");
            }

            // Hardening D1: Global lock failure has NO Local fallback
            {
                List<string> mutexNamesRequested = new List<string>();
                Func<string, Mutex> origFactory = DefaultTransactionLock.MutexFactory;

                try
                {
                    DefaultTransactionLock.MutexFactory = name =>
                    {
                        mutexNamesRequested.Add(name);
                        throw new UnauthorizedAccessException("Simulated Global mutex denial");
                    };

                    bool threwAuth = false;
                    try
                    {
                        new DefaultTransactionLock();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        threwAuth = true;
                    }

                    Assert(threwAuth, "D-H1: Global mutex failure throws UnauthorizedAccessException");
                    Assert(mutexNamesRequested.Count == 1, "D-H1: Exactly 1 mutex requested (no retry or secondary fallback)");
                    Assert(mutexNamesRequested[0].StartsWith(@"Global\"), "D-H1: Mutex requested was strictly Global");
                    Assert(!mutexNamesRequested[0].StartsWith(@"Local\"), "D-H1: Strictly NO Local mutex fallback");
                }
                finally
                {
                    DefaultTransactionLock.MutexFactory = origFactory;
                }
            }

            // Hardening B-Proc: Non-empty process list + null force-kill prompt callback strictly rejected
            {
                Process currentProc = Process.GetCurrentProcess();
                string nonExitedReason;
                bool nonExitedResult = LauncherService.CloseExistingInstances(
                    new List<Process> { currentProc },
                    false,
                    null, // promptForceKill is null!
                    out nonExitedReason,
                    gracefulTimeoutMs: 50); // Fast timeout so test completes in 50ms without stalling

                Assert(!nonExitedResult, "B-Proc: Non-empty process list with null prompt returns false");
                Assert(nonExitedReason.Contains("未提供强制关闭授权确认回调"), "B-Proc: Explicit rejection reason when force-kill prompt is null");
            }
        }

        // =========================================================================
        // SECTION AUDIT: Final Targeted Verifications (Task 1 & Task 2)
        // =========================================================================
        private static void RunFinalAuditTests()
        {
            Console.WriteLine("--- [Audit] Testing Final Targeted Fixes (WAL Integrity & Conflict Propagation) ---");

            // -----------------------------------------------------------------
            // TASK 1: Strict WAL Recovery Validation & Incomplete Log Rejection
            // -----------------------------------------------------------------

            // 1. Original Audit Repro: missing existed/timestamp/invalid transaction_id
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                env.Store["TZ"] = "temporary";
                string brokenJson = "{\"version\":1,\"transaction_id\":\"broken\",\"variables\":[{\"key\":\"TZ\",\"original_value\":\"original\",\"injected_value\":\"temporary\"}]}";
                byte[] brokenBytes = Encoding.UTF8.GetBytes(brokenJson);
                wal.Bytes = (byte[])brokenBytes.Clone();

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();

                Assert(!rec.Success, "AUDIT 1.1: Missing existed/timestamp/invalid GUID must be rejected", "success=" + rec.Success);
                Assert(env.SetCalls == 0, "AUDIT 1.1: Zero environment writes on rejected log", "writes=" + env.SetCalls);
                Assert(env.Store["TZ"] == "temporary", "AUDIT 1.1: Environment variable preserved unchanged", "TZ=" + env.Store["TZ"]);
                Assert(wal.Exists() && wal.Bytes.Length == brokenBytes.Length, "AUDIT 1.1: Malformed WAL is retained and bytes untouched");
            }

            // 1.2 Missing existed only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();
                env.Store["TZ"] = "temporary";

                string json = "{\"version\":1,\"transaction_id\":\"33333333-3333-3333-3333-333333333333\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"original_value\":\"original\",\"injected_value\":\"temporary\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.2: Missing existed only must be rejected with 0 writes");
            }

            // 1.3 Missing injected_value only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json = "{\"version\":1,\"transaction_id\":\"33333333-3333-3333-3333-333333333333\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.3: Missing injected_value only must be rejected with 0 writes");
            }

            // 1.4 Missing original_value only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json = "{\"version\":1,\"transaction_id\":\"33333333-3333-3333-3333-333333333333\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":false,\"injected_value\":null,\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.4: Missing original_value only must be rejected with 0 writes");
            }

            // 1.5 Missing timestamp only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json = "{\"version\":1,\"transaction_id\":\"33333333-3333-3333-3333-333333333333\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.5: Missing timestamp only must be rejected with 0 writes");
            }

            // 1.6 Missing restored only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json = "{\"version\":1,\"transaction_id\":\"33333333-3333-3333-3333-333333333333\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\"}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.6: Missing restored only must be rejected with 0 writes");
            }

            // 1.7 Missing transaction_id only
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json = "{\"version\":1,\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success && env.SetCalls == 0, "AUDIT 1.7: Missing transaction_id only must be rejected with 0 writes");
            }

            // 1.8 Full log with existed=false and legal null values cleanly restores (removes variable)
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();
                env.Store["TZ"] = "temporary";

                string json = "{\"version\":1,\"transaction_id\":\"44444444-4444-4444-4444-444444444444\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":false,\"original_value\":null,\"injected_value\":\"temporary\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(rec.Success, "AUDIT 1.8: existed=false and original_value=null recovered successfully");
                Assert(!env.Store.ContainsKey("TZ"), "AUDIT 1.8: Variable removed from environment when existed=false");
                Assert(!wal.Exists(), "AUDIT 1.8: WAL safely deleted after successful recovery");
            }

            // 1.9 Full log with existed=true and legal string value restores original value
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();
                env.Store["TZ"] = "temporary";

                string json = "{\"version\":1,\"transaction_id\":\"55555555-5555-5555-5555-555555555555\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"Asia/Tokyo\",\"injected_value\":\"temporary\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(rec.Success, "AUDIT 1.9: existed=true and original_value string recovered successfully");
                Assert(env.Store["TZ"] == "Asia/Tokyo", "AUDIT 1.9: Variable restored to original value");
                Assert(!wal.Exists(), "AUDIT 1.9: WAL safely deleted after full recovery");
            }

            // 1.10 Contradictory states: existed=true with original_value=null & existed=false with original_value!=null
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                string json1 = "{\"version\":1,\"transaction_id\":\"66666666-6666-6666-6666-666666666666\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":null,\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json1);
                RecoveryResult rec1 = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec1.Success && env.SetCalls == 0, "AUDIT 1.10: Contradictory existed=true & original_value=null rejected");

                string json2 = "{\"version\":1,\"transaction_id\":\"66666666-6666-6666-6666-666666666666\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":false,\"original_value\":\"non-null\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(json2);
                RecoveryResult rec2 = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec2.Success && env.SetCalls == 0, "AUDIT 1.10: Contradictory existed=false & original_value!=null rejected");
            }

            // 1.11 Invalid formats: transaction_id, timestamp, duplicates, whitespace key
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                // Non-guid transaction_id
                string badGuid = "{\"version\":1,\"transaction_id\":\"not-a-guid\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(badGuid);
                Assert(!UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions().Success, "AUDIT 1.11: Non-guid transaction_id rejected");

                // Bad timestamp
                string badTime = "{\"version\":1,\"transaction_id\":\"77777777-7777-7777-7777-777777777777\",\"timestamp\":\"invalid-time\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(badTime);
                Assert(!UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions().Success, "AUDIT 1.11: Invalid timestamp rejected");

                // Whitespace key " TZ "
                string spaceKey = "{\"version\":1,\"transaction_id\":\"77777777-7777-7777-7777-777777777777\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\" TZ \",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(spaceKey);
                Assert(!UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions().Success, "AUDIT 1.11: Whitespace-padded key rejected");

                // Duplicate keys
                string dupKeys = "{\"version\":1,\"transaction_id\":\"77777777-7777-7777-7777-777777777777\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"orig\",\"injected_value\":\"inj\",\"restored\":false},{\"key\":\"tz\",\"existed\":true,\"original_value\":\"orig2\",\"injected_value\":\"inj2\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(dupKeys);
                Assert(!UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions().Success, "AUDIT 1.11: Duplicate keys rejected");
            }

            // 1.12 First record legal, second record illegal: ZERO writes across the board
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();
                env.Store["TZ"] = "temporary";
                env.Store["HTTP_PROXY"] = "temporary_proxy";

                string mixedJson = "{\"version\":1,\"transaction_id\":\"88888888-8888-8888-8888-888888888888\",\"timestamp\":\"2026-09-06T00:00:00Z\",\"variables\":[{\"key\":\"TZ\",\"existed\":true,\"original_value\":\"Asia/Tokyo\",\"injected_value\":\"temporary\",\"restored\":false},{\"key\":\"PATH\",\"existed\":true,\"original_value\":\"C:\\\\old\",\"injected_value\":\"C:\\\\new\",\"restored\":false}]}";
                wal.Bytes = Encoding.UTF8.GetBytes(mixedJson);

                RecoveryResult rec = UserEnvironmentTransaction.CheckAndRecoverDanglingTransactions();
                Assert(!rec.Success, "AUDIT 1.12: Mixed log with second illegal record rejected");
                Assert(env.SetCalls == 0, "AUDIT 1.12: Zero writes on entire log when any record is invalid");
                Assert(env.Store["TZ"] == "temporary", "AUDIT 1.12: First record TZ not partially modified");
                Assert(wal.Exists(), "AUDIT 1.12: Mixed WAL retained on disk");
            }

            // 1.13 Production writer generated log is deserializable and recovered
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();
                env.Store["TZ"] = "initial_tz";

                Dictionary<string, string> targets = new Dictionary<string, string>();
                targets["TZ"] = "target_tz";

                using (var tx = new UserEnvironmentTransaction(targets))
                {
                    Assert(wal.Exists(), "AUDIT 1.13: Production writer created WAL");
                }
                Assert(!wal.Exists(), "AUDIT 1.13: Clean transaction deleted WAL upon dispose");
            }

            // -----------------------------------------------------------------
            // TASK 2: External Modification Conflict Visibility
            // -----------------------------------------------------------------

            // 2.1 Activation succeeded with external modification conflict
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                var oldActivator = LauncherService.PackagedAppActivator;
                try
                {
                    LauncherService.PackagedAppActivator = id =>
                    {
                        env.Store["TZ"] = "external_user_value";
                        return 777;
                    };

                    TargetClientInfo storeClient = new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StorePackage,
                        AppUserModelId = "OpenAI.Codex_test!App"
                    };
                    LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Tokyo" };

                    LaunchResult result = LauncherService.Launch(storeClient, settings);

                    Assert(result.Succeeded, "AUDIT 2.1: Succeeded is true when app activated with conflict");
                    Assert(result.TargetPid == 777, "AUDIT 2.1: TargetPid accurately reports activated PID 777");
                    Assert(result.WarningLevel == LaunchWarningLevel.Warning, "AUDIT 2.1: WarningLevel is Warning");
                    Assert(result.HasConflicts, "AUDIT 2.1: HasConflicts is true");
                    Assert(result.ConflictCount == 1, "AUDIT 2.1: ConflictCount is 1");
                    Assert(env.Store["TZ"] == "external_user_value", "AUDIT 2.1: External modification value preserved");

                    string adoptionStatus = result.EnvironmentAdoptionStatus ?? "";
                    string summary = result.Summary ?? "";
                    Assert(adoptionStatus.Contains("冲突") || summary.Contains("外部修改"), "AUDIT 2.1: Conflict visible in adoption status or summary");
                    Assert(!summary.Contains("环境已恢复") || summary.Contains("外部修改"), "AUDIT 2.1: UI summary does not falsely claim full clean recovery without mentioning conflict");
                }
                finally
                {
                    LauncherService.PackagedAppActivator = oldActivator;
                }
            }

            // 2.2 Activation succeeded with NO conflicts (normal clean success)
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                var oldActivator = LauncherService.PackagedAppActivator;
                try
                {
                    LauncherService.PackagedAppActivator = id => 888;

                    TargetClientInfo storeClient = new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StorePackage,
                        AppUserModelId = "OpenAI.Codex_test!App"
                    };
                    LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Tokyo" };

                    LaunchResult result = LauncherService.Launch(storeClient, settings);

                    Assert(result.Succeeded, "AUDIT 2.2: Succeeded is true");
                    Assert(result.TargetPid == 888, "AUDIT 2.2: TargetPid is 888");
                    Assert(result.WarningLevel == LaunchWarningLevel.None, "AUDIT 2.2: WarningLevel is None for clean launch");
                    Assert(!result.HasConflicts, "AUDIT 2.2: HasConflicts is false");
                    Assert(result.ConflictCount == 0, "AUDIT 2.2: ConflictCount is 0");
                    Assert(result.ErrorMessage == null, "AUDIT 2.2: ErrorMessage is null");
                }
                finally
                {
                    LauncherService.PackagedAppActivator = oldActivator;
                }
            }

            // 2.3 Activation succeeded but restore write failed (must NOT be treated as mere conflict warning)
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                var oldActivator = LauncherService.PackagedAppActivator;
                try
                {
                    LauncherService.PackagedAppActivator = id =>
                    {
                        env.FailAfterSetCount = 0; // Trigger write failure during restore
                        return 999;
                    };

                    TargetClientInfo storeClient = new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StorePackage,
                        AppUserModelId = "OpenAI.Codex_test!App"
                    };
                    LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Tokyo" };

                    LaunchResult result = LauncherService.Launch(storeClient, settings);

                    Assert(!result.Succeeded, "AUDIT 2.3: Launch Succeeded is false when restore fails");
                    Assert(result.TargetPid == 999, "AUDIT 2.3: TargetPid reports PID 999");
                    Assert(result.WarningLevel == LaunchWarningLevel.Error, "AUDIT 2.3: WarningLevel is Error (not downgraded)");
                    Assert((result.ErrorMessage ?? "").Length > 0, "AUDIT 2.3: ErrorMessage contains failure details");
                }
                finally
                {
                    LauncherService.PackagedAppActivator = oldActivator;
                }
            }

            // 2.4 COM failed AND restore had conflict
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                var oldActivator = LauncherService.PackagedAppActivator;
                try
                {
                    LauncherService.PackagedAppActivator = id =>
                    {
                        env.Store["TZ"] = "external_mod";
                        throw new InvalidOperationException("模拟 COM 激活超时");
                    };

                    TargetClientInfo storeClient = new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StorePackage,
                        AppUserModelId = "OpenAI.Codex_test!App"
                    };
                    LauncherSettings settings = new LauncherSettings { Mode = "iana", IanaName = "Asia/Tokyo" };

                    LaunchResult result = LauncherService.Launch(storeClient, settings);

                    Assert(!result.Succeeded, "AUDIT 2.4: Succeeded is false on COM failure");
                    Assert(result.TargetPid == 0, "AUDIT 2.4: TargetPid is 0");
                    Assert(result.HasConflicts, "AUDIT 2.4: HasConflicts is true");
                    Assert(result.ConflictCount == 1, "AUDIT 2.4: ConflictCount is 1");
                    Assert(result.ErrorMessage.Contains("模拟 COM 激活超时"), "AUDIT 2.4: ErrorMessage contains COM error");
                    Assert(result.ErrorMessage.Contains("冲突"), "AUDIT 2.4: ErrorMessage contains conflict notice");
                    Assert(env.Store["TZ"] == "external_mod", "AUDIT 2.4: External modification preserved");
                }
                finally
                {
                    LauncherService.PackagedAppActivator = oldActivator;
                }
            }

            // 2.5 Warning / Error messages do NOT leak proxy credentials
            {
                MockEnvironmentStorage env = new MockEnvironmentStorage();
                MockWalStorage wal = new MockWalStorage();
                UserEnvironmentTransaction.EnvironmentStorage = env;
                UserEnvironmentTransaction.WalStorage = wal;
                UserEnvironmentTransaction.LockProvider = () => new MockTransactionLock();

                var oldActivator = LauncherService.PackagedAppActivator;
                try
                {
                    LauncherService.PackagedAppActivator = id =>
                    {
                        env.Store["HTTP_PROXY"] = "http://sensitive_user:secret_password@127.0.0.1:8080";
                        return 1234;
                    };

                    TargetClientInfo storeClient = new TargetClientInfo
                    {
                        IsFound = true,
                        Type = ClientInstallationType.StorePackage,
                        AppUserModelId = "OpenAI.Codex_test!App"
                    };
                    LauncherSettings settings = new LauncherSettings
                    {
                        NetworkMode = "vpn",
                        ProxyHost = "127.0.0.1",
                        ProxyPort = 7897
                    };

                    LaunchResult result = LauncherService.Launch(storeClient, settings);

                    string combined = (result.Summary ?? "") + " " + (result.EnvironmentAdoptionStatus ?? "") + " " + (result.WarningMessage ?? "") + " " + (result.ErrorMessage ?? "");
                    Assert(!combined.Contains("secret_password"), "AUDIT 2.5: Messages strictly do NOT contain proxy credentials");
                }
                finally
                {
                    LauncherService.PackagedAppActivator = oldActivator;
                }
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
