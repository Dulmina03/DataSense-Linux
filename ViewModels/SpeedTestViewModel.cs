using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
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
    public bool IsMajor { get; init; } = true;
}

public class RealtimeSamplePoint
{
    public double ElapsedSeconds { get; init; }
    public double SpeedMBps { get; init; }
    public bool IsUpload { get; init; }
}

public partial class SpeedTestViewModel : ViewModelBase, IDisposable
{
    private readonly ISpeedTestService _speedTestService;
    private readonly INetworkUsageRepository _repository;
    private readonly INetworkMonitorWorker? _networkMonitorWorker;
    private readonly INetworkIdentityService _identityService;
    private readonly INetworkConnectionService? _connectionService;
    private CancellationTokenSource? _cancellationTokenSource;

    private const double MeterCenterX = 180.0;
    private const double MeterCenterY = 160.0;
    private const double MeterRadius = 120.0;
    private const double StartAngleDeg = 150.0;
    private const double TotalArcAngleDeg = 240.0;

    // ── Metric Readouts ────────────────────────────────────────────────────────
    [ObservableProperty] private string _downloadSpeedText = "—";
    [ObservableProperty] private string _uploadSpeedText = "—";
    [ObservableProperty] private string _pingText = "—";
    [ObservableProperty] private string _jitterText = "—";

    [ObservableProperty] private string _downloadValueText = "—";
    [ObservableProperty] private string _uploadValueText = "—";
    [ObservableProperty] private string _pingValueText = "—";

    [ObservableProperty] private string _downloadQuality = "—";
    [ObservableProperty] private string _uploadQuality = "—";
    [ObservableProperty] private string _pingQuality = "—";
    [ObservableProperty] private string _jitterQuality = "—";

    [ObservableProperty] private string _downloadQualityColor = "Muted";
    [ObservableProperty] private string _uploadQualityColor = "Muted";
    [ObservableProperty] private string _pingQualityColor = "Muted";

    // ── Central Gauge State ───────────────────────────────────────────────────
    [ObservableProperty] private string _displaySpeedValue = "0.0";
    [ObservableProperty] private string _displayUnitText = "MB/s";
    [ObservableProperty] private string _displayPhaseText = "READY";
    [ObservableProperty] private string _displayPhaseIcon = "☁️↓";
    [ObservableProperty] private string _activePhaseSemanticColor = "Download";
    [ObservableProperty] private double _currentSpeedValue;
    [ObservableProperty] private double _dynamicMaxSpeed = 20.0;
    [ObservableProperty] private double _meterAngleFraction;

    [ObservableProperty] private PathGeometry? _meterActiveArc;
    [ObservableProperty] private PathGeometry? _meterActiveDomeArc;
    [ObservableProperty] private PathGeometry? _meterSecondaryArc;
    [ObservableProperty] private PathGeometry? _meterBackgroundArc;
    [ObservableProperty] private PathGeometry? _meterOuterRing;
    [ObservableProperty] private PathGeometry? _meterInnerRing;
    [ObservableProperty] private PathGeometry? _meterInnerDomeArc;
    [ObservableProperty] private PathGeometry? _meterScaleTicksGeometry;
    [ObservableProperty] private PathGeometry? _meterActiveScaleTicksGeometry;

    [ObservableProperty] private PathGeometry? _concentricRing1;
    [ObservableProperty] private PathGeometry? _concentricRing2;
    [ObservableProperty] private PathGeometry? _concentricRing3;
    [ObservableProperty] private PathGeometry? _concentricRing4;

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
    [ObservableProperty] private string _ipAddress = "192.168.1.1";
    [ObservableProperty] private string _osPlatform = "Linux";

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
    [ObservableProperty] private string _graphYMaxText = "20 MB/s";
    [ObservableProperty] private string _graphYMidText = "10 MB/s";
    [ObservableProperty] private string _graphYMinText = "0 MB/s";
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
        INetworkIdentityService? identityService = null,
        INetworkConnectionService? connectionService = null)
    {
        _speedTestService = speedTestService;
        _repository = repository;
        _networkMonitorWorker = networkMonitorWorker;
        _connectionService = connectionService;
        _identityService = identityService ?? new NetworkIdentityService(connectionService ?? new LinuxNetworkConnectionService());

        // Resolve OS platform details
        ResolveSystemInfo();

        // Initialize radial meter static geometries & scale
        InitializeMeterGeometry();
        UpdateScaleTicks();

        _ = RefreshNetworkIdentityAsync();
        _ = LoadHistoryAsync();
    }

    private void ResolveSystemInfo()
    {
        try
        {
            string desc = RuntimeInformation.OSDescription;
            if (desc.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            {
                OsPlatform = "Ubuntu Linux";
            }
            else
            {
                OsPlatform = desc.Length > 20 ? desc[..20] : desc;
            }
        }
        catch
        {
            OsPlatform = "Linux OS";
        }
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

            // Resolve IP Address
            if (_connectionService != null)
            {
                var details = await _connectionService.GetConnectionDetailsAsync(iface);
                if (!string.IsNullOrWhiteSpace(details.Ipv4Address) && details.Ipv4Address != "—" && details.Ipv4Address != "Unavailable")
                {
                    IpAddress = details.Ipv4Address;
                }
                else
                {
                    IpAddress = "192.168.1.100";
                }
            }
            else
            {
                IpAddress = "192.168.1.100";
            }
        }
        catch
        {
            ActiveNetworkName = "Connected Network";
            ActiveConnectionType = "Network";
            IpAddress = "192.168.1.100";
        }
    }

    // ── Gauge & Scale Mathematics ─────────────────────────────────────────────

    private void InitializeMeterGeometry()
    {
        // Concentric Orbital Radar Rings
        ConcentricRing1 = CreateFullCircleGeometry(MeterCenterX, MeterCenterY, 155.0);
        ConcentricRing2 = CreateFullCircleGeometry(MeterCenterX, MeterCenterY, 210.0);
        ConcentricRing3 = CreateFullCircleGeometry(MeterCenterX, MeterCenterY, 275.0);
        ConcentricRing4 = CreateFullCircleGeometry(MeterCenterX, MeterCenterY, 350.0);

        // Background track (240 degrees from 150° to 390°)
        MeterBackgroundArc = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius, StartAngleDeg, TotalArcAngleDeg);
        MeterOuterRing = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius + 24.0, StartAngleDeg - 5.0, TotalArcAngleDeg + 10.0);
        MeterInnerRing = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius - 20.0, StartAngleDeg, TotalArcAngleDeg);
        MeterInnerDomeArc = CreateArcGeometry(MeterCenterX, MeterCenterY, 82.0, StartAngleDeg, TotalArcAngleDeg);

        UpdateActiveArc(0.0);
    }

    private static PathGeometry CreateFullCircleGeometry(double cx, double cy, double radius)
    {
        var geom = new PathGeometry { Figures = new PathFigures() };
        var fig = new PathFigure
        {
            StartPoint = new Point(cx + radius, cy),
            IsClosed = true,
            Segments = new PathSegments
            {
                new ArcSegment
                {
                    Point = new Point(cx - radius, cy),
                    Size = new Size(radius, radius),
                    IsLargeArc = false,
                    SweepDirection = SweepDirection.Clockwise
                },
                new ArcSegment
                {
                    Point = new Point(cx + radius, cy),
                    Size = new Size(radius, radius),
                    IsLargeArc = false,
                    SweepDirection = SweepDirection.Clockwise
                }
            }
        };
        geom.Figures.Add(fig);
        return geom;
    }

    private void UpdateScaleTicks()
    {
        ScaleTicks.Clear();
        var allTicksGeom = new PathGeometry { Figures = new PathFigures() };

        int majorSteps = 8;
        int subSteps = 3; // 3 minor ticks between each major tick

        int totalTicks = majorSteps * subSteps;
        for (int i = 0; i <= totalTicks; i++)
        {
            double fraction = (double)i / totalTicks;
            double angleDeg = StartAngleDeg + fraction * TotalArcAngleDeg;
            double rad = angleDeg * Math.PI / 180.0;

            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            bool isMajor = (i % subSteps) == 0;
            double r1 = isMajor ? (MeterRadius + 2.0) : (MeterRadius + 4.0);
            double r2 = isMajor ? (MeterRadius + 14.0) : (MeterRadius + 9.0);
            double rLabel = MeterRadius + 26.0;

            var p1 = new Point(MeterCenterX + r1 * cos, MeterCenterY + r1 * sin);
            var p2 = new Point(MeterCenterX + r2 * cos, MeterCenterY + r2 * sin);

            var fig = new PathFigure { StartPoint = p1, IsClosed = false, Segments = new PathSegments() };
            fig.Segments.Add(new LineSegment { Point = p2 });
            allTicksGeom.Figures.Add(fig);

            if (isMajor)
            {
                double tickVal = fraction * DynamicMaxSpeed;
                string labelStr = tickVal >= 10 ? $"{tickVal:F0}" : (tickVal > 0 ? $"{tickVal:0.#}" : "0");

                ScaleTicks.Add(new ScaleTickMark
                {
                    X1 = p1.X,
                    Y1 = p1.Y,
                    X2 = p2.X,
                    Y2 = p2.Y,
                    LabelX = MeterCenterX + rLabel * cos - 10.0,
                    LabelY = MeterCenterY + rLabel * sin - 7.0,
                    Label = labelStr,
                    IsMajor = true
                });
            }
        }

        MeterScaleTicksGeometry = allTicksGeom;
    }

    private void AdaptDynamicMaxSpeed(double currentMeasured)
    {
        if (currentMeasured > DynamicMaxSpeed * 0.85)
        {
            double newMax;
            if (currentMeasured > 250) newMax = 500;
            else if (currentMeasured > 100) newMax = 250;
            else if (currentMeasured > 50) newMax = 100;
            else if (currentMeasured > 20) newMax = 50;
            else if (currentMeasured > 10) newMax = 20;
            else newMax = 10;

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
        MeterActiveDomeArc = CreateArcGeometry(MeterCenterX, MeterCenterY, 82.0, StartAngleDeg, Math.Max(0.5, sweepDeg));
        MeterSecondaryArc = CreateArcGeometry(MeterCenterX, MeterCenterY, MeterRadius - 6.0, StartAngleDeg, Math.Max(0.5, sweepDeg * 0.95));

        // Active glowing ticks
        var activeTicksGeom = new PathGeometry { Figures = new PathFigures() };
        int majorSteps = 8;
        int subSteps = 3;
        int totalTicks = majorSteps * subSteps;
        int activeTickCount = (int)Math.Round(fraction * totalTicks);

        for (int i = 0; i <= activeTickCount; i++)
        {
            double f = (double)i / totalTicks;
            double angleDeg = StartAngleDeg + f * TotalArcAngleDeg;
            double rad = angleDeg * Math.PI / 180.0;

            double cos = Math.Cos(rad);
            double sin = Math.Sin(rad);

            bool isMajor = (i % subSteps) == 0;
            double r1 = isMajor ? (MeterRadius + 2.0) : (MeterRadius + 4.0);
            double r2 = isMajor ? (MeterRadius + 14.0) : (MeterRadius + 9.0);

            var p1 = new Point(MeterCenterX + r1 * cos, MeterCenterY + r1 * sin);
            var p2 = new Point(MeterCenterX + r2 * cos, MeterCenterY + r2 * sin);

            var fig = new PathFigure { StartPoint = p1, IsClosed = false, Segments = new PathSegments() };
            fig.Segments.Add(new LineSegment { Point = p2 });
            activeTicksGeom.Figures.Add(fig);
        }
        MeterActiveScaleTicksGeometry = activeTicksGeom;
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

    private void RecordRealtimeSample(double speedMBps, bool isUpload)
    {
        double elapsed = _testStopwatch.Elapsed.TotalSeconds;
        _realtimeSamples.Add(new RealtimeSamplePoint
        {
            ElapsedSeconds = elapsed,
            SpeedMBps = speedMBps,
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

        double maxSpeed = Math.Max(5.0, _realtimeSamples.Max(s => s.SpeedMBps) * 1.15);
        double maxTime = Math.Max(10.0, _realtimeSamples.Max(s => s.ElapsedSeconds));

        GraphYMaxText = $"{maxSpeed:F1} MB/s";
        GraphYMidText = $"{maxSpeed / 2.0:F1} MB/s";
        GraphYMinText = "0 MB/s";

        var dlSamples = _realtimeSamples.Where(s => !s.IsUpload).OrderBy(s => s.ElapsedSeconds).ToList();
        var ulSamples = _realtimeSamples.Where(s => s.IsUpload).OrderBy(s => s.ElapsedSeconds).ToList();

        // 1. Download Graph Line & Area
        if (dlSamples.Count >= 2)
        {
            var dlLineGeom = new PathGeometry { Figures = new PathFigures() };
            var dlAreaGeom = new PathGeometry { Figures = new PathFigures() };

            var firstPt = new Point((dlSamples[0].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (dlSamples[0].SpeedMBps / maxSpeed) * GraphHeight);
            var lineFig = new PathFigure { StartPoint = firstPt, IsClosed = false, Segments = new PathSegments() };
            var areaFig = new PathFigure { StartPoint = new Point(firstPt.X, GraphHeight), IsClosed = true, Segments = new PathSegments() };
            areaFig.Segments.Add(new LineSegment { Point = firstPt });

            for (int i = 1; i < dlSamples.Count; i++)
            {
                var pt = new Point((dlSamples[i].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (dlSamples[i].SpeedMBps / maxSpeed) * GraphHeight);
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

            var firstPt = new Point((ulSamples[0].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (ulSamples[0].SpeedMBps / maxSpeed) * GraphHeight);
            var lineFig = new PathFigure { StartPoint = firstPt, IsClosed = false, Segments = new PathSegments() };
            var areaFig = new PathFigure { StartPoint = new Point(firstPt.X, GraphHeight), IsClosed = true, Segments = new PathSegments() };
            areaFig.Segments.Add(new LineSegment { Point = firstPt });

            for (int i = 1; i < ulSamples.Count; i++)
            {
                var pt = new Point((ulSamples[i].ElapsedSeconds / maxTime) * GraphWidth, GraphHeight - (ulSamples[i].SpeedMBps / maxSpeed) * GraphHeight);
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

    private void AssessConnectionQuality(double downloadMbps, double uploadMbps, double pingMs)
    {
        // Quality tiers based on network performance
        DownloadQuality = downloadMbps >= 100 ? "Excellent" : (downloadMbps >= 30 ? "Good" : (downloadMbps >= 10 ? "Fair" : "Poor"));
        UploadQuality = uploadMbps >= 50 ? "Excellent" : (uploadMbps >= 15 ? "Good" : (uploadMbps >= 5 ? "Fair" : "Poor"));
        PingQuality = pingMs > 0 && pingMs <= 25 ? "Excellent" : (pingMs <= 60 ? "Good" : (pingMs <= 120 ? "Fair" : "Poor"));

        DownloadQualityColor = DownloadQuality == "Excellent" ? "Success" : (DownloadQuality == "Good" ? "Accent" : "Warning");
        UploadQualityColor = UploadQuality == "Excellent" ? "Success" : (UploadQuality == "Good" ? "Accent" : "Warning");
        PingQualityColor = PingQuality == "Excellent" ? "Success" : (PingQuality == "Good" ? "Accent" : "Warning");

        double score = 0;
        if (downloadMbps >= 100) score += 40;
        else score += Math.Min(40, (downloadMbps / 100.0) * 40.0);

        if (uploadMbps >= 50) score += 30;
        else score += Math.Min(30, (uploadMbps / 50.0) * 30.0);

        if (pingMs > 0)
        {
            if (pingMs <= 20) score += 30;
            else if (pingMs <= 50) score += 25;
            else if (pingMs <= 100) score += 15;
            else score += 5;
        }

        OverallQualityPercent = Math.Clamp(score, 0, 100);
        if (score >= 85)
        {
            OverallQuality = "Excellent";
            OverallQualityColor = "Success";
            QualityDescription = "Exceptional network speed & low latency — optimal for 4K streaming, gaming, and cloud backups.";
        }
        else if (score >= 65)
        {
            OverallQuality = "Good";
            OverallQualityColor = "Accent";
            QualityDescription = "High-speed connection suitable for video conferences, HD media, and fast browsing.";
        }
        else if (score >= 40)
        {
            OverallQuality = "Fair";
            OverallQualityColor = "Warning";
            QualityDescription = "Standard connection speed — may experience occasional buffering under heavy load.";
        }
        else
        {
            OverallQuality = "Poor";
            OverallQualityColor = "Error";
            QualityDescription = "Degraded connectivity detected. Check router placement or contact ISP.";
        }
    }

    // ── Test Execution Commands ───────────────────────────────────────────────

    [RelayCommand]
    public async Task StartTestAsync()
    {
        if (IsTesting) return;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var token = _cancellationTokenSource.Token;

        IsTesting = true;
        ActionButtonText = "TESTING...";
        _realtimeSamples.Clear();
        HasRealtimeGraphData = false;
        _testStopwatch.Restart();

        IsDownloadActive = false;
        IsDownloadDone = false;
        IsUploadActive = false;
        IsUploadDone = false;
        IsPingActive = false;
        IsPingDone = false;

        try
        {
            // Start ping measurement asynchronously in background
            var pingTask = _speedTestService.TestPingAsync(token);

            // ── 1. Download Phase (Starts from 0 and increases in Cyan) ─────────
            CurrentStage = SpeedTestStage.Download;
            StatusText = "Measuring download bandwidth...";
            DisplayPhaseText = "DOWNLOAD";
            DisplayUnitText = "MB/s";
            DisplayPhaseIcon = "☁️↓";
            ActivePhaseSemanticColor = "Download";
            ActiveMeterBrushKey = "Brush.Download";
            CurrentSpeedValue = 0.0;
            DisplaySpeedValue = "0.0";
            UpdateActiveArc(0.0);
            IsDownloadActive = true;

            double finalDownloadMbps = await _speedTestService.TestDownloadAsync(speedMbps =>
            {
                double speedMBps = speedMbps / 8.0;
                RunOnUI(() =>
                {
                    CurrentSpeedValue = speedMBps;
                    DisplaySpeedValue = $"{speedMBps:F1}";
                    DownloadSpeedText = $"{speedMBps:F1} MB/s";
                    DownloadValueText = $"{speedMBps:F1}";
                    UpdateActiveArc(speedMBps);
                    RecordRealtimeSample(speedMBps, isUpload: false);
                });
            }, token);

            token.ThrowIfCancellationRequested();
            double finalDownloadMBps = finalDownloadMbps / 8.0;
            DownloadSpeedText = finalDownloadMBps > 0 ? $"{finalDownloadMBps:F1} MB/s" : "Error";
            DownloadValueText = finalDownloadMBps > 0 ? $"{finalDownloadMBps:F1}" : "—";
            IsDownloadActive = false;
            IsDownloadDone = true;

            // ── 2. Transition: Reset Meter to 0 and switch to Upload color ─────
            CurrentStage = SpeedTestStage.Upload;
            StatusText = "Preparing upload test...";
            DisplayPhaseText = "UPLOAD";
            DisplayUnitText = "MB/s";
            DisplayPhaseIcon = "☁️↑";
            ActivePhaseSemanticColor = "Upload";
            ActiveMeterBrushKey = "Brush.Upload";
            CurrentSpeedValue = 0.0;
            DisplaySpeedValue = "0.0";
            UpdateActiveArc(0.0);
            IsUploadActive = true;

            // Smooth visual pause so user clearly sees the meter drop to 0 and switch to Purple
            await Task.Delay(350, token);

            // ── 3. Upload Phase (Starts from 0 and increases in Purple) ─────────
            StatusText = "Measuring upload throughput...";
            double finalUploadMbps = await _speedTestService.TestUploadAsync(speedMbps =>
            {
                double speedMBps = speedMbps / 8.0;
                RunOnUI(() =>
                {
                    CurrentSpeedValue = speedMBps;
                    DisplaySpeedValue = $"{speedMBps:F1}";
                    UploadSpeedText = $"{speedMBps:F1} MB/s";
                    UploadValueText = $"{speedMBps:F1}";
                    UpdateActiveArc(speedMBps);
                    RecordRealtimeSample(speedMBps, isUpload: true);
                });
            }, token);

            token.ThrowIfCancellationRequested();
            double finalUploadMBps = finalUploadMbps / 8.0;
            UploadSpeedText = finalUploadMBps > 0 ? $"{finalUploadMBps:F1} MB/s" : "Error";
            UploadValueText = finalUploadMBps > 0 ? $"{finalUploadMBps:F1}" : "—";
            IsUploadActive = false;
            IsUploadDone = true;

            // ── 4. Finalize Ping ───────────────────────────────────────────────
            double ping = 0;
            try
            {
                ping = await pingTask;
            }
            catch
            {
                ping = await _speedTestService.TestPingAsync(token);
            }

            PingText = ping > 0 ? $"{ping:F0} ms" : "—";
            PingValueText = ping > 0 ? $"{ping:F0}" : "—";
            double jitter = ping > 0 ? Math.Max(0.5, ping * 0.12) : 0;
            JitterText = ping > 0 ? $"{jitter:F1} ms" : "—";
            IsPingDone = true;

            // ── 5. Completion ─────────────────────────────────────────────────
            CurrentStage = SpeedTestStage.Completed;
            StatusText = "Diagnostic test complete";
            ActionButtonText = "RUN AGAIN";
            DisplayPhaseText = "COMPLETED";
            DisplayPhaseIcon = "☁️↓";
            ActivePhaseSemanticColor = "Download";
            DisplaySpeedValue = $"{finalDownloadMBps:F1}";
            DisplayUnitText = "MB/s";
            ActiveMeterBrushKey = "Brush.Accent";
            UpdateActiveArc(finalDownloadMBps);

            AssessConnectionQuality(finalDownloadMbps, finalUploadMbps, ping);

            if (finalDownloadMbps > 0 || finalUploadMbps > 0)
            {
                var record = new SpeedTestRecord
                {
                    Timestamp = DateTime.UtcNow,
                    DownloadSpeedMbps = finalDownloadMBps, // stored as MB/s
                    UploadSpeedMbps = finalUploadMBps,
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
            StatusText = "Speed test was cancelled";
            ActionButtonText = "RUN SPEED TEST";
            DisplayPhaseText = "CANCELLED";
            DisplayPhaseIcon = "⚡";
            ActivePhaseSemanticColor = "Muted";
            DisplaySpeedValue = "—";
            UpdateActiveArc(0.0);
        }
        catch (Exception ex)
        {
            CurrentStage = SpeedTestStage.Failed;
            StatusText = $"Speed test failed: {ex.Message}";
            ActionButtonText = "TRY AGAIN";
            DisplayPhaseText = "ERROR";
            DisplayPhaseIcon = "⚠️";
            ActivePhaseSemanticColor = "Warning";
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
