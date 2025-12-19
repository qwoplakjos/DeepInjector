using DeepInjector.Models;
using DeepInjector.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

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


        private DispatcherTimer _processRefreshTimer;
        private DispatcherTimer _foregroundTimer;


        bool shouldUpdate = false;

        public MainWindow()
        {
            InitializeComponent();

            _injectorService = new InjectorService();
            _fileAccessService = new FileAccessService();
            _settings = InjectorSettings.Load();
            _dllEntries = new ObservableCollection<DllEntry>(_settings.DllEntries);

            DllListBox.ItemsSource = _dllEntries;

            if (!string.IsNullOrEmpty(_settings.LastTargetProcess))
            {
                ProcessComboBox.Text = _settings.LastTargetProcess;
            }


            _processRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };

            _foregroundTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            _foregroundTimer.Tick += (s, e) =>
            {
                shouldUpdate = IsWindowInForeground();
            };

            _processRefreshTimer.Tick += (s, e) =>
            {
                if (shouldUpdate)
                    RefreshProcessList();
            };

            _processRefreshTimer.Start();
            _foregroundTimer.Start();


        }

        private bool IsWindowInForeground()
        {
            IntPtr hwnd = GetForegroundWindow();
            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            return processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
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

        private void ToggleMaximizeWindow()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
            UpdateUIState();
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

                // Check if the DLL is already in the list
                if (_dllEntries.Any(d => d.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("This DLL is already in your list.", "Duplicate DLL", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DllEntry newEntry = new DllEntry(fileName, filePath);
                _dllEntries.Add(newEntry);

                SaveSettings();

                // Select the newly added DLL
                DllListBox.SelectedItem = newEntry;

                StatusTextBlock.Text = $"Added DLL: {fileName}";
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

                StatusTextBlock.Text = $"Removed DLL: {selectedDll.Name}";
            }
        }

        private void DllListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateUIState();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
            StatusTextBlock.Text = "Process list refreshed";
        }

        private void ProcessComboBox_FocusableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Enable dropdown clicking when the user clicks anywhere on the ComboBox
            if (sender is ComboBox comboBox)
            {
                if (comboBox.IsEditable && comboBox.Template != null)
                {
                    comboBox.IsDropDownOpen = true;
                }
            }
        }

        private void ProcessComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateUIState();
        }

        private void InjectButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDll = DllListBox.SelectedItem as DllEntry;
            string processName = ProcessComboBox.Text;

            if (selectedDll != null && !string.IsNullOrWhiteSpace(processName))
            {
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

                    _settings.LastTargetProcess = processName;
                    SaveSettings();

                    string result = _injectorService.InjectDll(processName, selectedDll.FilePath);

                    StatusTextBlock.Text = result;

                    if (!result.Contains("successfully"))
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 50, 50));
                        MessageBox.Show(result, "Injection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(50, 255, 50));
                    }
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error during injection";
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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


        private void RefreshProcessList()
        {
            string currentSelection = ProcessComboBox.Text;
            ProcessComboBox.Items.Clear();
            foreach (var process in _injectorService.GetRunningProcesses())
            {
                ProcessComboBox.Items.Add(process);
            }
            if (!string.IsNullOrEmpty(currentSelection))
            {
                ProcessComboBox.Text = currentSelection;
            }
        }

    }
}
