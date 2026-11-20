using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GafelSound;

public sealed class MultiAudioEngine : IDisposable
{
    private const string ConfigFile = "gafelsound_config.json";
    private readonly Lock _lock = new();
    private readonly MMDeviceEnumerator _enumerator = new();

    private readonly Dictionary<string, AudioDeviceInfo> _devices = new();
    private readonly Dictionary<string, SecondaryOutput> _outputs = new();

    private WasapiLoopbackCapture? _loopback;
    private WaveFormat? _captureFormat;

    public bool Running { get; private set; }
    public AppSettings Settings { get; private set; } = new();

    public MultiAudioEngine()
    {
        LoadConfig();
        ScanDevices();
    }

    public void LoadConfig()
    {
        if (!File.Exists(ConfigFile)) return;
        try
        {
            var json = File.ReadAllText(ConfigFile);
            var wrapper = JsonSerializer.Deserialize<ConfigWrapper>(json);
            if (wrapper?.Settings != null)
                Settings = wrapper.Settings;
        }
        catch { }
    }

    public void SaveConfig()
    {
        try
        {
            var json = JsonSerializer.Serialize(new ConfigWrapper { Settings = Settings },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch { }
    }

    private static (string Type, string Icon) ClassifyDevice(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("bluetooth") || n.Contains("hands-free") || n.Contains("airpods") || n.Contains("headset"))
            return ("Bluetooth", "♧");
        if (n.Contains("headphone"))
            return ("Headphones", "♧");
        if (n.Contains("hdmi") || n.Contains("nvidia") || n.Contains("amd") || n.Contains("display") || n.Contains("monitor"))
            return ("HDMI", "▣");
        return ("Speakers", "◖))");
    }

    public List<AudioDeviceInfo> ScanDevices()
    {
        lock (_lock)
        {
            var oldStates = _devices.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.Enabled, kv.Value.Volume, kv.Value.Muted));

            _devices.Clear();

            string? defaultId = null;
            try
            {
                using var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = def.ID;
            }
            catch { }

            foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (dev)
                {
                    var id = dev.ID;
                    var isDefault = id == defaultId;
                    var (dtype, icon) = ClassifyDevice(dev.FriendlyName);
                    var fmt = dev.AudioClient.MixFormat;
                    int sr = fmt?.SampleRate ?? 48000;
                    int ch = Math.Min(2, fmt?.Channels ?? 2);

                    var hasOld = oldStates.TryGetValue(id, out var prev);
                    _devices[id] = new AudioDeviceInfo
                    {
                        Id = id,
                        Name = dev.FriendlyName,
                        Desc = $"WASAPI · {dtype} Endpoint",
                        Type = dtype,
                        Icon = icon,
                        Status = "Connected",
                        IsDefault = isDefault,
                        SampleRate = sr,
                        Channels = ch,
                        Format = $"{sr / 1000} kHz · {(ch == 2 ? "Stereo" : "Mono")}",
                        Enabled = hasOld ? prev.Enabled : isDefault,
                        Volume = hasOld ? prev.Volume : 75,
                        Muted = hasOld && prev.Muted
                    };
                }
            }

            return _devices.Values.ToList();
        }
    }

    public List<AudioDeviceInfo> GetDevices()
    {
        lock (_lock) return _devices.Values.ToList();
    }

    public void StartEngine()
    {
        if (Running) return;
        try
        {
            _loopback = new WasapiLoopbackCapture();
            _captureFormat = _loopback.WaveFormat;
            _loopback.DataAvailable += OnLoopbackDataAvailable;
            _loopback.StartRecording();
            Running = true;
            SyncSecondaryOutputs();
        }
        catch
        {
            Running = false;
        }
    }

    public void StopEngine()
    {
        Running = false;
        if (_loopback != null)
        {
            _loopback.DataAvailable -= OnLoopbackDataAvailable;
            try { _loopback.StopRecording(); } catch { }
            _loopback.Dispose();
            _loopback = null;
        }
        CloseAllOutputs();
    }

    public void RestartEngine()
    {
        StopEngine();
        StartEngine();
    }

    private void CloseAllOutputs()
    {
        lock (_lock)
        {
            foreach (var o in _outputs.Values) o.Dispose();
            _outputs.Clear();
        }
    }

    private void SyncSecondaryOutputs()
    {
        if (_captureFormat == null) return;
        int latencyMs = Math.Clamp((int)(Settings.BufferSize * 1000.0 / 48000.0) * 2, 10, 60);

        lock (_lock)
        {
            foreach (var dev in _devices.Values)
            {
                if (dev.IsDefault || !dev.Enabled)
                {
                    if (_outputs.Remove(dev.Id, out var existing))
                        existing.Dispose();
                    continue;
                }

                if (!_outputs.ContainsKey(dev.Id))
                {
                    try
                    {
                        var mmDev = _enumerator.GetDevice(dev.Id);
                        var output = new SecondaryOutput(mmDev, _captureFormat, latencyMs);
                        output.SetVolume(dev.Muted ? 0f : dev.Volume / 100f);
                        _outputs[dev.Id] = output;
                    }
                    catch { }
                }
            }
        }
    }

    private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!Running || e.BytesRecorded == 0) return;

        lock (_lock)
        {
            foreach (var output in _outputs.Values)
            {
                output.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    public void SetDeviceVolume(string devId, int volume)
    {
        int vol = Math.Clamp(volume, 0, 100);
        lock (_lock)
        {
            if (!_devices.TryGetValue(devId, out var dev)) return;
            dev.Volume = vol;

            if (dev.IsDefault)
                SetWindowsDefaultVolume(vol / 100f, null);
            else if (_outputs.TryGetValue(devId, out var output))
                output.SetVolume(dev.Muted ? 0f : vol / 100f);
        }
    }

    public void SetDeviceMute(string devId, bool muted)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(devId, out var dev)) return;
            dev.Muted = muted;

            if (dev.IsDefault)
                SetWindowsDefaultVolume(null, muted);
            else if (_outputs.TryGetValue(devId, out var output))
                output.SetVolume(muted ? 0f : dev.Volume / 100f);
        }
    }

    public void SetDeviceEnabled(string devId, bool enabled)
    {
        lock (_lock)
        {
            if (!_devices.TryGetValue(devId, out var dev)) return;
            dev.Enabled = enabled;

            if (dev.IsDefault)
                SetWindowsDefaultVolume(null, !enabled);
        }
        SyncSecondaryOutputs();
    }

    private void SetWindowsDefaultVolume(float? scalar, bool? mute)
    {
        try
        {
            using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (scalar.HasValue) dev.AudioEndpointVolume.MasterVolumeLevelScalar = scalar.Value;
            if (mute.HasValue) dev.AudioEndpointVolume.Mute = mute.Value;
        }
        catch { }
    }

    public List<AudioAppInfo> GetActiveAudioApps()
    {
        var apps = new List<AudioAppInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var dev = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = dev.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                try
                {
                    using var proc = Process.GetProcessById(pid);
                    var pname = proc.ProcessName;
                    if (string.IsNullOrEmpty(pname) || !seen.Add(pname)) continue;

                    var lower = pname.ToLowerInvariant();
                    string icon = (lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("firefox") || lower.Contains("brave")) ? "🌐"
                                : (lower.Contains("spotify") || lower.Contains("music") || lower.Contains("vlc") || lower.Contains("wmplayer")) ? "♫"
                                : (lower.Contains("discord") || lower.Contains("telegram") || lower.Contains("teams") || lower.Contains("zoom")) ? "💬"
                                : "▶";

                    apps.Add(new AudioAppInfo
                    {
                        Pid = pid,
                        Name = char.ToUpper(pname[0]) + pname[1..],
                        Process = pname + ".exe",
                        Icon = icon
                    });
                }
                catch { }
            }
        }
        catch { }

        return apps;
    }

    public void SetStartupRegistry(bool enable)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string appName = "GafelSoundAudioManager";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue(appName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(appName, throwOnMissingValue: false);
        }
        catch { }
    }

    public void Dispose()
    {
        StopEngine();
        _enumerator.Dispose();
    }

    private sealed class SecondaryOutput : IDisposable
    {
        private readonly MMDevice _device;
        private readonly BufferedWaveProvider _buffer;
        private readonly VolumeSampleProvider _volumeProvider;
        private readonly WasapiOut _wasapiOut;

        public SecondaryOutput(MMDevice device, WaveFormat captureFormat, int latencyMs)
        {
            _device = device;
            _buffer = new BufferedWaveProvider(captureFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromMilliseconds(200)
            };

            ISampleProvider pipeline = _buffer.ToSampleProvider();

            var targetFormat = device.AudioClient.MixFormat;
            if (targetFormat != null)
            {
                if (pipeline.WaveFormat.Channels == 2 && targetFormat.Channels == 1)
                    pipeline = new StereoToMonoSampleProvider(pipeline);
                else if (pipeline.WaveFormat.Channels == 1 && targetFormat.Channels == 2)
                    pipeline = new MonoToStereoSampleProvider(pipeline);

                if (pipeline.WaveFormat.SampleRate != targetFormat.SampleRate)
                    pipeline = new WdlResamplingSampleProvider(pipeline, targetFormat.SampleRate);
            }

            _volumeProvider = new VolumeSampleProvider(pipeline) { Volume = 0.75f };

            _wasapiOut = new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs);
            _wasapiOut.Init(_volumeProvider);
            _wasapiOut.Play();
        }

        public void AddSamples(byte[] data, int offset, int count) => _buffer.AddSamples(data, offset, count);
        public void SetVolume(float volume) => _volumeProvider.Volume = Math.Clamp(volume, 0f, 1f);

        public void Dispose()
        {
            try { _wasapiOut.Stop(); } catch { }
            _wasapiOut.Dispose();
            _device.Dispose();
        }
    }
}
