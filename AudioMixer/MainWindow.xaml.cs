using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
namespace AuraMixer

{
    public partial class MainWindow : Window
    {
        private SerialPort _serialPort;
        private string _serialBuffer = "";
        
        private readonly int[] _previousValues = { -1, -1, -1, -1, -1 };
        private const int DEADZONE = 5; 

        private TextBlock[] _volTexts;
        private TextBlock[] _rawTexts;
        private TextBox[] _processInputs;
        private MMDeviceEnumerator _audioEnumerator;
        private MMDevice _defaultDevice;
        private ComboBox[] _processCombos;
        private bool _isSlidersReversed = true;
        private Dictionary<uint, string> _pidCache = new Dictionary<uint, string>();
        private System.Windows.Forms.NotifyIcon _trayIcon; 
        private bool _isRealExit = false;
        public MainWindow()
        {
            InitializeComponent();

     
            _volTexts = new[] { TxtVol0, TxtVol1, TxtVol2, TxtVol3, TxtVol4 };
            _rawTexts = new[] { TxtRaw0, TxtRaw1, TxtRaw2, TxtRaw3, TxtRaw4 };
            _processInputs = new[] { InputProcess0, InputProcess1, InputProcess2, InputProcess3, InputProcess4 };
            _processCombos = new[] { ComboProcess0, ComboProcess1, ComboProcess2, ComboProcess3, ComboProcess4 };

            
            for (int i = 0; i < 5; i++)
            {
                _processCombos[i].Items.Add("Main (Master)");
                _processCombos[i].Items.Add("Current App");
                _processCombos[i].Items.Add("Other...");
                _processCombos[i].SelectedIndex = 0; 
            }
            
            _audioEnumerator = new MMDeviceEnumerator();
            _defaultDevice = _audioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            SetupTrayIcon();
            
            LoadSettings();
        }
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        private string GetForegroundProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow(); // ID активного окна
                if (hwnd == IntPtr.Zero) return null;
                GetWindowThreadProcessId(hwnd, out uint pid); // ID процесса этого окна
                return Process.GetProcessById((int)pid).ProcessName; //  текстовое имя
            }
            catch { return null; }
        }
        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();
            _trayIcon.Icon = System.Drawing.SystemIcons.Information; 
            _trayIcon.Text = "Aura Mixer";
            _trayIcon.Visible = true;

       
            _trayIcon.DoubleClick += (s, e) => 
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            };
            
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Выход", null, (s, e) => {
                _isRealExit = true; 
                this.Close();
            });
            _trayIcon.ContextMenuStrip = menu;
        }
        private void ComboComPort_DropDownOpened(object sender, EventArgs e)
        {
            ComboComPort.ItemsSource = SerialPort.GetPortNames();
        }
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                BtnConnect.Content = "Подключить";
                BtnConnect.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3b82f6")); // Синий
                Title = "Aura Mixer | Отключен";
                return;
            }

            string portName = ComboComPort.Text.Trim();
            if (string.IsNullOrEmpty(portName))
            {
                MessageBox.Show("Выберите или введите COM-порт!", "Aura Mixer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _serialPort = new SerialPort(portName, 115200);
                _serialPort.DataReceived += Port_DataReceived;
                _serialPort.Open();
        
                BtnConnect.Content = "Отключить";
                BtnConnect.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444")); // Красный
                Title = $"Aura Mixer | Активен ({portName})";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения: {ex.Message}", "Aura Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BtnOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SaveSettings(); 
        }

        private void CheckAutoStart_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        
                string appName = "AuraMixer";
                
                string appPath = Process.GetCurrentProcess().MainModule.FileName; 

                if (CheckAutoStart.IsChecked == true)
                {
                    rk.SetValue(appName, appPath); 
                }
                else
                {
                    rk.DeleteValue(appName, false); 
                }
            }
            catch {  }
        }
      
        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort port = (sender as SerialPort);
            if (port == null) return;

            try
            {
                string chunk = port.ReadExisting();
                _serialBuffer += chunk;

                if (!_serialBuffer.Contains('\n')) return;

                string[] lines = _serialBuffer.Split('\n');
                _serialBuffer = lines[^1]; 

                string latestData = lines[^2].Trim();
                string[] stringValues = latestData.Split('|');

                if (stringValues.Length == 5)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        if (int.TryParse(stringValues[i], out int currentValue))
                        {
                            if (Math.Abs(currentValue - _previousValues[i]) > DEADZONE)
                            {
                                float volumePercent = currentValue / 1023f;
                                _previousValues[i] = currentValue;
                                int hwIndex = i; 
                                int uiIndex = _isSlidersReversed ? (4 - hwIndex) : hwIndex;
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                              
                                    string targetProcess = "";
                                    var selectedItem = _processCombos[uiIndex].SelectedItem?.ToString();
                                    if (selectedItem == "Main (Master)") 
                                    {
                                        targetProcess = "master";
                                    }
                                    else if (selectedItem == "Current App") 
                                    {
                                        targetProcess = GetForegroundProcessName();
                                    }
                                    else if (selectedItem == "Other...") 
                                    {
                                        targetProcess = _processInputs[uiIndex].Text.Trim(); 
                                    }
                                    else if (!string.IsNullOrEmpty(selectedItem))
                                    {
                                        targetProcess = selectedItem; 
                                    }
                                    
                                    if (targetProcess == "master")
                                    {
                                        SetMasterVolume(volumePercent);
                                    }
                                    else if (!string.IsNullOrEmpty(targetProcess))
                                    {
                                        SetProcessVolume(targetProcess, volumePercent);
                                    }

                                    
                                    if (this.Visibility == Visibility.Visible && this.WindowState != WindowState.Minimized)
                                    {
                                        _volTexts[uiIndex].Text = $"{volumePercent * 100:F0}%";
                                        _rawTexts[uiIndex].Text = $"Raw: {currentValue}";
                                        Title = $"Aura Mixer | Цель: [{targetProcess}] | Raw: {currentValue}"; 
                                    }
                                }));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
              
                Dispatcher.Invoke(() => { Title = $"Ошибка: {ex.Message}"; });
            }
        }
        private void SetMasterVolume(float volume)
        {
            try
            {
                _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch { }
        }
      
        private bool ApplyVolumeToSessions(string targetNames, float volume)
        {
            if (string.IsNullOrEmpty(targetNames)) return false;

            var sessions = _defaultDevice.AudioSessionManager.Sessions;
            bool foundAny = false;
            
            string[] rawTargets = targetNames.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                uint pid = session.GetProcessID;
                if (pid == 0) continue; 

                string pName;
                if (!_pidCache.TryGetValue(pid, out pName) || pName == "unknown")
                {
                    try {
                        using var p = Process.GetProcessById((int)pid);
                        pName = p.ProcessName.ToLower();
                        _pidCache[pid] = pName;
                    } catch { 
                        _pidCache[pid] = "unknown"; 
                        pName = "unknown"; 
                    }
                }

                if (string.IsNullOrEmpty(pName) || pName == "unknown") continue;
                
                foreach (string target in rawTargets)
                {
                    if (string.Equals(pName, target.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        try 
                        {
                            session.SimpleAudioVolume.Volume = volume;
                            foundAny = true; 
                        }
                        catch {  }
                        break; 
                    }
                }
            }
            return foundAny; 
        }

    private void SetProcessVolume(string processName, float volume)
    {
        try
        {
            bool found = ApplyVolumeToSessions(processName, volume);
            if (!found)
            {
                _defaultDevice?.Dispose();
                _defaultDevice = _audioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _pidCache.Clear(); 
                ApplyVolumeToSessions(processName, volume);
            }
        }
        catch { }
    }
        private void Combo_DropDownOpened(object sender, EventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;

            string currentSelection = combo.SelectedItem?.ToString();
            combo.Items.Clear();
            
            combo.Items.Add("Main (Master)");
            combo.Items.Add("Current App");
            combo.Items.Add("Other...");
            
            _defaultDevice?.Dispose();
            _defaultDevice = _audioEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _pidCache.Clear();
            var sessions = _defaultDevice.AudioSessionManager.Sessions;
            List<string> activeApps = new List<string>();
    
            for (int i = 0; i < sessions.Count; i++)
            {
                uint pid = sessions[i].GetProcessID;
                if (pid == 0) continue;
                try
                {
                    string pName = Process.GetProcessById((int)pid).ProcessName;
                    if (!activeApps.Contains(pName)) activeApps.Add(pName);
                }
                catch { }
            }

            foreach (var app in activeApps) combo.Items.Add(app);

            if (currentSelection != null && combo.Items.Contains(currentSelection))
                combo.SelectedItem = currentSelection;
        }

        private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || combo.SelectedItem == null) return;

           
            int index = Array.IndexOf(_processCombos, combo);
            if (index == -1) return;

           
            if (combo.SelectedItem.ToString() == "Other...")
            {
                _processInputs[index].Visibility = Visibility.Visible;
            }
            else
            {
                _processInputs[index].Visibility = Visibility.Collapsed;
            }
        }
        private void ToggleReverse_Click(object sender, RoutedEventArgs e)
        {
            _isSlidersReversed = ToggleReverse.IsChecked == true;
            
            if (_isSlidersReversed)
                ToggleReverse.Content = "🔄 Инверсия: ВКЛ";
            else
                ToggleReverse.Content = "➡️ Инверсия: ВЫКЛ";
        }
        
        private readonly string _configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private void SaveSettings()
        {
            try
            {
                var settings = new MixerSettings
                {
                    ComPort = ComboComPort.Text.Trim(),
                    IsSlidersReversed = _isSlidersReversed,
                    SelectedItems = new string[5],
                    CustomProcesses = new string[5],
                    AutoStart = CheckAutoStart.IsChecked == true
                };

              
                for (int i = 0; i < 5; i++)
                {
                    settings.SelectedItems[i] = _processCombos[i].SelectedItem?.ToString() ?? "Main (Master)";
                    settings.CustomProcesses[i] = _processInputs[i].Text.Trim();
                }
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(settings, options);
        
                File.WriteAllText(_configPath, jsonString);
            }
            catch {  }
        }
        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_configPath)) return; 

                string jsonString = File.ReadAllText(_configPath);
                var settings = JsonSerializer.Deserialize<MixerSettings>(jsonString);

                if (settings == null) return;

                ComboComPort.Text = settings.ComPort;

                _isSlidersReversed = settings.IsSlidersReversed;
                ToggleReverse.IsChecked = _isSlidersReversed;
                ToggleReverse.Content = _isSlidersReversed ? "🔄 Инверсия: ВКЛ" : "➡️ Инверсия: ВЫКЛ";
                CheckAutoStart.IsChecked = settings.AutoStart;

                for (int i = 0; i < 5; i++)
                {

                    string savedItem = settings.SelectedItems[i];
                    if (!_processCombos[i].Items.Contains(savedItem))
                    {
                        _processCombos[i].Items.Add(savedItem);
                    }
                    _processCombos[i].SelectedItem = savedItem;

                    _processInputs[i].Text = settings.CustomProcesses[i];
                    if (savedItem == "Other...")
                    {
                        _processInputs[i].Visibility = Visibility.Visible;
                    }
                }

                if (!string.IsNullOrEmpty(settings.ComPort))
                {
                    BtnConnect_Click(null, null);
                }
            }
            catch { }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true; 
                this.Hide();     
                return;
            }

             SaveSettings();
            
             _trayIcon?.Dispose();
            if (_serialPort is { IsOpen: true })
            {
                _serialPort.Close();
            }
            _defaultDevice?.Dispose();
            _audioEnumerator?.Dispose();
        }
    }
    public class MixerSettings
    {
        public string ComPort { get; set; } = "";
        public bool IsSlidersReversed { get; set; } = true;
        public string[] SelectedItems { get; set; } = new string[5];
        public string[] CustomProcesses { get; set; } = new string[5];
        
        public bool AutoStart { get; set; } = false;
        
    }
}