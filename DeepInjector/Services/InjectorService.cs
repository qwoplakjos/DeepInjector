using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DeepInjector.Services
{
    public class InjectorService
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // Constants
        private const int PROCESS_CREATE_THREAD = 0x0002;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_OPERATION = 0x0008;
        private const int PROCESS_VM_WRITE = 0x0020;
        private const int PROCESS_VM_READ = 0x0010;
        private const uint MEM_COMMIT = 0x00001000;
        private const uint MEM_RESERVE = 0x00002000;
        private const uint PAGE_READWRITE = 0x04;

        public string InjectDll(string processName, string dllPath)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                    return $"Process '{processName}' not found.";

                Process targetProcess = processes[0];
                
                // Get handle to the process
                IntPtr processHandle = OpenProcess(
                    PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                    false, targetProcess.Id);

                if (processHandle == IntPtr.Zero)
                    return "Failed to open process.";

                try
                {
                    // Get address of LoadLibraryA function
                    IntPtr loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                    if (loadLibraryAddr == IntPtr.Zero)
                        return "Failed to get LoadLibraryA address.";

                    // Allocate memory in the target process
                    byte[] dllPathBytes = Encoding.ASCII.GetBytes(dllPath);
                    IntPtr allocMemAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)dllPathBytes.Length + 1, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    if (allocMemAddress == IntPtr.Zero)
                        return "Failed to allocate memory in the target process.";

                    // Write the DLL path to the allocated memory
                    UIntPtr bytesWritten;
                    if (!WriteProcessMemory(processHandle, allocMemAddress, dllPathBytes, (uint)dllPathBytes.Length, out bytesWritten))
                        return "Failed to write to process memory.";

                    // Create a remote thread that calls LoadLibraryA with allocMemAddress as parameter
                    IntPtr threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero);
                    if (threadHandle == IntPtr.Zero)
                        return "Failed to create remote thread.";

                    CloseHandle(threadHandle);
                    return "DLL injected successfully!";
                }
                finally
                {
                    if (processHandle != IntPtr.Zero)
                        CloseHandle(processHandle);
                }
            }
            catch (Exception ex)
            {
                return $"Error during injection: {ex.Message}";
            }
        }

        public string[] GetRunningProcesses()
        {
            Process[] processes = Process.GetProcesses();
            string[] processNames = new string[processes.Length];

            for (int i = 0; i < processes.Length; i++)
            {
                processNames[i] = processes[i].ProcessName;
            }

            Array.Sort(processNames);
            return processNames;
        }
    }
} 