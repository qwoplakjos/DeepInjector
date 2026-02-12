using DeepInjector.Models;
using DeepInjector.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;
using System.Management;
using DeepInjector.Services.DeepInjector;

namespace DeepInjector
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private InjectorService _injectorService;
        private FileAccessService _fileAccessService;
        private InjectorSettings _settings;
        private ObservableCollection<DllEntry> _dllEntries;
        private WmiProcessWatcher _wmiProcessWatcher;
        private ObservableCollection<ProcessItem> _processes;

        private volatile bool _isRefreshing = false;
        private volatile bool _writingText = false;
        private int _textWritingCounter = 20;

        public MainWindow()
        {
            InitializeComponent();

            _injectorService = new InjectorService();
            _fileAccessService = new FileAccessService();
            _settings = InjectorSettings.Load();
            _dllEntries = new ObservableCollection<DllEntry>(_settings.DllEntries);
            _processes = new ObservableCollection<ProcessItem>();

            DllListBox.ItemsSource = _dllEntries;
            ProcessComboBox.ItemsSource = _processes;

            if (!string.IsNullOrEmpty(_settings.LastTargetProcess))
            {
                ProcessComboBox.Text = _settings.LastTargetProcess;
            }

            _wmiProcessWatcher = new WmiProcessWatcher();
            SetupProcessWatcher();
        }

        private void SetupProcessWatcher()
        {
            _wmiProcessWatcher.ProcessStarted += (pid, name) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnProcessStarted(pid, name);
                }));
            };

            _wmiProcessWatcher.ProcessStopped += (pid) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnProcessStopped(pid);
                }));
            };
        }

        private void OnProcessStarted(int pid, string name)
        {
            if (pid <= 0)
                return;

            var existingProcess = _processes.FirstOrDefault(p => p.Pid == pid);
            if (existingProcess != null)
                return;

            var newProcess = new ProcessItem { Name = name, Pid = pid };

            int insertIndex = _processes.Count;
            for (int i = 0; i < _processes.Count; i++)
            {
                if (string.Compare(_processes[i].Name, name, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    insertIndex = i;
                    break;
                }
            }

            _processes.Insert(insertIndex, newProcess);
        }

        private void OnProcessStopped(int pid)
        {
            string currentText = ProcessComboBox.Text;
            var process = _processes.FirstOrDefault(p => p.Pid == pid);
            if (process != null)
            {
                _processes.Remove(process);
            }
            ProcessComboBox.Text = currentText;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitialLoadProcessList();
            UpdateUIState();

            try
            {
                _wmiProcessWatcher.Start();
            }
            catch
            {
                MessageBox.Show(
                    "Failed to start process monitoring. The application will work but won't auto-update the process list.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _wmiProcessWatcher?.Dispose();
            base.OnClosed(e);
        }

        private void UpdateUIState()
        {
            var selectedDll = DllListBox.SelectedItem as DllEntry;
            bool isDllSelected = selectedDll != null;
            bool isProcessSelected = !string.IsNullOrWhiteSpace(ProcessComboBox.Text);

            InjectButton.IsEnabled = isDllSelected && isProcessSelected;

            if (selectedDll != null)
            {
                SelectedDllPathTextBlock.Text = selectedDll.FilePath;
            }
            else
            {
                SelectedDllPathTextBlock.Text = "No DLL selected";
            }
        }

        private void AddDllButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Title = "Select DLL File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileNameWithoutExtension(filePath);

                if (_dllEntries.Any(d => d.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("This DLL is already in your list.", "Duplicate DLL", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DllEntry newEntry = new DllEntry(fileName, filePath);
                _dllEntries.Add(newEntry);

                SaveSettings();
                DllListBox.SelectedItem = newEntry;
                SetStatusTextAndResetAsync($"Added DLL: {fileName}", Color.FromRgb(255, 255, 255));
            }
        }

        private void RemoveDllButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDll = DllListBox.SelectedItem as DllEntry;

            if (selectedDll != null)
            {
                _dllEntries.Remove(selectedDll);
                SaveSettings();
                UpdateUIState();
                SetStatusTextAndResetAsync($"Removed DLL: {selectedDll.Name}", Color.FromRgb(255, 255, 255));
            }
        }

        private void DllListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateUIState();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
            StatusTextBlock.Text = "Process list refreshed";
            StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(50, 255, 50));
            await Task.Delay(1500);
            StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        }

        private void ProcessComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _textWritingCounter = 20;

            if (!_writingText)
            {
                _writingText = true;

                Task.Run(async () =>
                {
                    for (; _textWritingCounter > 0; _textWritingCounter -= 1)
                    {
                        await Task.Delay(100);
                    }

                    _writingText = false;
                });
            }

            UpdateUIState();
        }

        private void InjectButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDll = DllListBox.SelectedItem as DllEntry;
            var process = ProcessComboBox.SelectedItem as ProcessItem;

            if (process == null && !string.IsNullOrEmpty(ProcessComboBox.Text))
            {
                process = FindProcessByName(ProcessComboBox.Text);
            }

            if (process != null)
            {
                try
                {
                    var currentProcess = Process.GetProcessById(process.Pid);
                    if (!currentProcess.ProcessName.Equals(process.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"PID {process.Pid} is now a different process. Refreshing...");
                        RefreshProcessList();
                        process = FindProcessByName(process.Name);
                    }
                }
                catch (ArgumentException)
                {
                    RefreshProcessList();
                    process = FindProcessByName(process.Name);
                }
            }

            if (process == null && !string.IsNullOrEmpty(ProcessComboBox.Text))
            {
                RefreshProcessList();
                process = FindProcessByName(ProcessComboBox.Text);
            }

            if (process == null)
            {
                SetStatusTextAndResetAsync("Process not found", Color.FromRgb(255, 50, 50));
                return;
            }

            ProcessComboBox.SelectedItem = process;

            if (selectedDll == null || string.IsNullOrWhiteSpace(process.Name) || process.Pid <= 0)
                return;

            try
            {
                if (!File.Exists(selectedDll.FilePath))
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    StatusTextBlock.Text = "DLL doesn't exist anymore!";
                    StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 50, 50));
                    return;
                }

                _fileAccessService.SetAccessControl(selectedDll.FilePath);
                selectedDll.LastUsed = DateTime.Now;
                _settings.LastTargetProcess = process.Name;
                SaveSettings();

                string result = _injectorService.InjectDll(process.Pid, selectedDll.FilePath);
                Color resultColor;

                if (!result.Contains("successfully"))
                {
                    System.Media.SystemSounds.Asterisk.Play();
                    resultColor = Color.FromRgb(255, 50, 50);
                    MessageBox.Show(result, "Injection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    resultColor = Color.FromRgb(50, 255, 50);
                }

                SetStatusTextAndResetAsync(result, resultColor);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error during injection";
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private ProcessItem FindProcessByName(string name)
        {
            return _processes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private void SaveSettings()
        {
            _settings.DllEntries = _dllEntries.ToList();
            _settings.Save();
        }

        private void DllListBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Any(f => Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = DragDropEffects.Copy;

                    if (DllListBoxBorder != null)
                    {
                        DllListBoxBorder.BorderBrush = (SolidColorBrush)FindResource("AccentBrush");
                        DllListBoxBorder.BorderThickness = new Thickness(2);
                    }

                    StatusTextBlock.Text = "Drop DLL files to add them";
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void DllListBox_DragOver(object sender, DragEventArgs e)
        {
            DllListBox_DragEnter(sender, e);
        }

        private void DllListBox_DragLeave(object sender, DragEventArgs e)
        {
            if (DllListBoxBorder != null)
            {
                DllListBoxBorder.BorderBrush = (SolidColorBrush)FindResource("GlassBorderBrush");
                DllListBoxBorder.BorderThickness = new Thickness(0);
            }

            StatusTextBlock.Text = "Ready";
            e.Handled = true;
        }

        private void DllListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null)
                {
                    var dllFiles = files.Where(f => Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (dllFiles.Any())
                    {
                        int addedCount = 0;

                        foreach (var file in dllFiles)
                        {
                            if (_dllEntries.Any(d => d.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            string fileName = Path.GetFileNameWithoutExtension(file);
                            DllEntry newEntry = new DllEntry(fileName, file);
                            _dllEntries.Add(newEntry);
                            addedCount++;

                            if (addedCount == dllFiles.Count)
                            {
                                DllListBox.SelectedItem = newEntry;
                            }
                        }

                        if (addedCount > 0)
                        {
                            SaveSettings();
                            StatusTextBlock.Text = $"Added {addedCount} DLL{(addedCount > 1 ? "s" : "")}";
                        }
                        else
                        {
                            StatusTextBlock.Text = "All DLLs already in list";
                        }
                    }
                }
            }

            e.Handled = true;
        }

        private void InitialLoadProcessList()
        {
            string currentText = ProcessComboBox.Text;

            var processList = ProcessService.GetProcessesFast()
                .Where(p => p.pid > 0)
                .Select(p => new ProcessItem
                {
                    Name = p.name,
                    Pid = p.pid,
                })
                .OrderBy(p => p.Name)
                .ToList();

            _processes.Clear();
            foreach (var process in processList)
            {
                _processes.Add(process);
            }

            if (!string.IsNullOrEmpty(currentText))
            {
                ProcessComboBox.Text = currentText;
            }
        }


        private void RefreshProcessList()
        {
            if (_isRefreshing)
                return;

            _isRefreshing = true;
            Console.WriteLine("Refreshing process list...");
            try
            {
                string currentText = ProcessComboBox.Text;
                ProcessItem currentSelection = ProcessComboBox.SelectedItem as ProcessItem;

                var currentProcesses = ProcessService.GetProcessesFast()
                    .Where(p => p.pid > 0)
                    .Select(p => new ProcessItem { Name = p.name, Pid = p.pid })
                    .ToList();

                var currentPids = new HashSet<int>(currentProcesses.Select(p => p.Pid));
                var existingPids = new HashSet<int>(_processes.Select(p => p.Pid));

                for (int i = _processes.Count - 1; i >= 0; i--)
                {
                    if (!currentPids.Contains(_processes[i].Pid))
                    {
                        _processes.RemoveAt(i);
                    }
                }

                foreach (var process in currentProcesses)
                {
                    if (!existingPids.Contains(process.Pid))
                    {
                        int insertIndex = _processes.Count;
                        for (int i = 0; i < _processes.Count; i++)
                        {
                            if (string.Compare(_processes[i].Name, process.Name, StringComparison.OrdinalIgnoreCase) > 0)
                            {
                                insertIndex = i;
                                break;
                            }
                        }
                        _processes.Insert(insertIndex, process);
                    }
                }

                if (currentSelection != null)
                {
                    var matchingProcess = _processes.FirstOrDefault(p =>
                        p.Pid == currentSelection.Pid ||
                        p.Name.Equals(currentSelection.Name, StringComparison.OrdinalIgnoreCase));

                    if (matchingProcess != null)
                    {
                        ProcessComboBox.SelectedItem = matchingProcess;
                    }
                    else
                    {
                        ProcessComboBox.Text = currentText;
                    }
                }
                else if (!string.IsNullOrEmpty(currentText))
                {
                    ProcessComboBox.Text = currentText;
                   
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async void SetStatusTextAndResetAsync(string text, Color color, int delayMs = 1500)
        {
            StatusTextBlock.Text = text;
            StatusTextBlock.Foreground = new SolidColorBrush(color);
            await Task.Delay(delayMs);
            StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            StatusTextBlock.Text = "Ready";
        }
    }

    public class ProcessItem
    {
        public string Name { get; set; }
        public int Pid { get; set; }
    }
}