using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DeepInjector.Services
{
    using System;
    using System.Management;
    using System.Diagnostics;
    using System.Threading;

    namespace DeepInjector
    {
        public class WmiProcessWatcher : IDisposable
        {
            private ManagementEventWatcher _startWatcher;
            private ManagementEventWatcher _stopWatcher;
            private bool _disposed = false;

            public event Action<int, string> ProcessStarted;
            public event Action<int> ProcessStopped;

            public void Start()
            {
                try
                {
                    var startQuery = new WqlEventQuery("__InstanceCreationEvent",
                        TimeSpan.FromSeconds(1),
                        "TargetInstance ISA 'Win32_Process'");

                    _startWatcher = new ManagementEventWatcher(startQuery);
                    _startWatcher.EventArrived += OnProcessStarted;
                    _startWatcher.Start();

                    var stopQuery = new WqlEventQuery("__InstanceDeletionEvent",
                        TimeSpan.FromSeconds(1),
                        "TargetInstance ISA 'Win32_Process'");

                    _stopWatcher = new ManagementEventWatcher(stopQuery);
                    _stopWatcher.EventArrived += OnProcessStopped;
                    _stopWatcher.Start();

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to start WMI watcher: {ex.Message}");
                    throw;
                }
            }

            private void OnProcessStarted(object sender, EventArrivedEventArgs e)
            {
                try
                {
                    var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                    int processId = Convert.ToInt32(targetInstance["ProcessId"]);
                    string processName = targetInstance["Name"]?.ToString() ?? "Unknown";

                    ProcessStarted?.Invoke(processId, processName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in ProcessStarted handler: {ex.Message}");
                }
            }

            private void OnProcessStopped(object sender, EventArrivedEventArgs e)
            {
                try
                {
                    var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                    int processId = Convert.ToInt32(targetInstance["ProcessId"]);

                    ProcessStopped?.Invoke(processId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in ProcessStopped handler: {ex.Message}");
                }
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_startWatcher != null)
                {
                    _startWatcher.Stop();
                    _startWatcher.EventArrived -= OnProcessStarted;
                    _startWatcher.Dispose();
                }

                if (_stopWatcher != null)
                {
                    _stopWatcher.Stop();
                    _stopWatcher.EventArrived -= OnProcessStopped;
                    _stopWatcher.Dispose();
                }
            }
        }
    }
}
