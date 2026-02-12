using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeepInjector.Services
{
    internal class ProcessService
    {
        const uint TH32CS_SNAPPROCESS = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

        [DllImport("kernel32.dll")]
        static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        public static IEnumerable<(int pid, string name)> GetProcessesFast()
        {
            var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero) yield break;

            var entry = new PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(entry);

            if (Process32First(snap, ref entry))
            {
                do
                {
                    yield return ((int)entry.th32ProcessID, entry.szExeFile);
                }
                while (Process32Next(snap, ref entry));
            }

            CloseHandle(snap);
        }
    }
}
