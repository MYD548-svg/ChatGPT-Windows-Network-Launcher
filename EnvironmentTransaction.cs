using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ChatGPTAntiBanLauncher
{
    [DataContract]
    public sealed class EnvVariableRecord
    {
        [DataMember(Name = "key", IsRequired = true)] public string Key { get; set; }
        [DataMember(Name = "existed", IsRequired = true)] public bool Existed { get; set; }
        [DataMember(Name = "original_value", IsRequired = true, EmitDefaultValue = true)] public string OriginalValue { get; set; }
        [DataMember(Name = "injected_value", IsRequired = true, EmitDefaultValue = true)] public string InjectedValue { get; set; }
        [DataMember(Name = "restored", IsRequired = true, EmitDefaultValue = true)] public bool Restored { get; set; }
        [DataMember(Name = "conflict", IsRequired = false)] public bool Conflict { get; set; }
        [DataMember(Name = "conflict_detail", IsRequired = false)] public string ConflictDetail { get; set; }
    }

    [DataContract]
    public sealed class EnvTransactionLog
    {
        [DataMember(Name = "version", IsRequired = true)] public int Version { get; set; }
        [DataMember(Name = "transaction_id", IsRequired = true)] public string TransactionId { get; set; }
        [DataMember(Name = "timestamp", IsRequired = true)] public string Timestamp { get; set; }
        [DataMember(Name = "variables", IsRequired = true)] public List<EnvVariableRecord> Variables { get; set; }

        public EnvTransactionLog()
        {
            Version = 1;
            Variables = new List<EnvVariableRecord>();
        }
    }

    public sealed class RecoveryResult
    {
        public bool Success { get; set; }
        public bool HasDangling { get; set; }
        public int RecoveredCount { get; set; }
        public int ConflictCount { get; set; }
        public string Message { get; set; }
    }

    public sealed class TransactionRestoreResult
    {
        public bool Success { get; set; }
        public bool AllRestored { get; set; }
        public bool HasConflicts { get { return ConflictCount > 0; } }
        public bool AllOriginalRestored { get { return Success && FailureCount == 0 && ConflictCount == 0; } }
        public int RestoredCount { get; set; }
        public int ConflictCount { get; set; }
        public int FailureCount { get; set; }
        public string Message { get; set; }
        public bool WalCleaned { get; set; }
    }

    public interface IEnvironmentStorage
    {
        string GetVariable(string key);
        void SetVariable(string key, string value);
        void BroadcastChange();
    }

    public interface IWalStorage
    {
        bool Exists();
        byte[] ReadAllBytes();
        void WriteAtomic(byte[] bytes);
        void Delete();
    }

    public interface ITransactionLock : IDisposable
    {
        bool TryAcquire(int timeoutMs, out bool acquiredViaAbandon);
        void Release();
        void Close();
    }

    internal sealed class DefaultEnvironmentStorage : IEnvironmentStorage
    {
        public string GetVariable(string key)
        {
            return Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
        }

        public void SetVariable(string key, string value)
        {
            Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.User);
        }

        public void BroadcastChange()
        {
            EnvironmentChangeBroadcaster.Broadcast();
        }
    }

    internal sealed class DefaultWalStorage : IWalStorage
    {
        private readonly string walPath;

        public DefaultWalStorage(string path = null)
        {
            walPath = !string.IsNullOrEmpty(path) ? path : Path.Combine(SettingsStore.SettingsDir, "env_transaction.json");
        }

        public bool Exists()
        {
            return File.Exists(walPath);
        }

        public byte[] ReadAllBytes()
        {
            using (FileStream fs = new FileStream(walPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] data = new byte[fs.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int r = fs.Read(data, read, data.Length - read);
                    if (r <= 0) break;
                    read += r;
                }
                return data;
            }
        }

        public void WriteAtomic(byte[] bytes)
        {
            string dir = Path.GetDirectoryName(walPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = walPath + ".tmp." + Guid.NewGuid().ToString("N");
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true); // Force disk write-through
            }

            if (File.Exists(walPath))
            {
                File.Replace(tmp, walPath, null);
            }
            else
            {
                File.Move(tmp, walPath);
            }
        }

        public void Delete()
        {
            if (File.Exists(walPath))
            {
                File.Delete(walPath);
            }
        }
    }

    internal sealed class DefaultTransactionLock : ITransactionLock
    {
        internal static Func<string, Mutex> MutexFactory = name => new Mutex(false, name);

        private Mutex mutex;
        private bool hasLock;

        public string MutexName { get; private set; }

        public DefaultTransactionLock()
        {
            string sid = null;
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                if (id != null && id.User != null)
                {
                    sid = id.User.Value;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(sid))
            {
                throw new UnauthorizedAccessException("无法获取当前用户安全标识符 (SID)，已阻止创建环境事务锁");
            }

            MutexName = string.Format(@"Global\ChatGPTLauncher_EnvLock_{0}", sid);
            // Strictly require Global cross-session mutex. No silent Local fallback!
            mutex = MutexFactory(MutexName);
        }

        public bool TryAcquire(int timeoutMs, out bool acquiredViaAbandon)
        {
            acquiredViaAbandon = false;
            if (mutex == null) return false;

            try
            {
                hasLock = mutex.WaitOne(timeoutMs);
                return hasLock;
            }
            catch (AbandonedMutexException)
            {
                hasLock = true;
                acquiredViaAbandon = true;
                return true;
            }
        }

        public void Release()
        {
            if (hasLock && mutex != null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch { }
                hasLock = false;
            }
        }

        public void Close()
        {
            Release();
            if (mutex != null)
            {
                try
                {
                    mutex.Close();
                }
                catch { }
                mutex = null;
            }
        }

        public void Dispose()
        {
            Close();
        }
    }

    public sealed class UserEnvironmentTransaction : IDisposable
    {
        // Whitelist of strictly managed environment variables
        public static readonly HashSet<string> AllowedEnvironmentVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TZ",
            "HTTP_PROXY",
            "HTTPS_PROXY",
            "ALL_PROXY",
            "NO_PROXY"
        };

        public static bool ValidateVariableKey(string key, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(key))
            {
                errorMessage = "环境变量键名不可为空";
                return false;
            }

            if (key.Trim() != key)
            {
                errorMessage = string.Format("环境变量键名 '{0}' 包含前后空白字符，已拒绝处理", key);
                return false;
            }

            if (!AllowedEnvironmentVariables.Contains(key))
            {
                errorMessage = string.Format("环境变量 '{0}' 不在受信任管理允许列表中 (仅允许: TZ, HTTP_PROXY, HTTPS_PROXY, ALL_PROXY, NO_PROXY)", key);
                return false;
            }

            return true;
        }

        public static bool ValidateLogStructure(EnvTransactionLog log, out string errorMessage)
        {
            errorMessage = null;
            if (log == null)
            {
                errorMessage = "事务日志对象为空";
                return false;
            }

            if (log.Version != 1)
            {
                errorMessage = string.Format("事务日志版本不受支持: {0} (当前仅支持版本 1)", log.Version);
                return false;
            }

            if (string.IsNullOrEmpty(log.TransactionId))
            {
                errorMessage = "事务日志缺少必要字段 'transaction_id'";
                return false;
            }

            Guid txGuid;
            if (!Guid.TryParse(log.TransactionId, out txGuid) || txGuid == Guid.Empty)
            {
                errorMessage = "事务日志 transaction_id 格式非法 (须为有效非空 GUID)";
                return false;
            }

            if (string.IsNullOrEmpty(log.Timestamp))
            {
                errorMessage = "事务日志缺少必要字段 'timestamp'";
                return false;
            }

            DateTime parsedTime;
            if (!DateTime.TryParse(log.Timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out parsedTime))
            {
                errorMessage = "事务日志 timestamp 格式非法 (无法解析为有效时间戳)";
                return false;
            }

            if (log.Variables == null || log.Variables.Count == 0)
            {
                errorMessage = "事务日志缺少必要字段 'variables' 或变量列表为空";
                return false;
            }

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EnvVariableRecord rec in log.Variables)
            {
                if (rec == null)
                {
                    errorMessage = "事务日志包含空记录对象，已拒绝恢复";
                    return false;
                }

                string keyErr;
                if (!ValidateVariableKey(rec.Key, out keyErr))
                {
                    errorMessage = "事务日志包含非法环境变量键: " + keyErr;
                    return false;
                }

                if (!seenKeys.Add(rec.Key))
                {
                    errorMessage = "事务日志包含重复的环境变量键: " + rec.Key;
                    return false;
                }

                // Consistency check: existed vs original_value
                if (rec.Existed && rec.OriginalValue == null)
                {
                    errorMessage = string.Format("事务日志变量 '{0}' 状态矛盾: existed=true 但 original_value=null", rec.Key);
                    return false;
                }

                if (!rec.Existed && rec.OriginalValue != null)
                {
                    errorMessage = string.Format("事务日志变量 '{0}' 状态矛盾: existed=false 但 original_value 不为 null", rec.Key);
                    return false;
                }
            }

            return true;
        }

        internal static IEnvironmentStorage EnvironmentStorage = new DefaultEnvironmentStorage();
        internal static IWalStorage WalStorage = new DefaultWalStorage();
        internal static Func<ITransactionLock> LockProvider = () => new DefaultTransactionLock();

        private ITransactionLock txLock;
        private bool hasLock = false;
        private EnvTransactionLog currentLog = null;
        private bool isDisposed = false;
        private bool isRestored = false;
        private TransactionRestoreResult lastRestoreResult = null;

        public string TransactionId { get; private set; }

        public UserEnvironmentTransaction(IDictionary<string, string> targetValues)
        {
            TransactionId = Guid.NewGuid().ToString("N");

            // Pre-validate target keys against strict whitelist before acquiring lock
            if (targetValues != null)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, string> kvp in targetValues)
                {
                    string keyErr;
                    if (!ValidateVariableKey(kvp.Key, out keyErr))
                    {
                        throw new ArgumentException("目标环境变量不受支持: " + keyErr, kvp.Key);
                    }
                    if (!seen.Add(kvp.Key))
                    {
                        throw new ArgumentException("目标环境变量包含重复键: " + kvp.Key, kvp.Key);
                    }
                }
            }

            // 1. Acquire mutex lock (Global cross-session SID lock)
            txLock = LockProvider();
            bool acquiredViaAbandon = false;
            try
            {
                hasLock = txLock.TryAcquire(3000, out acquiredViaAbandon);
            }
            catch (Exception ex)
            {
                ReleaseLock();
                throw new UnauthorizedAccessException("获取事务互斥锁异常: " + ex.Message, ex);
            }

            if (!hasLock)
            {
                ReleaseLock();
                throw new TimeoutException("获取跨会话环境事务锁超时 (3000ms)，当前可能存在其他正在运行的事务，操作已中止。");
            }

            // Unified try-catch for entire post-lock initialization phase
            List<string> successfullySet = new List<string>();
            try
            {
                // 2. Perform crash recovery under lock before starting new transaction
                RecoveryResult recRes = RecoverDanglingTransactionsLocked(EnvironmentStorage, WalStorage);
                if (!recRes.Success)
                {
                    throw new InvalidOperationException("存在未解决或已损坏的事务日志，已阻止启动新事务: " + recRes.Message);
                }

                if (targetValues == null || targetValues.Count == 0) return;

                currentLog = new EnvTransactionLog
                {
                    Version = 1,
                    TransactionId = TransactionId,
                    Timestamp = DateTime.UtcNow.ToString("o")
                };

                // 3. Record original state
                foreach (KeyValuePair<string, string> kvp in targetValues)
                {
                    string key = kvp.Key;
                    string currentVal = EnvironmentStorage.GetVariable(key);
                    bool exists = (currentVal != null);

                    currentLog.Variables.Add(new EnvVariableRecord
                    {
                        Key = key,
                        Existed = exists,
                        OriginalValue = currentVal,
                        InjectedValue = kvp.Value,
                        Restored = false,
                        Conflict = false
                    });
                }

                // 4. Save WAL to disk BEFORE modifying environment
                byte[] walBytes;
                try
                {
                    walBytes = SerializeLog(currentLog);
                    WalStorage.WriteAtomic(walBytes);
                }
                catch (Exception ex)
                {
                    throw new IOException("保存恢复日志 (WAL) 失败，环境写入已中止 (写入次数为 0): " + ex.Message, ex);
                }

                // 5. Apply changes with rollback on partial failure
                foreach (EnvVariableRecord rec in currentLog.Variables)
                {
                    EnvironmentStorage.SetVariable(rec.Key, rec.InjectedValue);
                    successfullySet.Add(rec.Key);
                }

                EnvironmentStorage.BroadcastChange();
            }
            catch (Exception)
            {
                // Rollback any successfully applied variable using Compare-and-Restore
                if (successfullySet.Count > 0)
                {
                    bool rollbackAllSucceeded = true;
                    foreach (string setKey in successfullySet)
                    {
                        EnvVariableRecord rec = currentLog != null && currentLog.Variables != null
                            ? currentLog.Variables.Find(r => string.Equals(r.Key, setKey, StringComparison.OrdinalIgnoreCase))
                            : null;
                        if (rec != null)
                        {
                            try
                            {
                                string cur = EnvironmentStorage.GetVariable(rec.Key);
                                if (string.Equals(cur, rec.InjectedValue, StringComparison.Ordinal))
                                {
                                    EnvironmentStorage.SetVariable(rec.Key, rec.Existed ? rec.OriginalValue : null);
                                    rec.Restored = true;
                                }
                                else
                                {
                                    rec.Conflict = true;
                                    rec.Restored = true;
                                    rec.ConflictDetail = "部分写入失败回滚时检测到外部修改";
                                }
                            }
                            catch
                            {
                                rollbackAllSucceeded = false;
                                rec.Restored = false;
                            }
                        }
                    }

                    try { EnvironmentStorage.BroadcastChange(); } catch { }

                    if (rollbackAllSucceeded)
                    {
                        try { WalStorage.Delete(); } catch { }
                    }
                    else
                    {
                        try
                        {
                            byte[] updated = SerializeLog(currentLog);
                            WalStorage.WriteAtomic(updated);
                        }
                        catch { }
                    }
                }

                // Always release lock on any constructor failure
                ReleaseLock();
                throw;
            }
        }

        public TransactionRestoreResult Restore()
        {
            if (isRestored) return lastRestoreResult;
            isRestored = true;

            TransactionRestoreResult res = new TransactionRestoreResult();
            if (currentLog == null || currentLog.Variables == null || currentLog.Variables.Count == 0)
            {
                res.Success = true;
                res.AllRestored = true;
                res.Message = "无待恢复环境变量";
                lastRestoreResult = res;
                return res;
            }

            bool allResolved = true;
            int restored = 0;
            int conflicts = 0;
            int failures = 0;

            foreach (EnvVariableRecord rec in currentLog.Variables)
            {
                try
                {
                    // Compare-and-Restore
                    string currentVal = EnvironmentStorage.GetVariable(rec.Key);
                    if (string.Equals(currentVal, rec.InjectedValue, StringComparison.Ordinal))
                    {
                        EnvironmentStorage.SetVariable(rec.Key, rec.Existed ? rec.OriginalValue : null);
                        rec.Restored = true;
                        rec.Conflict = false;
                        restored++;
                    }
                    else
                    {
                        // External modification conflict
                        rec.Conflict = true;
                        rec.Restored = true; // Resolved per conflict policy: do not overwrite
                        rec.ConflictDetail = string.Format("外部修改冲突: 变量 '{0}' 值已被外部修改，为避免破坏外部配置已保留当前值", rec.Key);
                        conflicts++;
                    }
                }
                catch (Exception ex)
                {
                    rec.Restored = false;
                    allResolved = false;
                    failures++;
                    rec.ConflictDetail = "恢复写入失败: " + ex.Message;
                }
            }

            try { EnvironmentStorage.BroadcastChange(); } catch { }

            res.RestoredCount = restored;
            res.ConflictCount = conflicts;
            res.FailureCount = failures;
            res.AllRestored = allResolved && (conflicts == 0);

            if (allResolved)
            {
                try
                {
                    WalStorage.Delete();
                    res.WalCleaned = true;
                    res.Success = true;
                    if (conflicts > 0)
                    {
                        res.Message = string.Format("环境已安全处理 (已恢复: {0}, 外部修改冲突保留: {1})", restored, conflicts);
                    }
                    else
                    {
                        res.Message = string.Format("环境已安全完全恢复 (已恢复: {0})", restored);
                    }
                }
                catch (Exception ex)
                {
                    res.WalCleaned = false;
                    res.Success = false;
                    res.Message = "环境变量已处理，但删除 WAL 日志失败: " + ex.Message;
                }
            }
            else
            {
                try
                {
                    byte[] updated = SerializeLog(currentLog);
                    WalStorage.WriteAtomic(updated);
                }
                catch { }

                res.Success = false;
                res.WalCleaned = false;
                res.Message = string.Format("部分环境变量恢复写入失败 ({0} 项失败，{1} 项已恢复)，已保留 WAL 记录", failures, restored);
            }

            lastRestoreResult = res;
            return res;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            TransactionRestoreResult res = null;
            Exception restoreException = null;

            try
            {
                if (!isRestored)
                {
                    res = Restore();
                    if (!res.Success)
                    {
                        restoreException = new InvalidOperationException("环境事务释放时恢复未完全成功: " + res.Message);
                    }
                }
            }
            finally
            {
                ReleaseLock();
            }

            if (restoreException != null)
            {
                throw restoreException;
            }
        }

        private void ReleaseLock()
        {
            if (hasLock && txLock != null)
            {
                try { txLock.Release(); } catch { }
                hasLock = false;
            }
            if (txLock != null)
            {
                try { txLock.Close(); } catch { }
                txLock = null;
            }
        }

        public static RecoveryResult CheckAndRecoverDanglingTransactions()
        {
            ITransactionLock lockObj = LockProvider();
            bool acquired = false;
            try
            {
                bool viaAbandon = false;
                acquired = lockObj.TryAcquire(3000, out viaAbandon);
                if (!acquired)
                {
                    return new RecoveryResult
                    {
                        Success = false,
                        Message = "获取事务锁超时 (3000ms)，可能有活跃事务正在运行，跳过启动恢复"
                    };
                }

                return RecoverDanglingTransactionsLocked(EnvironmentStorage, WalStorage);
            }
            catch (Exception ex)
            {
                return new RecoveryResult
                {
                    Success = false,
                    Message = "启动恢复异常: " + ex.Message
                };
            }
            finally
            {
                if (acquired)
                {
                    try { lockObj.Release(); } catch { }
                }
                lockObj.Close();
            }
        }

        internal static RecoveryResult RecoverDanglingTransactionsLocked(IEnvironmentStorage env, IWalStorage wal)
        {
            if (wal == null || !wal.Exists())
            {
                return new RecoveryResult { Success = true, HasDangling = false, Message = "无待恢复事务" };
            }

            EnvTransactionLog log;
            try
            {
                byte[] data = wal.ReadAllBytes();
                log = DeserializeLog(data);
                if (log == null)
                {
                    return new RecoveryResult
                    {
                        Success = false,
                        HasDangling = true,
                        Message = "事务日志内容为空或未能解析为有效对象，已拒绝恢复以保护环境"
                    };
                }
            }
            catch (Exception ex)
            {
                return new RecoveryResult
                {
                    Success = false,
                    HasDangling = true,
                    Message = "读取或解析事务日志失败 (格式损坏或缺失必要字段): " + ex.Message
                };
            }

            string validationError;
            if (!ValidateLogStructure(log, out validationError))
            {
                return new RecoveryResult
                {
                    Success = false,
                    HasDangling = true,
                    Message = "事务日志已损坏或未通过完整性校验，已拒绝恢复以保护环境: " + validationError
                };
            }

            bool allResolved = true;
            int recovered = 0;
            int conflicts = 0;

            foreach (EnvVariableRecord rec in log.Variables)
            {
                if (rec.Restored) continue;

                try
                {
                    string cur = env.GetVariable(rec.Key);
                    if (string.Equals(cur, rec.InjectedValue, StringComparison.Ordinal))
                    {
                        env.SetVariable(rec.Key, rec.Existed ? rec.OriginalValue : null);
                        rec.Restored = true;
                        rec.Conflict = false;
                        recovered++;
                    }
                    else
                    {
                        // External modification conflict
                        rec.Conflict = true;
                        rec.Restored = true;
                        rec.ConflictDetail = string.Format("外部修改冲突: 变量 '{0}' 值已被外部修改，为避免破坏外部配置已保留当前值", rec.Key);
                        conflicts++;
                    }
                }
                catch (Exception ex)
                {
                    allResolved = false;
                    rec.ConflictDetail = "恢复项执行失败: " + ex.Message;
                }
            }

            try { env.BroadcastChange(); } catch { }

            if (allResolved)
            {
                try
                {
                    wal.Delete();
                }
                catch (Exception ex)
                {
                    return new RecoveryResult
                    {
                        Success = false,
                        HasDangling = true,
                        RecoveredCount = recovered,
                        ConflictCount = conflicts,
                        Message = "所有项已恢复但删除 WAL 失败: " + ex.Message
                    };
                }

                return new RecoveryResult
                {
                    Success = true,
                    HasDangling = true,
                    RecoveredCount = recovered,
                    ConflictCount = conflicts,
                    Message = string.Format("已完成挂起事务恢复 (已恢复: {0}, 冲突保留: {1})", recovered, conflicts)
                };
            }
            else
            {
                // Preserve unresolved records
                try
                {
                    byte[] updated = SerializeLog(log);
                    wal.WriteAtomic(updated);
                }
                catch { }

                return new RecoveryResult
                {
                    Success = false,
                    HasDangling = true,
                    RecoveredCount = recovered,
                    ConflictCount = conflicts,
                    Message = "部分挂起事务项恢复失败，已保留未解决记录"
                };
            }
        }

        internal static byte[] SerializeLog(EnvTransactionLog log)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(EnvTransactionLog));
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, log);
                return ms.ToArray();
            }
        }

        internal static EnvTransactionLog DeserializeLog(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(EnvTransactionLog));
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return (EnvTransactionLog)ser.ReadObject(ms);
            }
        }
    }

    public static class ProcessEnvironmentBuilder
    {
        public static Dictionary<string, string> BuildEnvironment(LauncherSettings settings)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Timezone
            string tzValue = TimezoneHelper.BuildProcessTzValue(
                settings.Mode, settings.IanaName, settings.UtcOffset, settings.DisableTz);

            if (tzValue != null)
            {
                env["TZ"] = tzValue;
            }
            else
            {
                env["TZ"] = null; // explicitly cleared
            }

            // 2. Proxy
            if (string.Equals(settings.NetworkMode, "vpn", StringComparison.OrdinalIgnoreCase))
            {
                ProxyConfig proxy = new ProxyConfig(settings.ProxyHost, settings.ProxyPort);
                string proxyUrl = proxy.ToUrl();

                env["HTTP_PROXY"] = proxyUrl;
                env["HTTPS_PROXY"] = proxyUrl;
                env["ALL_PROXY"] = proxyUrl;
                env["NO_PROXY"] = "localhost,127.0.0.1,::1";
            }
            else
            {
                // In Direct mode: explicitly remove proxy variables to prevent inheritance from parent process
                env["HTTP_PROXY"] = null;
                env["HTTPS_PROXY"] = null;
                env["ALL_PROXY"] = null;
                env["NO_PROXY"] = null;
            }

            return env;
        }
    }
}
