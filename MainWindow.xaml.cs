// MainWindow.xaml.cs
using GPIMSClient.Models;
using GPIMSClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GPIMSClient
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MainWindow> _logger;
        private ICommunicationService? _communicationService;
        private IDataGeneratorService? _dataGeneratorService;

        private Timer? _transmissionTimer;
        private bool _isTransmitting = false;
        private int _packetsSent = 0;
        private int _successfulPackets = 0;
        private bool _isConnected = false;

        public ObservableCollection<LogEntry> LogEntries { get; } = new();

        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
                Dispatcher.BeginInvoke(() => UpdateUI());
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow(IServiceProvider serviceProvider, ILogger<MainWindow> logger)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
            _logger = logger;

            DataContext = this;
            LogItemsControl.ItemsSource = LogEntries;

            InitializeServices();
            InitializeTimers();

            AddLogEntry("Application started", LogLevel.Information);
        }

        private void InitializeServices()
        {
            try
            {
                _dataGeneratorService = _serviceProvider.GetRequiredService<IDataGeneratorService>();
                AddLogEntry("Data generator service initialized", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize services");
                AddLogEntry($"Service initialization failed: {ex.Message}", LogLevel.Error);
            }
        }

        private void InitializeTimers()
        {
            // Timer for updating current time
            var timeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timeTimer.Tick += (s, e) => TimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            timeTimer.Start();
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionButton.IsEnabled = false;
            StatusBarText.Text = "Testing connection...";

            try
            {
                CreateCommunicationService();

                if (_communicationService != null)
                {
                    var success = await _communicationService.TestConnectionAsync();
                    if (success)
                    {
                        AddLogEntry("Connection test successful", LogLevel.Information);
                        StatusBarText.Text = "Connection test successful";

                        if (AutoStartCheckBox.IsChecked == true && !_isTransmitting)
                        {
                            StartTransmission();
                        }
                    }
                    else
                    {
                        AddLogEntry("Connection test failed", LogLevel.Error);
                        StatusBarText.Text = "Connection test failed";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test error");
                AddLogEntry($"Connection test error: {ex.Message}", LogLevel.Error);
                StatusBarText.Text = "Connection test error";
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isTransmitting)
            {
                StopTransmission();
            }
            else
            {
                if (_communicationService == null)
                {
                    CreateCommunicationService();
                }
                StartTransmission();
            }
        }

        private void CreateCommunicationService()
        {
            var serverUrl = ServerUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(serverUrl))
            {
                MessageBox.Show("Please enter a valid server URL.", "Configuration Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient();
                var logger = _serviceProvider.GetRequiredService<ILogger<CommunicationService>>();

                _communicationService = new CommunicationService(httpClient, logger, serverUrl);
                _communicationService.StatusChanged += OnCommunicationStatusChanged;

                AddLogEntry($"Communication service created for {serverUrl}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create communication service");
                AddLogEntry($"Failed to create communication service: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnCommunicationStatusChanged(object? sender, CommunicationStatusEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                IsConnected = e.IsConnected;
                StatusText.Text = e.IsConnected ? "Connected" : "Disconnected";
                AddLogEntry(e.Message, e.IsConnected ? LogLevel.Information : LogLevel.Warning);
            });
        }

        private void StartTransmission()
        {
            if (_communicationService == null || _dataGeneratorService == null)
            {
                MessageBox.Show("Services not initialized. Please test connection first.",
                               "Service Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var deviceId = DeviceIdTextBox.Text.Trim();
            if (string.IsNullOrEmpty(deviceId))
            {
                MessageBox.Show("Please enter a valid device ID.", "Configuration Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isTransmitting = true;
            var interval = TimeSpan.FromMilliseconds((int)IntervalSlider.Value);

            _transmissionTimer = new Timer(async _ => await TransmitData(), null, TimeSpan.Zero, interval);

            UpdateUI();
            AddLogEntry($"Started transmission with {interval.TotalMilliseconds}ms interval", LogLevel.Information);
            StatusBarText.Text = "Transmitting data...";
        }

        private void StopTransmission()
        {
            _isTransmitting = false;
            _transmissionTimer?.Dispose();
            _transmissionTimer = null;

            UpdateUI();
            AddLogEntry("Transmission stopped", LogLevel.Information);
            StatusBarText.Text = "Transmission stopped";
        }

        private async Task TransmitData()
        {
            if (!_isTransmitting || _communicationService == null || _dataGeneratorService == null)
                return;

            try
            {
                var deviceId = "";
                var channelCount = 0;
                var auxCount = 0;
                var canCount = 0;
                var linCount = 0;

                // Get values from UI thread
                await Dispatcher.BeginInvoke(() =>
                {
                    deviceId = DeviceIdTextBox.Text.Trim();
                    channelCount = (int)ChannelCountSlider.Value;
                    auxCount = (int)AuxCountSlider.Value;
                    canCount = (int)CANCountSlider.Value;
                    linCount = (int)LINCountSlider.Value;
                });

                var deviceData = _dataGeneratorService.GenerateDeviceData(
                    deviceId, channelCount, auxCount, canCount, linCount);

                var success = await _communicationService.SendDeviceDataAsync(deviceData);

                await Dispatcher.BeginInvoke(() =>
                {
                    _packetsSent++;
                    if (success) _successfulPackets++;

                    UpdateStatistics();
                    UpdateLiveDataPreview(deviceData);
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data transmission");
                await Dispatcher.BeginInvoke(() =>
                {
                    AddLogEntry($"Transmission error: {ex.Message}", LogLevel.Error);
                });
            }
        }

        private void UpdateStatistics()
        {
            PacketsSentText.Text = _packetsSent.ToString();
            var successRate = _packetsSent > 0 ? (_successfulPackets * 100.0 / _packetsSent) : 0;
            SuccessRateText.Text = $"{successRate:F1}%";
            LastUpdateText.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void UpdateLiveDataPreview(DeviceData deviceData)
        {
            try
            {
                var summary = $"Device: {deviceData.DeviceId}\n" +
                             $"Timestamp: {deviceData.Timestamp:HH:mm:ss.fff}\n" +
                             $"Channels: {deviceData.Channels.Count}\n" +
                             $"Aux Data: {deviceData.AuxData.Count}\n" +
                             $"CAN Data: {deviceData.CANData.Count}\n" +
                             $"LIN Data: {deviceData.LINData.Count}\n\n";

                if (deviceData.Channels.Any())
                {
                    summary += "Sample Channel Data:\n";
                    var sampleChannel = deviceData.Channels.First();
                    summary += $"  Ch{sampleChannel.ChannelNumber}: {sampleChannel.Status}, " +
                              $"V={sampleChannel.Voltage:F3}, I={sampleChannel.Current:F3}\n\n";
                }

                // Show JSON preview (first 500 characters)
                var json = JsonConvert.SerializeObject(deviceData, Formatting.Indented);
                if (json.Length > 500)
                {
                    json = json.Substring(0, 500) + "...";
                }
                summary += "JSON Data:\n" + json;

                LiveDataTextBlock.Text = summary;
            }
            catch (Exception ex)
            {
                LiveDataTextBlock.Text = $"Error formatting data: {ex.Message}";
            }
        }

        private void UpdateUI()
        {
            StartStopButton.Content = _isTransmitting ? "Stop Transmission" : "Start Transmission";
            StartStopButton.Background = _isTransmitting ?
                new SolidColorBrush(Colors.OrangeRed) :
                new SolidColorBrush(Color.FromRgb(76, 175, 80));

            // Enable/disable configuration controls
            ServerUrlTextBox.IsEnabled = !_isTransmitting;
            DeviceIdTextBox.IsEnabled = !_isTransmitting;
            ChannelCountSlider.IsEnabled = !_isTransmitting;
            AuxCountSlider.IsEnabled = !_isTransmitting;
            CANCountSlider.IsEnabled = !_isTransmitting;
            LINCountSlider.IsEnabled = !_isTransmitting;
            IntervalSlider.IsEnabled = !_isTransmitting;
        }

        private void CountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender is Slider slider)
            {
                var value = (int)slider.Value;
                switch (slider.Name)
                {
                    case "ChannelCountSlider":
                        if (ChannelCountText != null) ChannelCountText.Text = value.ToString();
                        break;
                    case "AuxCountSlider":
                        if (AuxCountText != null) AuxCountText.Text = value.ToString();
                        break;
                    case "CANCountSlider":
                        if (CANCountText != null) CANCountText.Text = value.ToString();
                        break;
                    case "LINCountSlider":
                        if (LINCountText != null) LINCountText.Text = value.ToString();
                        break;
                }
            }
        }

        private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntervalText != null)
            {
                IntervalText.Text = ((int)e.NewValue).ToString();
            }

            // Update timer interval if transmitting
            if (_isTransmitting && _transmissionTimer != null)
            {
                StopTransmission();
                StartTransmission();
            }
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogEntries.Clear();
            AddLogEntry("Log cleared", LogLevel.Information);
        }

        private void AddLogEntry(string message, LogLevel level)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level,
                StatusBrush = GetStatusBrush(level)
            };

            // Add to collection on UI thread
            if (Dispatcher.CheckAccess())
            {
                LogEntries.Add(entry);

                // Keep only last 100 entries
                while (LogEntries.Count > 100)
                {
                    LogEntries.RemoveAt(0);
                }

                // Auto-scroll to bottom
                LogScrollViewer.ScrollToBottom();
            }
            else
            {
                Dispatcher.BeginInvoke(() => AddLogEntry(message, level));
            }
        }

        private SolidColorBrush GetStatusBrush(LogLevel level)
        {
            return level switch
            {
                LogLevel.Error => new SolidColorBrush(Colors.Red),
                LogLevel.Warning => new SolidColorBrush(Colors.Orange),
                LogLevel.Information => new SolidColorBrush(Colors.Green),
                LogLevel.Debug => new SolidColorBrush(Colors.Blue),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopTransmission();
            _communicationService = null;
            base.OnClosed(e);
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = string.Empty;
        public LogLevel Level { get; set; }
        public SolidColorBrush StatusBrush { get; set; } = new(Colors.Gray);
    }
}