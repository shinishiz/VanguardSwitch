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
