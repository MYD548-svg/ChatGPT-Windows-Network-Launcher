using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TestTools
{
    public static class ProcessEnvironmentReader
    {
        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref PROCESS_BASIC_INFORMATION processInformation,
            int processInformationLength,
            out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out IntPtr lpNumberOfBytesRead);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr Reserved3;
        }

        private const int ProcessBasicInformation = 0;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;

        public static Dictionary<string, string> ReadEnvironmentVariables(int pid)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
            if (hProc == IntPtr.Zero)
            {
                throw new Exception("OpenProcess failed with error: " + Marshal.GetLastWin32Error());
            }

            try
            {
                PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                int retLen;
                int status = NtQueryInformationProcess(hProc, ProcessBasicInformation, ref pbi, Marshal.SizeOf(pbi), out retLen);
                if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero)
                {
                    throw new Exception("NtQueryInformationProcess failed with status: 0x" + status.ToString("X8"));
                }

                // In 64-bit PEB, ProcessParameters pointer is at offset 0x20
                IntPtr paramsPtrAddr = new IntPtr(pbi.PebBaseAddress.ToInt64() + 0x20);
                byte[] ptrBuf = new byte[8];
                IntPtr bytesRead;
                if (!ReadProcessMemory(hProc, paramsPtrAddr, ptrBuf, 8, out bytesRead) || bytesRead.ToInt64() != 8)
                {
                    throw new Exception("Failed to read ProcessParameters pointer");
                }

                long procParamsAddr = BitConverter.ToInt64(ptrBuf, 0);
                if (procParamsAddr == 0)
                {
                    throw new Exception("ProcessParameters is null");
                }

                // In 64-bit RTL_USER_PROCESS_PARAMETERS, Environment pointer is at offset 0x80
                IntPtr envPtrAddr = new IntPtr(procParamsAddr + 0x80);
                if (!ReadProcessMemory(hProc, envPtrAddr, ptrBuf, 8, out bytesRead) || bytesRead.ToInt64() != 8)
                {
                    throw new Exception("Failed to read Environment pointer");
                }

                long envAddr = BitConverter.ToInt64(ptrBuf, 0);
                if (envAddr == 0)
                {
                    throw new Exception("Environment pointer is null");
                }

                // Read environment block chunks until double null character (Unicode \0\0)
                List<byte> rawBytes = new List<byte>();
                int chunkSize = 2048;
                byte[] chunk = new byte[chunkSize];
                long currentAddr = envAddr;

                while (true)
                {
                    if (!ReadProcessMemory(hProc, new IntPtr(currentAddr), chunk, chunkSize, out bytesRead) || bytesRead.ToInt32() == 0)
                    {
                        break;
                    }

                    int actualRead = bytesRead.ToInt32();
                    rawBytes.AddRange(chunk);

                    // Check for double null (two consecutive 0x00, 0x00 in UTF-16)
                    bool foundTerminator = false;
                    for (int i = 0; i < rawBytes.Count - 3; i += 2)
                    {
                        if (rawBytes[i] == 0 && rawBytes[i + 1] == 0 && rawBytes[i + 2] == 0 && rawBytes[i + 3] == 0)
                        {
                            foundTerminator = true;
                            break;
                        }
                    }

                    if (foundTerminator || actualRead < chunkSize || rawBytes.Count > 1024 * 1024)
                    {
                        break;
                    }

                    currentAddr += actualRead;
                }

                string fullEnvStr = Encoding.Unicode.GetString(rawBytes.ToArray());
                string[] entries = fullEnvStr.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string entry in entries)
                {
                    int eqIdx = entry.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string k = entry.Substring(0, eqIdx);
                        string v = entry.Substring(eqIdx + 1);
                        env[k] = v;
                    }
                }

                return env;
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        public static void Main(string[] args)
        {
            int pid = Process.GetCurrentProcess().Id;
            if (args.Length > 0) int.TryParse(args[0], out pid);

            Console.WriteLine("Reading environment for PID: " + pid);
            try
            {
                Dictionary<string, string> vars = ReadEnvironmentVariables(pid);
                Console.WriteLine("Successfully read " + vars.Count + " variables.");
                foreach (string key in new string[] { "PATH", "USERPROFILE", "TZ", "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" })
                {
                    if (vars.ContainsKey(key))
                    {
                        Console.WriteLine("  " + key + " = " + vars[key]);
                    }
                    else
                    {
                        Console.WriteLine("  " + key + " = <NOT SET>");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}
