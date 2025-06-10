using GPIMSClient.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GPIMSClient.Services
{
    public interface IDataGeneratorService
    {
        DeviceData GenerateDeviceData(string deviceId, int channelCount, int auxCount, int canCount, int linCount);
    }

    public class DataGeneratorService : IDataGeneratorService
    {
        private readonly ILogger<DataGeneratorService> _logger;
        private readonly Random _random = new();
        private readonly Dictionary<int, ChannelSimulationState> _channelStates = new();
        private readonly List<string> _testNames = new() { "Capacity Test", "Life Cycle", "Performance", "Stability", "Rate Test" };
        private readonly List<string> _schedules = new() { "Standard", "Fast Charge", "Slow Discharge", "Custom", "OCV" };
        private readonly List<string> _auxSensorNames = new() { "Temperature", "Humidity", "Pressure", "Voltage", "Current" };
        private readonly List<string> _canNames = new() { "BMS_Voltage", "BMS_Current", "BMS_Temperature", "Cell_Voltage", "SOC" };
        private readonly List<string> _linNames = new() { "Motor_Speed", "Battery_Temp", "System_Status", "Error_Code" };

        public DataGeneratorService(ILogger<DataGeneratorService> logger)
        {
            _logger = logger;
        }

        public DeviceData GenerateDeviceData(string deviceId, int channelCount, int auxCount, int canCount, int linCount)
        {
            var deviceData = new DeviceData
            {
                DeviceId = deviceId,
                Timestamp = DateTime.UtcNow
            };

            // Generate channel data
            for (int i = 1; i <= channelCount; i++)
            {
                if (!_channelStates.ContainsKey(i))
                {
                    _channelStates[i] = new ChannelSimulationState();
                }

                var channelData = GenerateChannelData(i, _channelStates[i]);
                deviceData.Channels.Add(channelData);
            }

            // Generate auxiliary data
            for (int i = 1; i <= auxCount; i++)
            {
                deviceData.AuxData.Add(GenerateAuxData(i));
            }

            // Generate CAN data
            for (int i = 1; i <= canCount; i++)
            {
                deviceData.CANData.Add(GenerateCANData(i));
            }

            // Generate LIN data
            for (int i = 1; i <= linCount; i++)
            {
                deviceData.LINData.Add(GenerateLINData(i));
            }

            // Occasionally generate alarms
            if (_random.NextDouble() < 0.1) // 10% chance
            {
                deviceData.AlarmData.Add(GenerateAlarmData());
            }

            return deviceData;
        }

        private ChannelData GenerateChannelData(int channelNumber, ChannelSimulationState state)
        {
            // Update simulation state
            UpdateChannelSimulationState(state);

            var channel = new ChannelData
            {
                ChannelNumber = channelNumber,
                Status = state.Status,
                Mode = state.Mode,
                CycleNo = state.CycleNo,
                StepNo = state.StepNo,
                CyclerLoop = state.CyclerLoop,
                TestName = _testNames[_random.Next(_testNames.Count)],
                Schedule = _schedules[_random.Next(_schedules.Count)]
            };

            // Generate realistic values based on status
            switch (state.Status)
            {
                case ChannelStatus.Charge:
                    channel.Voltage = Math.Round(3.2 + _random.NextDouble() * 1.0, 3); // 3.2-4.2V
                    channel.Current = Math.Round(0.5 + _random.NextDouble() * 4.5, 3); // 0.5-5.0A
                    break;
                case ChannelStatus.Discharge:
                    channel.Voltage = Math.Round(2.8 + _random.NextDouble() * 1.0, 3); // 2.8-3.8V
                    channel.Current = Math.Round(-5.0 + _random.NextDouble() * 4.5, 3); // -5.0 to -0.5A
                    break;
                case ChannelStatus.Rest:
                    channel.Voltage = Math.Round(3.6 + (_random.NextDouble() - 0.5) * 0.4, 3); // 3.4-3.8V
                    channel.Current = Math.Round((_random.NextDouble() - 0.5) * 0.02, 3); // -0.01 to 0.01A
                    break;
                case ChannelStatus.Idle:
                    channel.Voltage = 0;
                    channel.Current = 0;
                    break;
                default:
                    channel.Voltage = Math.Round(3.7 + (_random.NextDouble() - 0.5) * 0.2, 3);
                    channel.Current = Math.Round((_random.NextDouble() - 0.5) * 0.1, 3);
                    break;
            }

            channel.Power = Math.Round(channel.Voltage * Math.Abs(channel.Current), 2);
            channel.Capacity = Math.Round(state.AccumulatedCapacity, 2);
            channel.ChamberTemperature = Math.Round(25.0 + (_random.NextDouble() - 0.5) * 10.0, 1); // 20-30°C
            channel.StepTime = TimeSpan.FromSeconds(state.StepTimeSeconds);
            channel.TotalTime = TimeSpan.FromSeconds(state.TotalTimeSeconds);

            return channel;
        }

        private void UpdateChannelSimulationState(ChannelSimulationState state)
        {
            state.StepTimeSeconds++;
            state.TotalTimeSeconds++;

            // Simulate capacity accumulation
            if (state.Status == ChannelStatus.Charge || state.Status == ChannelStatus.Discharge)
            {
                state.AccumulatedCapacity += _random.NextDouble() * 0.01;
            }

            // Randomly change status occasionally
            if (_random.NextDouble() < 0.01) // 1% chance per update
            {
                var statuses = Enum.GetValues<ChannelStatus>();
                state.Status = statuses[_random.Next(statuses.Length)];

                var modes = Enum.GetValues<ChannelMode>();
                state.Mode = modes[_random.Next(modes.Length)];

                state.StepNo++;
                state.StepTimeSeconds = 0;

                if (state.StepNo > 10)
                {
                    state.CycleNo++;
                    state.StepNo = 1;
                }
            }
        }

        private AuxData GenerateAuxData(int sensorId)
        {
            return new AuxData
            {
                SensorId = sensorId,
                SensorName = $"{_auxSensorNames[_random.Next(_auxSensorNames.Count)]}_{sensorId}",
                Value = Math.Round(20.0 + _random.NextDouble() * 60.0, 2), // 20-80 range
                SafeUpperLimit = 75.0,
                SafeLowerLimit = 15.0
            };
        }

        private CANData GenerateCANData(int bmsId)
        {
            return new CANData
            {
                Name = $"{_canNames[_random.Next(_canNames.Count)]}_{bmsId}",
                Value = Math.Round(_random.NextDouble() * 100.0, 2),
                BmsId = bmsId,
                Max = 100.0,
                Min = 0.0
            };
        }

        private LINData GenerateLINData(int bmsId)
        {
            return new LINData
            {
                Name = $"{_linNames[_random.Next(_linNames.Count)]}_{bmsId}",
                Value = Math.Round(_random.NextDouble() * 50.0, 2),
                BmsId = bmsId,
                Max = 50.0,
                Min = 0.0
            };
        }

        private AlarmData GenerateAlarmData()
        {
            var severities = Enum.GetValues<AlarmSeverity>();
            var severity = severities[_random.Next(severities.Length)];

            return new AlarmData
            {
                Id = _random.Next(1000, 9999),
                Name = $"ALARM_{_random.Next(100, 999)}",
                Description = GetAlarmDescription(severity),
                Timestamp = DateTime.UtcNow,
                Severity = severity
            };
        }

        private string GetAlarmDescription(AlarmSeverity severity)
        {
            return severity switch
            {
                AlarmSeverity.Critical => "Critical system failure detected",
                AlarmSeverity.Error => "Error condition requires attention",
                AlarmSeverity.Warning => "Warning: Parameter out of normal range",
                AlarmSeverity.Info => "Information: System status update",
                _ => "Unknown alarm condition"
            };
        }

        private class ChannelSimulationState
        {
            public ChannelStatus Status { get; set; } = ChannelStatus.Idle;
            public ChannelMode Mode { get; set; } = ChannelMode.Rest;
            public int CycleNo { get; set; } = 1;
            public int StepNo { get; set; } = 1;
            public int CyclerLoop { get; set; } = 0;
            public double AccumulatedCapacity { get; set; } = 0.0;
            public int StepTimeSeconds { get; set; } = 0;
            public int TotalTimeSeconds { get; set; } = 0;
        }
    }
}