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

        public MainWindow()
        {
            InitializeComponent();

     
            _volTexts = new[] { TxtVol0, TxtVol1, TxtVol2, TxtVol3, TxtVol4 };
            _rawTexts = new[] { TxtRaw0, TxtRaw1, TxtRaw2, TxtRaw3, TxtRaw4 };

            StartSerialConnection();
        }

        private void StartSerialConnection()
        {
            try
            {
           
                _serialPort = new SerialPort("COM3", 115200);
                _serialPort.DataReceived += Port_DataReceived;
                _serialPort.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка подключения к микшеру: {ex.Message}", "Aura Mixer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

                             
                                if (i == 4) 
                                {
                                    SetMasterVolume(volumePercent);
                                }
                                else if (i == 3) 
                                {
                                   
                                    SetProcessVolume("spotify", volumePercent); 
                                }

                                int index = i; 
                                Dispatcher.Invoke(() =>
                                {
                                    if (this.Visibility == Visibility.Visible && this.WindowState != WindowState.Minimized)
                                    {
                                        _volTexts[index].Text = $"{volumePercent * 100:F0}%";
                                        _rawTexts[index].Text = $"Raw: {currentValue}";
    
                                        Title = $"Aura Mixer | Активен"; 
                                    }
                                });
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
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
            }
            catch {  }
        }
      
        private void SetProcessVolume(string processName, float volume)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    uint pid = session.GetProcessID;
                    if (pid == 0) continue; 

                    try
                    {
                   
                        using var process = Process.GetProcessById((int)pid);
                        if (string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                        {
                           
                            session.SimpleAudioVolume.Volume = volume;
                        }
                    }
                    catch
                    {
                        
                    }
                }
            }
            catch {  }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
          
            if (_serialPort is { IsOpen: true })
            {
                _serialPort.Close();
            }
        }
    }
}