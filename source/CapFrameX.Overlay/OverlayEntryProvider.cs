﻿using CapFrameX.Contracts.Configuration;
using CapFrameX.Contracts.Overlay;
using CapFrameX.Contracts.Sensor;
using CapFrameX.EventAggregation.Messages;
using CapFrameX.Extensions;
using CapFrameX.PresentMonInterface;
using Newtonsoft.Json;
using Prism.Events;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace CapFrameX.Overlay
{
    public class OverlayEntryProvider : IOverlayEntryProvider
    {
        private static readonly string OVERLAY_CONFIG_FOLDER
            = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    @"CapFrameX\OverlayConfiguration\");

        private readonly ISensorService _sensorService;
        private readonly IAppConfiguration _appConfiguration;
        private readonly IEventAggregator _eventAggregator;
        private readonly ConcurrentDictionary<string, IOverlayEntry> _identifierOverlayEntryDict
             = new ConcurrentDictionary<string, IOverlayEntry>();
        private readonly TaskCompletionSource<bool> _taskCompletionSource
            = new TaskCompletionSource<bool>();
        private BlockingCollection<IOverlayEntry> _overlayEntries;

        public OverlayEntryProvider(ISensorService sensorService, IAppConfiguration appConfiguration, IEventAggregator eventAggregator)
        {
            _sensorService = sensorService;
            _appConfiguration = appConfiguration;
            _eventAggregator = eventAggregator;

            _ = Task.Run(async () => await LoadOrSetDefault())
                .ContinueWith(task => _taskCompletionSource.SetResult(true));

            SubscribeToOptionPopupClosed();
        }

        public async Task<IOverlayEntry[]> GetOverlayEntries()
        {
            await _taskCompletionSource.Task;
            UpdateSensorData();
            return _overlayEntries.ToArray();
        }

        public IOverlayEntry GetOverlayEntry(string identifier)
        {
            _identifierOverlayEntryDict.TryGetValue(identifier, out IOverlayEntry entry);
            return entry;
        }

        public void MoveEntry(int sourceIndex, int targetIndex)
        {
            _overlayEntries.Move(sourceIndex, targetIndex);
        }

        public bool SaveOverlayEntriesToJson()
        {
            try
            {
                var persistence = new OverlayEntryPersistence()
                {
                    OverlayEntries = _overlayEntries.Select(entry => entry as OverlayEntryWrapper).ToList()
                };

                var json = JsonConvert.SerializeObject(persistence);

                if (!Directory.Exists(OVERLAY_CONFIG_FOLDER))
                    Directory.CreateDirectory(OVERLAY_CONFIG_FOLDER);

                File.WriteAllText(GetConfigurationFileName(), json);

                return true;
            }
            catch { return false; }
        }

        public async Task SwitchConfigurationTo(int index)
        {
            SetConfigurationFileName(index);
            await LoadOrSetDefault();
        }

        public async Task<IEnumerable<IOverlayEntry>> GetDefaultOverlayEntries()
        {
            _overlayEntries = await SetOverlayEntryDefaults();
            _identifierOverlayEntryDict.Clear();
            foreach (var entry in _overlayEntries)
            {
                entry.OverlayEntryProvider = this;
                _identifierOverlayEntryDict.TryAdd(entry.Identifier, entry);
            }

            return _overlayEntries.ToList();
        }

        private async Task LoadOrSetDefault()
        {
            try
            {
                _overlayEntries = await InitializeOverlayEntryDictionary();
            }
            catch
            {
                _overlayEntries = await SetOverlayEntryDefaults();
            }
            _identifierOverlayEntryDict.Clear();
            foreach (var entry in _overlayEntries)
            {
                entry.OverlayEntryProvider = this;
                _identifierOverlayEntryDict.TryAdd(entry.Identifier, entry);
            }
            CheckCustomSystemInfo();
            CheckOSVersion();
        }

        private IObservable<BlockingCollection<IOverlayEntry>> InitializeOverlayEntryDictionary()
        {
            string json = File.ReadAllText(GetConfigurationFileName());
            var overlayEntriesFromJson = JsonConvert.DeserializeObject<OverlayEntryPersistence>(json)
                .OverlayEntries.ToBlockingCollection<IOverlayEntry>();

            return _sensorService.OnDictionaryUpdated
                .Take(1)
                .Select(sensorOverlayEntries =>
                {
                    var sensorOverlayEntryIdentfiers = sensorOverlayEntries
                        .Select(entry => entry.Identifier)
                        .ToList();

                    var adjustedOverlayEntries = new List<IOverlayEntry>(overlayEntriesFromJson);
                    var adjustedOverlayEntryIdentfiers = adjustedOverlayEntries
                        .Select(entry => entry.Identifier)
                        .ToList();

                    foreach (var entry in overlayEntriesFromJson.Where(x => x.OverlayEntryType != EOverlayEntryType.CX))
                    {
                        if (!sensorOverlayEntryIdentfiers.Contains(entry.Identifier))
                            adjustedOverlayEntries.Remove(entry);
                    }

                    foreach (var entry in sensorOverlayEntries)
                    {
                        if (!adjustedOverlayEntryIdentfiers.Contains(entry.Identifier))
                        {
                            adjustedOverlayEntries.Add(entry);
                        }
                    }
                    return adjustedOverlayEntries.ToBlockingCollection();
                });
        }

        private void CheckOSVersion()
        {
            _identifierOverlayEntryDict.TryGetValue("OS", out IOverlayEntry entry);

            if (entry != null)
            {
                entry.Value = SystemInfo.GetOSVersion();
            }
        }

        private void CheckCustomSystemInfo()
        {
            _identifierOverlayEntryDict.TryGetValue("CustomCPU", out IOverlayEntry customCPUEntry);

            if (customCPUEntry != null)
            {
                customCPUEntry.Value =
                    _appConfiguration.HardwareInfoSource == "Auto" ? SystemInfo.GetProcessorName()
                    : _appConfiguration.CustomCpuDescription;
            }

            _identifierOverlayEntryDict.TryGetValue("CustomGPU", out IOverlayEntry customGPUEntry);

            if (customGPUEntry != null)
            {
                customGPUEntry.Value =
                    _appConfiguration.HardwareInfoSource == "Auto" ? SystemInfo.GetGraphicCardName()
                    : _appConfiguration.CustomGpuDescription;
            }

            _identifierOverlayEntryDict.TryGetValue("Mainboard", out IOverlayEntry mainboardEntry);

            if (mainboardEntry != null)
            {
                mainboardEntry.Value = SystemInfo.GetMotherboardName();
            }

            _identifierOverlayEntryDict.TryGetValue("CustomRAM", out IOverlayEntry customRAMEntry); ;

            if (customRAMEntry != null)
            {
                customRAMEntry.Value =
                    _appConfiguration.HardwareInfoSource == "Auto" ? SystemInfo.GetSystemRAMInfoName()
                    : _appConfiguration.CustomRamDescription;
            }
        }

        private IObservable<BlockingCollection<IOverlayEntry>> SetOverlayEntryDefaults()
        {
            var overlayEntries = OverlayUtils.GetOverlayEntryDefaults()
                    .Select(item => item as IOverlayEntry).ToBlockingCollection();

            // Sensor data
            return _sensorService.OnDictionaryUpdated
                .Take(1)
                .Select(sensorOverlayEntries =>
                {
                    sensorOverlayEntries.ForEach(sensor => overlayEntries.TryAdd(sensor));
                    return overlayEntries;
                });
        }

        private void UpdateSensorData()
        {
            foreach (var entry in _overlayEntries.Where(x => !(x.OverlayEntryType == EOverlayEntryType.CX)))
            {
                var sensorEntry = _sensorService.GetSensorOverlayEntry(entry.Identifier);
                entry.Value = sensorEntry.Value;
            }
        }

        private string GetConfigurationFileName()
        {
            return Path.Combine(OVERLAY_CONFIG_FOLDER, $"OverlayEntryConfiguration_" +
                $"{_appConfiguration.OverlayEntryConfigurationFile}.json");
        }

        private void SetConfigurationFileName(int index)
        {
            _appConfiguration.OverlayEntryConfigurationFile = index;
        }

        private void SubscribeToOptionPopupClosed()
        {
            _eventAggregator.GetEvent<PubSubEvent<ViewMessages.OptionPopupClosed>>()
                            .Subscribe(_ =>
                            {
                                CheckCustomSystemInfo();
                            });
        }
    }
}
