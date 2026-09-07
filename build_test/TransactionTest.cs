using System;
using System.Collections.Generic;
using System.IO;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class TransactionTest
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

        private class SandboxEnvStorage : IEnvironmentStorage
        {
            public Dictionary<string, string> Storage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string GetVariable(string key)
            {
                string v;
                if (Storage.TryGetValue(key, out v)) return v;
                return null;
            }

            public void SetVariable(string key, string value)
            {
                if (value == null) Storage.Remove(key);
                else Storage[key] = value;
            }

            public void BroadcastChange() { }
        }

        private class SandboxLock : ITransactionLock
        {
            public bool TryAcquire(int timeoutMs, out bool acquiredViaAbandon)
            {
                acquiredViaAbandon = false;
                return true;
            }
            public void Release() { }
            public void Close() { }
            public void Dispose() { }
        }

        public static int Main()
        {
            Console.WriteLine("--- Testing UserEnvironmentTransaction (Isolated Sandbox) ---");

            string tempDir = Path.Combine(Path.GetTempPath(), "ChatGPT_TxTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string walPath = Path.Combine(tempDir, "env_transaction.json");

            SandboxEnvStorage envStorage = new SandboxEnvStorage();
            DefaultWalStorage walStorage = new DefaultWalStorage(walPath);

            UserEnvironmentTransaction.EnvironmentStorage = envStorage;
            UserEnvironmentTransaction.WalStorage = walStorage;
            UserEnvironmentTransaction.LockProvider = () => new SandboxLock();

            try
            {
                string testKey = "HTTP_PROXY";
                Dictionary<string, string> targetValues = new Dictionary<string, string>();
                targetValues[testKey] = "http://127.0.0.1:7890";

                // Test 1: Active transaction writes WAL
                using (UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targetValues))
                {
                    bool walExists = File.Exists(walPath);
                    string currentInjected = envStorage.GetVariable(testKey);

                    Assert(walExists, "WAL file created on disk before registry update");
                    Assert(currentInjected == "http://127.0.0.1:7890", "Target variable injected into environment");
                }

                // Test 2: Dispose cleans up WAL and restores variable
                bool walCleaned = !File.Exists(walPath);
                string restoredVal = envStorage.GetVariable(testKey);
                Assert(walCleaned, "WAL file safely removed after all variables restored");
                Assert(restoredVal == null, "Target variable cleanly restored to null");

                // Test 3: Compare-and-Restore conflict prevention
                using (UserEnvironmentTransaction tx = new UserEnvironmentTransaction(targetValues))
                {
                    // External program changes the variable to something else
                    envStorage.SetVariable(testKey, "http://127.0.0.1:9999");
                }

                // On dispose, because currentVal != "http://127.0.0.1:7890", Compare-and-Restore must NOT overwrite the external change!
                string preservedVal = envStorage.GetVariable(testKey);
                bool passCompare = (preservedVal == "http://127.0.0.1:9999");
                Assert(passCompare, "Compare-and-Restore preserved external modification without clobbering");

                return (failed == 0) ? 0 : 1;
            }
            finally
            {
                // Compliance: Do NOT batch or recursively delete test directories.
                // Output retained directory path for manual cleanup if desired.
                Console.WriteLine("[NOTICE] Test sandbox directory retained: " + tempDir);
            }
        }
    }
}
