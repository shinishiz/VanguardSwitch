using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace VanguardSwitch
{
    static class Native
    {
        [DllImport("psapi.dll", SetLastError = true)]
        static extern bool EnumDeviceDrivers([Out] IntPtr[] addresses, uint cb, out uint needed);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
        static extern uint GetDeviceDriverBaseNameW(IntPtr imageBase, StringBuilder name, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            public string pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

        static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        /// <summary>
        /// True if the file has a valid Authenticode signature that chains to a trusted root
        /// (WinVerifyTrust). Revocation isn't checked online so this never waits on the network.
        /// </summary>
        public static bool AuthenticodeValid(string path)
        {
            var file = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)), pcwszFilePath = path };
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WINTRUST_FILE_INFO)));
            try
            {
                Marshal.StructureToPtr(file, pFile, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA)),
                    dwUIChoice = 2,              // WTD_UI_NONE
                    fdwRevocationChecks = 0,     // WTD_REVOKE_NONE
                    dwUnionChoice = 1,           // WTD_CHOICE_FILE
                    pFile = pFile,
                    dwStateAction = 0,           // WTD_STATEACTION_IGNORE
                    dwProvFlags = 0x10 | 0x1000, // WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                return WinVerifyTrust(new IntPtr(-1), WINTRUST_ACTION_GENERIC_VERIFY_V2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure(pFile, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(pFile);
            }
        }

        /// <summary>File names of the kernel drivers currently loaded (vgk.sys, ntoskrnl.exe, ...).</summary>
        public static List<string> LoadedDrivers()
        {
            var list = new List<string>();
            var addresses = new IntPtr[4096];
            if (!EnumDeviceDrivers(addresses, (uint)(addresses.Length * IntPtr.Size), out uint needed)) return list;

            int count = Math.Min((int)(needed / (uint)IntPtr.Size), addresses.Length);
            for (int i = 0; i < count; i++)
            {
                if (addresses[i] == IntPtr.Zero) continue;
                var sb = new StringBuilder(260);
                if (GetDeviceDriverBaseNameW(addresses[i], sb, 260) > 0) list.Add(sb.ToString());
            }
            return list;
        }

        public static string ProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            // 20 on Windows 10 2004+ and 11, 19 on older Windows 10 builds
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
    }
}
