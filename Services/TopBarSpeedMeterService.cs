using System;
using System.IO;
using System.Text.Json;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;

namespace DataSense.Services;

public interface ITopBarSpeedMeterService : IDisposable
{
    void Start();
    Task<bool> RefreshConfigurationAsync();
}

public interface IGnomeExtensionController
{
    Task SetEnabledAsync(bool enabled);
}

public sealed class GnomeExtensionController : IGnomeExtensionController
{
    private const string ExtensionUuid = "datasense-speed-meter@dulmina.dev";

    public async Task SetEnabledAsync(bool enabled)
    {
        await ProcessExecutionHelper.ExecuteAsync(
            "gnome-extensions",
            new[] { enabled ? "enable" : "disable", ExtensionUuid },
            timeoutMs: 3000);
    }
}

public sealed class TopBarSpeedMeterService : ITopBarSpeedMeterService
{
    private readonly INetworkMonitorWorker _monitor;
    private readonly INetworkUsageRepository _repository;
    private readonly IThemeService _themeService;
    private readonly IGnomeExtensionController _extensionController;
    private readonly string _contractPath;
    private bool _started;
    private bool _enabled;
    private int _refreshIntervalMs = 1000;
    private bool _showDownload = true;
    private bool _showUpload = true;
    private bool _showIcons = true;
    private bool _compactMode = true;
    private string _units = "Auto";
    private string _precision = "1 decimal";
    private string _colorMode = "Theme colors";
    private string _singleColor = "#d8e4f2";
    private string _downloadColor = "#62d2a2";
    private string _uploadColor = "#f4b860";
    private string _size = "Medium";
    private string _fontWeight = "Normal";
    private string _position = "Right area";
    private string _clickAction = "Open Dashboard";
    private bool _showDetailsOnHover = true;
    private DateTime _lastWrite = DateTime.MinValue;

    public TopBarSpeedMeterService(
        INetworkMonitorWorker monitor,
        INetworkUsageRepository repository,
        IThemeService themeService,
        IGnomeExtensionController? extensionController = null)
    {
        _monitor = monitor;
        _repository = repository;
        _themeService = themeService;
        _extensionController = extensionController ?? new GnomeExtensionController();
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        _contractPath = Path.Combine(
            string.IsNullOrWhiteSpace(runtimeDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataSense")
                : Path.Combine(runtimeDirectory, "DataSense"),
            "speed-meter.json");
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _monitor.NetworkUsageUpdated += OnNetworkUsageUpdated;
        Publish();
        _ = LoadConfigurationAsync();
    }

    public async Task<bool> RefreshConfigurationAsync()
    {
        _enabled = await ReadBoolAsync("ShowNetworkSpeedMeter", false);
        _showDownload = await ReadBoolAsync("ShowMeterDownload", true);
        _showUpload = await ReadBoolAsync("ShowMeterUpload", true);
        _showIcons = await ReadBoolAsync("ShowMeterIcons", true);
        _compactMode = await ReadBoolAsync("MeterCompactMode", true);
        _units = await ReadStringAsync("MeterUnits", "Auto");
        _precision = await ReadStringAsync("MeterPrecision", "1 decimal");
        _colorMode = await ReadStringAsync("MeterColorMode", "Theme colors");
        _singleColor = await ReadStringAsync("MeterSingleColor", "#d8e4f2");
        _downloadColor = await ReadStringAsync("MeterDownloadColor", "#62d2a2");
        _uploadColor = await ReadStringAsync("MeterUploadColor", "#f4b860");
        _size = await ReadStringAsync("MeterSize", "Medium");
        _fontWeight = await ReadStringAsync("MeterFontWeight", "Normal");
        _position = await ReadStringAsync("MeterPosition", "Right area");
        _clickAction = await ReadStringAsync("MeterClickAction", "Open Dashboard");
        _showDetailsOnHover = await ReadBoolAsync("MeterShowDetailsOnHover", true);
        _refreshIntervalMs = ParseRefreshRate(await ReadStringAsync("MeterRefreshRate", "1 second"));
        Publish();
        try
        {
            await _extensionController.SetEnabledAsync(_enabled);
            return true;
        }
        catch
        {
            // Contract synchronization remains usable when GNOME is unavailable.
            return false;
        }
    }

    private async System.Threading.Tasks.Task LoadConfigurationAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await _repository.InitializeAsync();
                _enabled = await ReadBoolAsync("ShowNetworkSpeedMeter", false);
                _showDownload = await ReadBoolAsync("ShowMeterDownload", true);
                _showUpload = await ReadBoolAsync("ShowMeterUpload", true);
                _showIcons = await ReadBoolAsync("ShowMeterIcons", true);
                _compactMode = await ReadBoolAsync("MeterCompactMode", true);
                _units = await ReadStringAsync("MeterUnits", "Auto");
                _precision = await ReadStringAsync("MeterPrecision", "1 decimal");
                _colorMode = await ReadStringAsync("MeterColorMode", "Theme colors");
                _singleColor = await ReadStringAsync("MeterSingleColor", "#d8e4f2");
                _downloadColor = await ReadStringAsync("MeterDownloadColor", "#62d2a2");
                _uploadColor = await ReadStringAsync("MeterUploadColor", "#f4b860");
                _size = await ReadStringAsync("MeterSize", "Medium");
                _fontWeight = await ReadStringAsync("MeterFontWeight", "Normal");
                _position = await ReadStringAsync("MeterPosition", "Right area");
                _clickAction = await ReadStringAsync("MeterClickAction", "Open Dashboard");
                _showDetailsOnHover = await ReadBoolAsync("MeterShowDetailsOnHover", true);
                _refreshIntervalMs = ParseRefreshRate(await ReadStringAsync("MeterRefreshRate", "1 second"));
                Publish();
                try
                {
                    await _extensionController.SetEnabledAsync(_enabled);
                }
                catch
                {
                    // Contract synchronization remains usable when GNOME is unavailable.
                }
                return;
            }
            catch when (attempt < 19)
            {
                await System.Threading.Tasks.Task.Delay(250);
            }
        }
    }

    private void OnNetworkUsageUpdated(NetworkUsage _)
    {
        if (!_enabled) return;
        if ((DateTime.UtcNow - _lastWrite).TotalMilliseconds < _refreshIntervalMs) return;
        Publish();
    }

    private void Publish()
    {
        try
        {
            var payload = new
            {
                enabled = _enabled,
                download = Math.Max(0, _monitor.DownloadSpeed),
                upload = Math.Max(0, _monitor.UploadSpeed),
                totalDownloaded = Math.Max(0, _monitor.TotalBytesDownloaded),
                totalUploaded = Math.Max(0, _monitor.TotalBytesUploaded),
                activeInterface = _monitor.ActiveInterface ?? "None",
                showDownload = _showDownload,
                showUpload = _showUpload,
                showIcons = _showIcons,
                compactMode = _compactMode,
                units = _units,
                precision = _precision,
                colorMode = _colorMode,
                singleColor = _singleColor,
                downloadColor = _downloadColor,
                uploadColor = _uploadColor,
                refreshIntervalMs = _refreshIntervalMs,
                size = _size,
                fontWeight = _fontWeight,
                position = _position,
                clickAction = _clickAction,
                showDetailsOnHover = _showDetailsOnHover,
                themeColor = ThemeService.GetThemeDefinition(_themeService.CurrentThemeId).AccentPrimary,
                themeDownloadColor = ThemeService.GetThemeDefinition(_themeService.CurrentThemeId).Download,
                themeUploadColor = ThemeService.GetThemeDefinition(_themeService.CurrentThemeId).Upload,
                updatedUtc = DateTime.UtcNow
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_contractPath)!);
            var temporaryPath = _contractPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload));
            File.Move(temporaryPath, _contractPath, overwrite: true);
            _lastWrite = DateTime.UtcNow;
        }
        catch { }
    }

    private async System.Threading.Tasks.Task<bool> ReadBoolAsync(string key, bool fallback)
    {
        var value = await _repository.GetSettingAsync(key);
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    private async System.Threading.Tasks.Task<string> ReadStringAsync(string key, string fallback)
    {
        var value = await _repository.GetSettingAsync(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ParseRefreshRate(string value) => value switch
    {
        "250 ms" => 250,
        "500 ms" => 500,
        "2 seconds" => 2000,
        "5 seconds" => 5000,
        _ => 1000
    };

    public void Dispose()
    {
        if (!_started) return;
        _monitor.NetworkUsageUpdated -= OnNetworkUsageUpdated;
        _started = false;
        if (_enabled)
        {
            try { if (File.Exists(_contractPath)) File.Delete(_contractPath); } catch { }
        }
    }
}
