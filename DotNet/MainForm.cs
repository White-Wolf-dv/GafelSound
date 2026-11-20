using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GafelSound;

public class MainForm : Form
{
    private readonly MultiAudioEngine _engine;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    public MainForm(MultiAudioEngine engine)
    {
        _engine = engine;
        Text = "GafelSound";
        Size = new Size(1240, 820);
        MinimumSize = new Size(640, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var icoPath = Path.Combine(AppContext.BaseDirectory, "Gafelsound.ico");
        if (File.Exists(icoPath)) Icon = new Icon(icoPath);

        Controls.Add(_webView);
        Load += async (_, _) => await InitWebViewAsync();
        FormClosing += (_, _) => _engine.StopEngine();
    }

    private async Task InitWebViewAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        const string bridgeScript = """
            (() => {
              let reqId = 0;
              const pending = new Map();
              window.chrome.webview.addEventListener('message', e => {
                const { id, result } = e.data;
                if (pending.has(id)) {
                  pending.get(id)(result);
                  pending.delete(id);
                }
              });
              const call = (method, args = []) => new Promise(resolve => {
                const id = ++reqId;
                pending.set(id, resolve);
                window.chrome.webview.postMessage({ id, method, args });
              });
              window.pywebview = {
                api: new Proxy({}, {
                  get: (_, prop) => (...args) => call(prop, args)
                })
              };
              window.addEventListener('DOMContentLoaded', () => {
                window.dispatchEvent(new Event('pywebviewready'));
              });
            })();
            """;

        await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(bridgeScript);

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "ui.html");
        _webView.Source = new Uri(htmlPath);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        int id = root.GetProperty("id").GetInt32();
        string method = root.GetProperty("method").GetString() ?? "";
        var args = root.GetProperty("args");

        object? result = method switch
        {
            "get_initial_state" => new
            {
                devices = _engine.GetDevices(),
                apps = _engine.GetActiveAudioApps(),
                settings = _engine.Settings,
                engine_running = _engine.Running
            },
            "refresh_devices" => _engine.ScanDevices(),
            "refresh_apps" => _engine.GetActiveAudioApps(),
            "set_volume" => Execute(() => _engine.SetDeviceVolume(args[0].GetString()!, args[1].GetInt32())),
            "toggle_mute" => Execute(() => _engine.SetDeviceMute(args[0].GetString()!, args[1].GetBoolean())),
            "toggle_enable" => Execute(() => _engine.SetDeviceEnabled(args[0].GetString()!, args[1].GetBoolean())),
            "update_setting" => UpdateSetting(args[0].GetString()!, args[1]),
            "open_windows_mixer" => Execute(() => Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true })),
            _ => new { ok = false }
        };

        var responseJson = JsonSerializer.Serialize(new { id, result });
        _webView.CoreWebView2.PostWebMessageAsJson(responseJson);
    }

    private static object Execute(Action action)
    {
        action();
        return new { ok = true };
    }

    private object UpdateSetting(string key, JsonElement value)
    {
        switch (key)
        {
            case "theme":
                _engine.Settings.Theme = value.GetString() ?? "light";
                break;
            case "buffer_size":
                _engine.Settings.BufferSize = value.GetInt32();
                _engine.RestartEngine();
                break;
            case "start_with_windows":
                _engine.Settings.StartWithWindows = value.GetBoolean();
                _engine.SetStartupRegistry(_engine.Settings.StartWithWindows);
                break;
            case "minimize_to_tray":
                _engine.Settings.MinimizeToTray = value.GetBoolean();
                break;
            case "start_minimized":
                _engine.Settings.StartMinimized = value.GetBoolean();
                break;
        }
        _engine.SaveConfig();
        return new { ok = true };
    }
}
