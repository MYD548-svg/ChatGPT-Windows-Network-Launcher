using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ChatGPTAntiBanLauncher
{
    public enum LaunchWarningLevel
    {
        None,
        Warning,
        Error
    }

    public sealed class LaunchResult
    {
        public bool Succeeded { get; set; }
        public uint TargetPid { get; set; }
        public string Summary { get; set; }
        public string ProcessStatus { get; set; }
        public string EnvironmentAdoptionStatus { get; set; }
        public string ErrorMessage { get; set; }
        public LaunchWarningLevel WarningLevel { get; set; }
        public bool HasConflicts { get; set; }
        public int ConflictCount { get; set; }
        public string WarningMessage { get; set; }
    }

    public enum ProcessMatchDecision
    {
        ExactExeMatch,
        DirectoryMatch,
        PackageFamilyMatch,
        RejectedSimilarDirectoryPrefix,
        RejectedPackageMismatch,
        RejectedUnverifiableIdentity,
        RejectedNullTarget
    }

    public static class LauncherService
    {
        public static Func<string, uint> PackagedAppActivator = PackagedAppLauncher.Activate;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetPackageFamilyName(
            IntPtr hProcess,
            ref uint packageFamilyNameLength,
            StringBuilder packageFamilyName);

        public static ProcessMatchDecision EvaluateProcessMatch(
            string candidatePath,
            string candidatePackageFamily,
            TargetClientInfo targetInfo)
        {
            if (targetInfo == null || !targetInfo.IsFound)
            {
                return ProcessMatchDecision.RejectedNullTarget;
            }

            // 1. If candidate executable path is accessible
            if (!string.IsNullOrEmpty(candidatePath))
            {
                string normCandidate = Path.GetFullPath(candidatePath);

                // Exact executable path match
                if (!string.IsNullOrEmpty(targetInfo.ExecutablePath))
                {
                    string normTargetExe = Path.GetFullPath(targetInfo.ExecutablePath);
                    if (string.Equals(normCandidate, normTargetExe, StringComparison.OrdinalIgnoreCase))
                    {
                        return ProcessMatchDecision.ExactExeMatch;
                    }
                }

                // Strict directory boundary check
                if (!string.IsNullOrEmpty(targetInfo.InstallDirectory))
                {
                    string normTargetDir = Path.GetFullPath(targetInfo.InstallDirectory);
                    if (!normTargetDir.EndsWith("\\") && !normTargetDir.EndsWith("/"))
                    {
                        normTargetDir += "\\";
                    }

                    if (normCandidate.StartsWith(normTargetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return ProcessMatchDecision.DirectoryMatch;
                    }

                    // Check if matched only as a prefix without boundary (e.g. ChatGPT-other vs ChatGPT)
                    string rawDirNoSlash = normTargetDir.TrimEnd('\\', '/');
                    if (normCandidate.StartsWith(rawDirNoSlash, StringComparison.OrdinalIgnoreCase))
                    {
                        return ProcessMatchDecision.RejectedSimilarDirectoryPrefix;
                    }
                }
            }
            else
            {
                // 2. Path is not accessible (e.g. UAC / AppContainer isolation)
                // Require verified package family identity evidence
                if (targetInfo.Type == ClientInstallationType.StorePackage &&
                    !string.IsNullOrEmpty(targetInfo.PackageFamilyName))
                {
                    if (!string.IsNullOrEmpty(candidatePackageFamily))
                    {
                        if (string.Equals(candidatePackageFamily.Trim(), targetInfo.PackageFamilyName.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            return ProcessMatchDecision.PackageFamilyMatch;
                        }
                        return ProcessMatchDecision.RejectedPackageMismatch;
                    }
                }

                return ProcessMatchDecision.RejectedUnverifiableIdentity;
            }

            return ProcessMatchDecision.RejectedUnverifiableIdentity;
        }

        public static List<Process> FindMatchingTargetProcesses(TargetClientInfo targetInfo)
        {
            List<Process> matching = new List<Process>();
            if (targetInfo == null || !targetInfo.IsFound) return matching;

            List<Process> candidates = new List<Process>();
            try { candidates.AddRange(Process.GetProcessesByName("ChatGPT")); } catch { }
            try { candidates.AddRange(Process.GetProcessesByName("Codex")); } catch { }

            foreach (Process p in candidates)
            {
                bool matched = false;
                try
                {
                    string mainModulePath = null;
                    try
                    {
                        if (p.MainModule != null) mainModulePath = p.MainModule.FileName;
                    }
                    catch { }

                    string pkgFamily = null;
                    if (string.IsNullOrEmpty(mainModulePath))
                    {
                        pkgFamily = TryGetProcessPackageFamilyName(p);
                    }

                    ProcessMatchDecision decision = EvaluateProcessMatch(mainModulePath, pkgFamily, targetInfo);
                    if (decision == ProcessMatchDecision.ExactExeMatch ||
                        decision == ProcessMatchDecision.DirectoryMatch ||
                        decision == ProcessMatchDecision.PackageFamilyMatch)
                    {
                        matching.Add(p);
                        matched = true;
                    }
                }
                catch { }

                if (!matched)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            return matching;
        }

        private static string TryGetProcessPackageFamilyName(Process p)
        {
            try
            {
                uint len = 0;
                int rc = GetPackageFamilyName(p.Handle, ref len, null);
                if (len > 0)
                {
                    StringBuilder sb = new StringBuilder((int)len);
                    rc = GetPackageFamilyName(p.Handle, ref len, sb);
                    if (rc == 0)
                    {
                        return sb.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool CloseExistingInstances(
            List<Process> procs,
            bool gracefulOnly,
            Func<string, bool> promptForceKill,
            out string failureReason,
            int gracefulTimeoutMs = 3000)
        {
            failureReason = null;
            if (procs == null || procs.Count == 0) return true;

            try
            {
                // 1. Send graceful close signal to main windows
                foreach (Process p in procs)
                {
                    try
                    {
                        if (!p.HasExited) p.CloseMainWindow();
                    }
                    catch { }
                }

                // 2. Wait up to gracefulTimeoutMs for graceful exit
                int waited = 0;
                bool allExited = false;
                while (waited < gracefulTimeoutMs)
                {
                    allExited = true;
                    foreach (Process p in procs)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                allExited = false;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (allExited) break;
                    Thread.Sleep(200);
                    waited += 200;
                }

                if (allExited) return true;

                // 3. Not all exited
                if (gracefulOnly)
                {
                    failureReason = "旧实例仍未退出，用户设置仅允许优雅退出";
                    return false;
                }

                // 4. Force kill authorization check: promptForceKill MUST be present and agreed
                if (promptForceKill == null)
                {
                    failureReason = "未提供强制关闭授权确认回调，禁止执行强制终止";
                    return false;
                }

                bool userAgreed = promptForceKill("ChatGPT 旧实例未能在指定时间内响应退出。\n\n是否强制结束旧进程以启动新环境？\n（点击【是】将强制终止旧进程；点击【否】取消本次启动）");
                if (!userAgreed)
                {
                    failureReason = "用户取消了强制结束旧进程操作";
                    return false;
                }

                // 5. Force kill and verify
                foreach (Process p in procs)
                {
                    try
                    {
                        if (!p.HasExited) p.Kill();
                    }
                    catch { }
                }

                Thread.Sleep(500);

                foreach (Process p in procs)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            failureReason = "强制结束旧进程失败，进程仍在运行中";
                            return false;
                        }
                    }
                    catch { }
                }

                return true;
            }
            finally
            {
                // Dispose all process handles
                foreach (Process p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        public static LaunchResult Launch(TargetClientInfo client, LauncherSettings settings)
        {
            if (client == null || !client.IsFound)
            {
                return new LaunchResult
                {
                    Succeeded = false,
                    ErrorMessage = "未找到已安装的官方 ChatGPT 客户端 (OpenAI.Codex 或独立桌面版)"
                };
            }

            Dictionary<string, string> env = ProcessEnvironmentBuilder.BuildEnvironment(settings);

            // Path A: Standalone desktop edition -> Direct child process environment injection
            if (client.Type == ClientInstallationType.StandaloneDesktop &&
                !string.IsNullOrEmpty(client.ExecutablePath) &&
                File.Exists(client.ExecutablePath))
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = client.ExecutablePath,
                        WorkingDirectory = client.InstallDirectory ?? Path.GetDirectoryName(client.ExecutablePath),
                        UseShellExecute = false
                    };

                    foreach (KeyValuePair<string, string> kv in env)
                    {
                        if (kv.Value != null)
                        {
                            psi.EnvironmentVariables[kv.Key] = kv.Value;
                        }
                        else
                        {
                            // Explicit removal in direct mode
                            if (psi.EnvironmentVariables.ContainsKey(kv.Key))
                            {
                                psi.EnvironmentVariables.Remove(kv.Key);
                            }
                        }
                    }

                    Process targetProc = Process.Start(psi);
                    uint pid = targetProc != null ? (uint)targetProc.Id : 0;

                    return new LaunchResult
                    {
                        Succeeded = true,
                        TargetPid = pid,
                        ProcessStatus = string.Format("独立进程已启动 (PID: {0})", pid),
                        EnvironmentAdoptionStatus = "已完成进程级独立环境变量注入 (UseShellExecute=false)",
                        Summary = "独立版启动成功，环境已注入子进程"
                    };
                }
                catch (Exception ex)
                {
                    return new LaunchResult
                    {
                        Succeeded = false,
                        ErrorMessage = "启动独立桌面客户端失败: " + ex.Message
                    };
                }
            }

            // Path B: Windows Store AppX edition (OpenAI.Codex)
            if (client.Type == ClientInstallationType.StorePackage &&
                !string.IsNullOrEmpty(client.AppUserModelId))
            {
                UserEnvironmentTransaction tx = null;
                try
                {
                    tx = new UserEnvironmentTransaction(env);
                }
                catch (Exception txEx)
                {
                    return new LaunchResult
                    {
                        Succeeded = false,
                        ErrorMessage = "环境事务初始化失败: " + txEx.Message
                    };
                }

                uint pid = 0;
                Exception comEx = null;
                try
                {
                    pid = PackagedAppActivator(client.AppUserModelId);
                }
                catch (Exception ex)
                {
                    comEx = ex;
                }

                // Explicit restore after launch attempt
                TransactionRestoreResult restoreResult = null;
                Exception restoreEx = null;
                try
                {
                    restoreResult = tx.Restore();
                }
                catch (Exception ex)
                {
                    restoreEx = ex;
                }
                finally
                {
                    try
                    {
                        tx.Dispose();
                    }
                    catch (Exception ex)
                    {
                        if (restoreEx == null) restoreEx = ex;
                    }
                }

                bool restoreOk = (restoreResult != null && restoreResult.Success && restoreEx == null);
                int conflictCount = restoreResult != null ? restoreResult.ConflictCount : 0;
                bool hasConflicts = conflictCount > 0;

                // Case 1: COM failed
                if (comEx != null)
                {
                    string err = "唤起官方应用商店版 COM 接口失败: " + comEx.Message;
                    if (!restoreOk)
                    {
                        err += string.Format("；且环境恢复异常 ({0})，请检查 WAL 事务日志！",
                            restoreResult != null ? restoreResult.Message : (restoreEx != null ? restoreEx.Message : "未知异常"));
                    }
                    else if (hasConflicts)
                    {
                        err += string.Format("；环境恢复阶段检测到 {0} 项外部修改冲突，已保留外部修改未覆盖", conflictCount);
                    }

                    return new LaunchResult
                    {
                        Succeeded = false,
                        TargetPid = 0,
                        WarningLevel = LaunchWarningLevel.Error,
                        HasConflicts = hasConflicts,
                        ConflictCount = conflictCount,
                        ErrorMessage = err,
                        ProcessStatus = "应用商店版拉起失败",
                        EnvironmentAdoptionStatus = restoreOk
                            ? (hasConflicts
                                ? string.Format("环境已恢复，但检测到 {0} 项外部修改冲突并已保留", conflictCount)
                                : "已恢复原始环境变量")
                            : "环境恢复失败，残留修改或未清理WAL",
                        Summary = hasConflicts
                            ? string.Format("应用商店版拉起失败；恢复阶段检测到 {0} 项外部修改并已保留", conflictCount)
                            : "应用商店版拉起失败"
                    };
                }

                // Case 2: COM succeeded, but restore failed or had conflicts / uncleaned WAL
                if (!restoreOk)
                {
                    string restoreErrMsg = restoreResult != null ? restoreResult.Message : (restoreEx != null ? restoreEx.Message : "恢复失败");
                    return new LaunchResult
                    {
                        Succeeded = false, // Must NOT report plain success
                        TargetPid = pid,
                        WarningLevel = LaunchWarningLevel.Error,
                        HasConflicts = hasConflicts,
                        ConflictCount = conflictCount,
                        ProcessStatus = string.Format("应用商店版已拉起 (PID: {0})，但环境恢复异常", pid),
                        EnvironmentAdoptionStatus = string.Format("环境恢复失败 ({0})，WAL 日志可能残留，请手动检查或重启恢复！", restoreErrMsg),
                        Summary = string.Format("客户端已启动(PID:{0})，但环境变量未完全恢复！", pid),
                        ErrorMessage = string.Format("应用已拉起(PID:{0})，但环境恢复阶段失败: {1}", pid, restoreErrMsg)
                    };
                }

                // Case 3: Both COM and restore succeeded (zero write failures)
                // Subcase 3A: External modification conflicts were detected and preserved
                if (hasConflicts)
                {
                    string warnMsg = string.Format("客户端已启动 (PID: {0})；恢复时检测到 {1} 项外部修改冲突，已保留外部值未覆盖", pid, conflictCount);
                    return new LaunchResult
                    {
                        Succeeded = true,
                        TargetPid = pid,
                        WarningLevel = LaunchWarningLevel.Warning,
                        HasConflicts = true,
                        ConflictCount = conflictCount,
                        WarningMessage = warnMsg,
                        ProcessStatus = string.Format("应用商店版已拉起 (PID: {0})", pid),
                        EnvironmentAdoptionStatus = string.Format("已完成瞬态环境恢复，但检测到 {0} 项外部修改冲突并已保留 (注意：Store版由Shell托管，环境实际生效性未经验证)", conflictCount),
                        Summary = string.Format("客户端已启动 (PID: {0})；发现 {1} 项外部修改，已保留", pid, conflictCount)
                    };
                }

                // Subcase 3B: Complete clean success (zero conflicts, zero failures)
                return new LaunchResult
                {
                    Succeeded = true,
                    TargetPid = pid,
                    WarningLevel = LaunchWarningLevel.None,
                    HasConflicts = false,
                    ConflictCount = 0,
                    ProcessStatus = string.Format("应用商店版已拉起 (PID: {0})", pid),
                    EnvironmentAdoptionStatus = "已执行瞬态注册表写入并已成功恢复 (注意：Store版由Shell托管，环境实际生效性未经验证)",
                    Summary = "应用商店版启动请求已发送，环境已恢复"
                };
            }

            return new LaunchResult
            {
                Succeeded = false,
                ErrorMessage = "未识别的客户端安装形态或缺少启动标识"
            };
        }
    }
}
