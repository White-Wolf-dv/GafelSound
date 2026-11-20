using System.Text.Json.Serialization;

namespace GafelSound;

public class AudioDeviceInfo
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("desc")] public string Desc { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "Speakers";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "◖))";
    [JsonPropertyName("status")] public string Status { get; set; } = "Connected";
    [JsonPropertyName("is_default")] public bool IsDefault { get; set; }
    [JsonPropertyName("sample_rate")] public int SampleRate { get; set; } = 48000;
    [JsonPropertyName("channels")] public int Channels { get; set; } = 2;
    [JsonPropertyName("format")] public string Format { get; set; } = "48 kHz · Stereo";
    [JsonPropertyName("volume")] public int Volume { get; set; } = 75;
    [JsonPropertyName("muted")] public bool Muted { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public class AudioAppInfo
{
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("process")] public string Process { get; set; } = "";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "▶";
    [JsonPropertyName("status")] public string Status { get; set; } = "Application audio · Active Session";
}

public class AppSettings
{
    [JsonPropertyName("theme")] public string Theme { get; set; } = "light";
    [JsonPropertyName("start_with_windows")] public bool StartWithWindows { get; set; }
    [JsonPropertyName("minimize_to_tray")] public bool MinimizeToTray { get; set; } = true;
    [JsonPropertyName("start_minimized")] public bool StartMinimized { get; set; }
    [JsonPropertyName("buffer_size")] public int BufferSize { get; set; } = 512;
}

public class ConfigWrapper
{
    [JsonPropertyName("settings")] public AppSettings Settings { get; set; } = new();
}
