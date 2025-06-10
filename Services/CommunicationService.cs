// Services/CommunicationService.cs
using GPIMSClient.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GPIMSClient.Services
{
    public interface ICommunicationService
    {
        Task<bool> SendDeviceDataAsync(DeviceData deviceData, CancellationToken cancellationToken = default);
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
        event EventHandler<CommunicationStatusEventArgs> StatusChanged;
    }

    public class CommunicationService : ICommunicationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CommunicationService> _logger;
        private readonly string _serverUrl;
        private bool _isConnected = false;

        public event EventHandler<CommunicationStatusEventArgs>? StatusChanged;

        public CommunicationService(HttpClient httpClient, ILogger<CommunicationService> logger, string serverUrl)
        {
            _httpClient = httpClient;
            _logger = logger;
            _serverUrl = serverUrl.TrimEnd('/');

            // Configure HttpClient
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GPIMSClient/1.0");
        }

        public async Task<bool> SendDeviceDataAsync(DeviceData deviceData, CancellationToken cancellationToken = default)
        {
            try
            {
                var json = JsonConvert.SerializeObject(deviceData, new JsonSerializerSettings
                {
                    DateFormatHandling = DateFormatHandling.IsoDateFormat,
                    NullValueHandling = NullValueHandling.Ignore
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{_serverUrl}/api/Device/data", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (!_isConnected)
                    {
                        _isConnected = true;
                        OnStatusChanged(true, "Connected to server");
                    }
                    return true;
                }
                else
                {
                    _logger.LogWarning($"Failed to send data. Status: {response.StatusCode}, Reason: {response.ReasonPhrase}");
                    if (_isConnected)
                    {
                        _isConnected = false;
                        OnStatusChanged(false, $"Server error: {response.StatusCode}");
                    }
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed");
                if (_isConnected)
                {
                    _isConnected = false;
                    OnStatusChanged(false, "Connection failed");
                }
                return false;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogError("Request timed out");
                if (_isConnected)
                {
                    _isConnected = false;
                    OnStatusChanged(false, "Connection timeout");
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending device data");
                if (_isConnected)
                {
                    _isConnected = false;
                    OnStatusChanged(false, "Unexpected error");
                }
                return false;
            }
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/Device/devices", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    if (!_isConnected)
                    {
                        _isConnected = true;
                        OnStatusChanged(true, "Connection test successful");
                    }
                    return true;
                }
                else
                {
                    if (_isConnected)
                    {
                        _isConnected = false;
                        OnStatusChanged(false, $"Connection test failed: {response.StatusCode}");
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed");
                if (_isConnected)
                {
                    _isConnected = false;
                    OnStatusChanged(false, "Connection test failed");
                }
                return false;
            }
        }

        private void OnStatusChanged(bool isConnected, string message)
        {
            StatusChanged?.Invoke(this, new CommunicationStatusEventArgs(isConnected, message));
        }
    }

    public class CommunicationStatusEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public CommunicationStatusEventArgs(bool isConnected, string message)
        {
            IsConnected = isConnected;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }
}