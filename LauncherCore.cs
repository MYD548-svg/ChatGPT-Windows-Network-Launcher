using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ChatGPTAntiBanLauncher
{
    internal static class EnvironmentChangeBroadcaster
    {
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);
        private const int WmSettingChange = 0x001A;
        private const int SmtoAbortIfHung = 0x0002;

        public static void Broadcast()
        {
            try
            {
                UIntPtr result;
                SendMessageTimeout(
                    HwndBroadcast,
                    WmSettingChange,
                    UIntPtr.Zero,
                    "Environment",
                    SmtoAbortIfHung,
                    2000,
                    out result);
            }
            catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            int msg,
            UIntPtr wParam,
            string lParam,
            int fuFlags,
            int uTimeout,
            out UIntPtr lpdwResult);
    }

    internal static class PackagedAppLauncher
    {
        public static uint Activate(string appUserModelId)
        {
            object managerObject = new ApplicationActivationManager();
            IApplicationActivationManager manager = (IApplicationActivationManager)managerObject;
            try
            {
                uint pid = 0;
                int hr = manager.ActivateApplication(appUserModelId, null, ActivateOptions.None, out pid);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }
                return pid;
            }
            finally
            {
                Marshal.FinalReleaseComObject(managerObject);
            }
        }

        [Flags]
        private enum ActivateOptions
        {
            None = 0
        }

        [ComImport]
        [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private sealed class ApplicationActivationManager
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication(
                [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments,
                ActivateOptions options,
                out uint processId);

            [PreserveSig]
            int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, string verb, out uint processId);

            [PreserveSig]
            int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
        }
    }

    internal static class DesktopShortcutManager
    {
        public static void CreateOrUpdate(string shortcutName, string description)
        {
            string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopDirectory, shortcutName + ".lnk");
            string executablePath = Application.ExecutablePath;
            string workingDirectory = Path.GetDirectoryName(executablePath);

            object shellLinkObject = new ShellLink();
            IShellLinkW shellLink = (IShellLinkW)shellLinkObject;
            try
            {
                shellLink.SetPath(executablePath);
                shellLink.SetWorkingDirectory(workingDirectory);
                shellLink.SetDescription(description);
                shellLink.SetIconLocation(executablePath, 0);

                IPersistFile persistFile = (IPersistFile)shellLink;
                persistFile.Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private sealed class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010B-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }
    }
}
