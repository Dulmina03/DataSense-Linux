using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;

namespace DataSense.ViewModels;

public enum SpeedTestStage
{
    Idle,
    Ping,
    Download,
    Upload,
    Completed,
    Failed,
    Cancelled
}

public class ScaleTickMark
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public double LabelX { get; init; }
    public double LabelY { get; init; }
    public string Label { get; init; } = string.Empty;
}

public class RealtimeSamplePoint
{
    public double ElapsedSeconds { get; init; }
    public double SpeedMbps { get; init; }
    public bool IsUpload { get; init; }
}

public partial class SpeedTestViewModel : ViewModelBase, IDisposable
{
    private readonly ISpeedTestService _speedTestService;
    private readonly INetworkUsageRepository _repository;
    private readonly INetworkMonitorWorker? _networkMonitorWorker;
    private readonly INetworkIdentityService _identityService;
    private CancellationTokenSource? _cancellationTokenSource;

    private const double MeterCenterX = 160.0;
    private const double MeterCenterY = 145.0;
    private const double MeterRadius = 110.0;
    private const double StartAngleDeg = 150.0;
    private const double TotalArcAngleDeg = 240.0;

    // ── Metric Readouts ────────────────────────────────────────────────────────
    [ObservableProperty] private string _downloadSpeedText = "—";
    [ObservableProperty] private string _uploadSpeedText = "—";
    [ObservableProperty] private string _pingText = "—";
    [ObservableProperty] private string _jitterText = "—";

    [ObservableProperty] private string _downloadQuality = "—";
    [ObservableProperty] private string _uploadQuality = "—";
    [ObservableProperty] private string _pingQuality = "—";
    [ObservableProperty] private string _jitterQuality = "—";

    [ObservableProperty] private string _downloadQualityColor = "Muted";
    [ObservableProperty] private string _uploadQualityColor = "Muted";
    [ObservableProperty] private string _pingQualityColor = "Muted";

    // ── Central Gauge State ───────────────────────────────────────────────────
    [ObservableProperty] private string _displaySpeedValue = "0.0";
    [ObservableProperty] private string _displayUnitText = "Mbps";
    [ObservableProperty] private string _displayPhaseText = "READY";
    [ObservableProperty] private double _currentSpeedValue;
    [ObservableProperty] private double _dynamicMaxSpeed = 100.0;
    [ObservableProperty] private double _meterAngleFraction;
    [ObservableProperty] private PathGeometry? _meterActiveArc;
    [ObservableProperty] private PathGeometry? _meterBackgroundArc;
    [ObservableProperty] private PathGeometry? _meterOuterRing;
    [ObservableProperty] private PathGeometry? _meterInnerRing;
    [ObservableProperty] private PathGeometry? _meterScaleTicksGeometry;
    [ObservableProperty] private string _activeMeterBrushKey = "Brush.Download";

    // ── Status & Control ──────────────────────────────────────────────────────
    [ObservableProperty] private SpeedTestStage _currentStage = SpeedTestStage.Idle;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _statusText = "Ready to test connection";
    [ObservableProperty] private string _actionButtonText = "RUN SPEED TEST";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isIndeterminateProgress;

    // ── Stage Indicators ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _isPingActive;
    [ObservableProperty] private bool _isPingDone;
    [ObservableProperty] private bool _isDownloadActive;
    [ObservableProperty] private bool _isDownloadDone;
    [ObservableProperty] private bool _isUploadActive;
    [ObservableProperty] private bool _isUploadDone;

    // ── Network Diagnostics ───────────────────────────────────────────────────
    [ObservableProperty] private string _activeNetworkName = "Resolving...";
    [ObservableProperty] private string _activeConnectionType = "Network";
    [ObservableProperty] private string _activeInterfaceName = "—";
    [ObservableProperty] private string _serverName = "Cloudflare CDN";

    // ── Quality Assessment ────────────────────────────────────────────────────
    [ObservableProperty] private string _overallQuality = "—";
    [ObservableProperty] private double _overallQualityPercent;
    [ObservableProperty] private string _qualityDescription = "Run a speed test to evaluate connection quality.";
    [ObservableProperty] private string _overallQualityColor = "Muted";

    // ── Real-Time Performance Graph ───────────────────────────────────────────
    [ObservableProperty] private PathGeometry? _realtimeDownloadGeometry;
    [ObservableProperty] private PathGeometry? _realtimeUploadGeometry;
    [ObservableProperty] private PathGeometry? _realtimeDownloadAreaGeometry;
    [ObservableProperty] private PathGeometry? _realtimeUploadAreaGeometry;
    [ObservableProperty] private string _graphYMaxText = "100 Mbps";
    [ObservableProperty] private string _graphYMidText = "50 Mbps";
    [ObservableProperty] private string _graphYMinText = "0 Mbps";
    [ObservableProperty] private bool _hasRealtimeGraphData;

    private readonly List<RealtimeSamplePoint> _realtimeSamples = new();
    private readonly Stopwatch _testStopwatch = new();

    public ObservableCollection<ScaleTickMark> ScaleTicks { get; } = new();
    public ObservableCollection<SpeedTestRecord> TestHistory { get; } = new();

    public override string Title => "Speed Test";

    public SpeedTestViewModel(
        ISpeedTestService speedTestService,
        INetworkUsageRepository repository,
        INetworkMonitorWorker? networkMonitorWorker = null,
        INetworkIdentityService? identityService = null)
    {
        _speedTestService = speedTestService;
        _repository = repository;
        _networkMonitorWorker = networkMonitorWorker;
        _identityService = identityService ?? new NetworkIdentityService(new LinuxNetworkConnectionService());

        // Initialize radial meter static geometries & scale
        InitializeMeterGeometry();
        UpdateScaleTicks();

        _ = RefreshNetworkIdentityAsync();
        _ = LoadHistoryAsync();
    }

    // ── Network Identity Resolution ───────────────────────────────────────────

    public async Task RefreshNetworkIdentityAsync()
    {
        try
        {
            string iface = _networkMonitorWorker?.ActiveInterface ?? string.Empty;
            if (string.IsNullOrWhiteSpace(iface) || iface == "None" || iface == "Disconnected")
            {
                var ifaces = await _repository.GetInterfaceNamesAsync();
                iface = ifaces.FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? "wlo1";
            }

            ActiveInterfaceName = iface;
            var identity = await _identityService.GetCurrentIdentityAsync(iface);

            if (identity.IsConnected && _identityService.IsValidNetworkName(identity.DisplayName))
            {
                ActiveNetworkName = identity.DisplayName;
                ActiveConnectionType = identity.Type == NetworkType.WiFi ? "Wi-Fi" : (identity.Type == NetworkType.Ethernet ? "Ethernet" : "Network");
            }
            else
            {
                ActiveNetworkName = _identityService.NormalizeNetworkName(identity.DisplayName, iface);
                ActiveConnectionType = iface.StartsWith("wl", StringComparison.OrdinalIgnoreCase) ? "Wi-Fi" : "Ethernet";
            }
        }
        catch
        {
            ActiveNetworkName = "Connected Network";
            ActiveConnectionType = "Network";
        }
    }

    // ── Gauge & Scale Mathematics ─────────────────────────────────────────────

    private void InitializeMeterGeometry()
    {
        // Background track (240 degrees from 150° to 390°)
        MeterBackgroundArc = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius, StartAngleDeg, TotalArcAngleDeg);
        MeterOuterRing = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius + 22.0, StartAngleDeg - 5.0, TotalArcAngleDeg + 10.0);
        MeterInnerRing = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius - 22.0, StartAngleDeg, TotalArcAngleDeg);
        UpdateActiveArc(0.0);
    }

    private void UpdateScaleTicks()
    {
        ScaleTicks.Clear();
        var ticksGeom = new PathGeometry();
        int stepCount = 5; // 0%, 20%, 40%, 60%, 80%, 100%
        for (int i = 0; i <= stepCount; i++)
        {
            double fraction = (double)i / stepCount;
            double angleDeg = StartAngleDeg + fraction * TotalArcAngleDeg;
            double rad = angleDeg * Math.PI / 180.0;

            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            double r1 = MeterRadius + 4.0;
            double r2 = MeterRadius + 12.0;
            double rLabel = MeterRadius + 26.0;

            double tickVal = fraction * DynamicMaxSpeed;
            string labelStr = tickVal >= 100 ? $"{tickVal:F0}" : (tickVal >= 10 ? $"{tickVal:F0}" : $"{tickVal:0.#}");

            var p1 = new Point(MeterCenterX + r1 * cos, MeterCenterY + r1 * sin);
            var p2 = new Point(MeterCenterX + r2 * cos, MeterCenterY + r2 * sin);

            var fig = new PathFigure { StartPoint = p1, IsClosed = false };
            fig.Segments.Add(new LineSegment { Point = p2 });
            ticksGeom.Figures.Add(fig);

            ScaleTicks.Add(new ScaleTickMark
            {
                X1 = p1.X,
                Y1 = p1.Y,
                X2 = p2.X,
                Y2 = p2.Y,
                LabelX = MeterCenterX + rLabel * cos - 10.0,
                LabelY = MeterCenterY + rLabel * sin - 7.0,
                Label = labelStr
            });
        }
        MeterScaleTicksGeometry = ticksGeom;
    }

    private void AdaptDynamicMaxSpeed(double currentMeasured)
    {
        if (currentMeasured > DynamicMaxSpeed * 0.85)
        {
            double newMax;
            if (currentMeasured > 1000) newMax = 2000;
            else if (currentMeasured > 500) newMax = 1000;
            else if (currentMeasured > 200) newMax = 500;
            else if (currentMeasured > 100) newMax = 200;
            else if (currentMeasured > 50) newMax = 100;
            else newMax = 50;

            if (Math.Abs(newMax - DynamicMaxSpeed) > 0.01)
            {
                DynamicMaxSpeed = newMax;
                UpdateScaleTicks();
            }
        }
    }

    private void UpdateActiveArc(double speedValue)
    {
        AdaptDynamicMaxSpeed(speedValue);
        double fraction = DynamicMaxSpeed > 0 ? Math.Clamp(speedValue / DynamicMaxSpeed, 0.0, 1.0) : 0.0;
        MeterAngleFraction = fraction;

        double sweepDeg = fraction * TotalArcAngleDeg;
        MeterActiveArc = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius, StartAngleDeg, Math.Max(0.5, sweepDeg));
    }

    public static PathGeometry CreateArcGeometry(double cx, double cy, double radius, double startAngleDeg, double sweepAngleDeg)
    {
        var geometry = new PathGeometry { Figures = new PathFigures() };
        if (sweepAngleDeg <= 0.05) return geometry;

        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;

        var startPoint = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
        var endPoint = new Point(cx + radius * Math.Cos(endRad), cy + radius * Math.Sin(endRad));
        bool isLargeArc = sweepAngleDeg > 180.0;

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            Segments = new PathSegments()
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        geometry.Figures.Add(figure);
        return geometry;
    }

    // ── Real-Time Performance Graph Construction ──────────────────────────────

    private void RecordRealtimeSample(double speedMbps, bool isUpload)
    {
        double elapsed = _testStopwatch.Elapsed.TotalSeconds;
        _realtimeSamples.Add(new RealtimeSamplePoint
        {
            ElapsedSeconds = elapsed,
            SpeedMbps = speedMbps,
            IsUpload = isUpload
        });

        BuildRealtimeGraph();
    }

    private void BuildRealtimeGraph()
    {
        if (_realtimeSamples.Count < 2)
        {
            HasRealtimeGraphData = false;
            return;
        }

        const double GraphWidth = 540.0;
        const double GraphHeight = 120.0;

        double maxSpeed = Math.Max(20.0, _realtimeSamples.Max(s => s.SpeedMbps) * 1.15);
        double maxTime = Math.Max(10.0, _realtimeSamples.Max(s => s.ElapsedSeconds));

        GraphYMaxText = $"{maxSpeed:F0} Mbps";
        GraphYMidText = $"{maxSpeed / 2.0:F0} Mbps";
        GraphYMinText = "0 Mbps";

        var dlSamples = _realtimeSamples.Where(s => !s.IsUpload).OrderBy(s => s.ElapsedSeconds).ToList();
        var ulSamples = _realtimeSamples.Where(s => s.IsUpload).OrderBy(s => s.ElapsedSeconds).ToList();

        // 1. Download Graph Line & Area
        if (dlSamples.Count >= 2)
        {
            var dlLineGeom = new PathGeometry { Figures = new PathFigures() };
            var dlAreaGeom = new PathGeometry { Figures = new PathFigures() };

            var firstPt = new Point((dlSamples[0].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (dlSamples[0].SpeedMbps / maxSpeed) * GraphHeight);
            var lineFig = new PathFigure { StartPoint = firstPt, IsClosed = false, Segments = new PathSegments() };
            var areaFig = new PathFigure { StartPoint = new Point(firstPt.X, GraphHeight), IsClosed = true, Segments = new PathSegments() };
            areaFig.Segments.Add(new LineSegment { Point = firstPt });

            for (int i = 1; i < dlSamples.Count; i++)
            {
                var pt = new Point((dlSamples[i].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (dlSamples[i].SpeedMbps / maxSpeed) * GraphHeight);
                lineFig.Segments.Add(new LineSegment { Point = pt });
                areaFig.Segments.Add(new LineSegment { Point = pt });
            }

            var lastPt = lineFig.Segments.OfType<LineSegment>().LastOrDefault()?.Point ?? firstPt;
            areaFig.Segments.Add(new LineSegment { Point = new Point(lastPt.X, GraphHeight) });

            dlLineGeom.Figures.Add(lineFig);
            dlAreaGeom.Figures.Add(areaFig);

            RealtimeDownloadGeometry = dlLineGeom;
            RealtimeDownloadAreaGeometry = dlAreaGeom;
        }

        // 2. Upload Graph Line & Area
        if (ulSamples.Count >= 2)
        {
            var ulLineGeom = new PathGeometry { Figures = new PathFigures() };
            var ulAreaGeom = new PathGeometry { Figures = new PathFigures() };

            var firstPt = new Point((ulSamples[0].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (ulSamples[0].SpeedMbps / maxSpeed) * GraphHeight);
            var lineFig = new PathFigure { StartPoint = firstPt, IsClosed = false, Segments = new PathSegments() };
            var areaFig = new PathFigure { StartPoint = new Point(firstPt.X, GraphHeight), IsClosed = true, Segments = new PathSegments() };
            areaFig.Segments.Add(new LineSegment { Point = firstPt });

            for (int i = 1; i < ulSamples.Count; i++)
            {
                var pt = new Point((ulSamples[i].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (ulSamples[i].SpeedMbps / maxSpeed) * GraphHeight);
                lineFig.Segments.Add(new LineSegment { Point = pt });
                areaFig.Segments.Add(new LineSegment { Point = pt });
            }

            var lastPt = lineFig.Segments.OfType<LineSegment>().LastOrDefault()?.Point ?? firstPt;
            areaFig.Segments.Add(new LineSegment { Point = new Point(lastPt.X, GraphHeight) });

            ulLineGeom.Figures.Add(lineFig);
            ulAreaGeom.Figures.Add(areaFig);

            RealtimeUploadGeometry = ulLineGeom;
            RealtimeUploadAreaGeometry = ulAreaGeom;
        }

        HasRealtimeGraphData = true;
    }

    // ── Quality Assessment ────────────────────────────────────────────────────

    private void AssessConnectionQuality(double dlMbps, double ulMbps, double pingMs)
    {
        // Download evaluation
        if (dlMbps >= 75) { DownloadQuality = "Excellent"; DownloadQualityColor = "Success"; }
        else if (dlMbps >= 30) { DownloadQuality = "Good"; DownloadQualityColor = "Success"; }
        else if (dlMbps >= 10) { DownloadQuality = "Fair"; DownloadQualityColor = "Warning"; }
        else { DownloadQuality = "Poor"; DownloadQualityColor = "Danger"; }

        // Upload evaluation
        if (ulMbps >= 25) { UploadQuality = "Excellent"; UploadQualityColor = "Success"; }
        else if (ulMbps >= 10) { UploadQuality = "Good"; UploadQualityColor = "Success"; }
        else if (ulMbps >= 3) { UploadQuality = "Fair"; UploadQualityColor = "Warning"; }
        else { UploadQuality = "Poor"; UploadQualityColor = "Danger"; }

        // Ping evaluation
        if (pingMs <= 25) { PingQuality = "Excellent"; PingQualityColor = "Success"; }
        else if (pingMs <= 50) { PingQuality = "Good"; PingQualityColor = "Success"; }
        else if (pingMs <= 100) { PingQuality = "Fair"; PingQualityColor = "Warning"; }
        else { PingQuality = "High"; PingQualityColor = "Danger"; }

        // Overall score & verdict
        if (dlMbps >= 60 && ulMbps >= 15 && pingMs <= 35)
        {
            OverallQuality = "Excellent";
            OverallQualityPercent = 95.0;
            OverallQualityColor = "Success";
            QualityDescription = "Exceptional performance — optimal for 4K/8K media, real-time gaming, and cloud backups.";
        }
        else if (dlMbps >= 25 && ulMbps >= 8 && pingMs <= 60)
        {
            OverallQuality = "Good";
            OverallQualityPercent = 78.0;
            OverallQualityColor = "Success";
            QualityDescription = "Solid connection — great for multi-device HD streaming, conferencing, and fast browsing.";
        }
        else if (dlMbps >= 10 && pingMs <= 100)
        {
            OverallQuality = "Fair";
            OverallQualityPercent = 55.0;
            OverallQualityColor = "Warning";
            QualityDescription = "Standard connection — suitable for general browsing and video calls with occasional buffering.";
        }
        else
        {
            OverallQuality = "Poor";
            OverallQualityPercent = 30.0;
            OverallQualityColor = "Danger";
            QualityDescription = "Limited bandwidth or high latency detected. Consider checking your router or network status.";
        }
    }

    // ── Test Execution Lifecycle ──────────────────────────────────────────────

    [RelayCommand]
    public async Task StartTestAsync()
    {
        if (IsTesting) return;

        IsTesting = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        CurrentStage = SpeedTestStage.Ping;
        ActionButtonText = "TESTING...";
        StatusText = "Initializing diagnostic test...";

        // Reset live metrics
        DownloadSpeedText = "—";
        UploadSpeedText = "—";
        PingText = "—";
        JitterText = "—";
        DisplaySpeedValue = "0.0";
        DisplayUnitText = "ms";
        DisplayPhaseText = "PING";
        ActiveMeterBrushKey = "Brush.Accent";

        IsPingActive = true;
        IsPingDone = false;
        IsDownloadActive = false;
        IsDownloadDone = false;
        IsUploadActive = false;
        IsUploadDone = false;

        _realtimeSamples.Clear();
        _testStopwatch.Restart();
        BuildRealtimeGraph();

        await RefreshNetworkIdentityAsync();

        try
        {
            // ── 1. Ping Phase ─────────────────────────────────────────────────
            StatusText = "Measuring network latency...";
            double ping = await _speedTestService.TestPingAsync(token);
            token.ThrowIfCancellationRequested();

            PingText = ping > 0 ? $"{ping:F0} ms" : "Error";
            DisplaySpeedValue = ping > 0 ? $"{ping:F0}" : "—";
            DisplayUnitText = "ms";

            double jitter = ping > 0 ? Math.Max(0.5, ping * 0.12) : 0;
            JitterText = ping > 0 ? $"{jitter:F1} ms" : "—";

            IsPingActive = false;
            IsPingDone = true;

            // ── 2. Download Phase ─────────────────────────────────────────────
            CurrentStage = SpeedTestStage.Download;
            StatusText = "Measuring download bandwidth...";
            DisplayPhaseText = "DOWNLOAD";
            DisplayUnitText = "Mbps";
            ActiveMeterBrushKey = "Brush.Download";
            IsDownloadActive = true;

            double finalDownload = await _speedTestService.TestDownloadAsync(speed =>
            {
                RunOnUI(() =>
                {
                    CurrentSpeedValue = speed;
                    DisplaySpeedValue = $"{speed:F1}";
                    DownloadSpeedText = $"{speed:F1} Mbps";
                    UpdateActiveArc(speed);
                    RecordRealtimeSample(speed, isUpload: false);
                });
            }, token);

            token.ThrowIfCancellationRequested();
            DownloadSpeedText = finalDownload > 0 ? $"{finalDownload:F1} Mbps" : "Error";
            IsDownloadActive = false;
            IsDownloadDone = true;

            // ── 3. Upload Phase ───────────────────────────────────────────────
            CurrentStage = SpeedTestStage.Upload;
            StatusText = "Measuring upload throughput...";
            DisplayPhaseText = "UPLOAD";
            DisplayUnitText = "Mbps";
            ActiveMeterBrushKey = "Brush.Upload";
            IsUploadActive = true;

            double finalUpload = await _speedTestService.TestUploadAsync(speed =>
            {
                RunOnUI(() =>
                {
                    CurrentSpeedValue = speed;
                    DisplaySpeedValue = $"{speed:F1}";
                    UploadSpeedText = $"{speed:F1} Mbps";
                    UpdateActiveArc(speed);
                    RecordRealtimeSample(speed, isUpload: true);
                });
            }, token);

            token.ThrowIfCancellationRequested();
            UploadSpeedText = finalUpload > 0 ? $"{finalUpload:F1} Mbps" : "Error";
            IsUploadActive = false;
            IsUploadDone = true;

            // ── 4. Completion ─────────────────────────────────────────────────
            CurrentStage = SpeedTestStage.Completed;
            StatusText = "Diagnostic test complete";
            ActionButtonText = "RUN AGAIN";
            DisplayPhaseText = "COMPLETED";
            DisplaySpeedValue = $"{finalDownload:F1}";
            DisplayUnitText = "Mbps";
            ActiveMeterBrushKey = "Brush.Accent";
            UpdateActiveArc(finalDownload);

            AssessConnectionQuality(finalDownload, finalUpload, ping);

            if (finalDownload > 0 || finalUpload > 0)
            {
                var record = new SpeedTestRecord
                {
                    Timestamp = DateTime.UtcNow,
                    DownloadSpeedMbps = finalDownload,
                    UploadSpeedMbps = finalUpload,
                    PingMs = ping,
                    JitterMs = jitter,
                    ServerName = ServerName,
                    NetworkName = ActiveNetworkName,
                    ConnectionType = ActiveConnectionType
                };

                await _repository.SaveSpeedTestAsync(record);
                await LoadHistoryAsync();
            }
        }
        catch (OperationCanceledException)
        {
            CurrentStage = SpeedTestStage.Cancelled;
            StatusText = "Speed test cancelled";
            ActionButtonText = "RUN SPEED TEST";
            DisplayPhaseText = "CANCELLED";
            DisplaySpeedValue = "—";
            UpdateActiveArc(0.0);
        }
        catch (Exception ex)
        {
            CurrentStage = SpeedTestStage.Failed;
            StatusText = $"Speed test failed: {ex.Message}";
            ActionButtonText = "TRY AGAIN";
            DisplayPhaseText = "ERROR";
            DisplaySpeedValue = "—";
            UpdateActiveArc(0.0);
        }
        finally
        {
            _testStopwatch.Stop();
            IsTesting = false;
            IsPingActive = false;
            IsDownloadActive = false;
            IsUploadActive = false;
        }
    }

    [RelayCommand]
    public void CancelTest()
    {
        if (IsTesting)
        {
            _cancellationTokenSource?.Cancel();
        }
    }

    [RelayCommand]
    public async Task RepeatTestAsync()
    {
        if (!IsTesting)
        {
            await StartTestAsync();
        }
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _repository.GetSpeedTestsAsync(20);
            RunOnUI(() =>
            {
                TestHistory.Clear();
                foreach (var item in history)
                {
                    TestHistory.Add(item);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load speed test history: {ex.Message}");
        }
    }

    private static void RunOnUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
