using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using DataSense.ViewModels;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class HistoryViewModelTests : IDisposable
{
    private readonly TestDatabaseContext _dbContext;
    private readonly HistoricalAnalyticsService _historicalService;
    private readonly ApplicationAnalyticsService _appAnalyticsService;
    private readonly MockNetworkMonitorWorker _monitorWorker;
    private readonly LinuxApplicationIconService _iconService;
    private readonly ApplicationChartColorProvider _colorProvider;

    public HistoryViewModelTests()
    {
        _dbContext = TestDatabaseFactory.CreateAsync().GetAwaiter().GetResult();
        _historicalService = new HistoricalAnalyticsService(_dbContext.Repository);
        _appAnalyticsService = new ApplicationAnalyticsService(_dbContext.Repository, new LinuxProcessResolver());
        _monitorWorker = new MockNetworkMonitorWorker();
        _iconService = new LinuxApplicationIconService();
        _colorProvider = new ApplicationChartColorProvider();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task InitialState_DefaultsToLast7Days_AndComputesValidRange()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        await vm.LoadAsync(showLoading: false);

        Assert.Equal(HistoryPeriodType.Last7Days, vm.SelectedPeriod);
        Assert.True(vm.Is7DaysActive);
        Assert.False(vm.IsTodayActive);
        Assert.False(vm.IsMonthActive);
        Assert.Equal("AVG / DAY", vm.AverageUsageLabel);

        var (start, end) = vm.ComputeDateRange();
        Assert.True(end > start);
        Assert.Equal(7, (end.Date - start.Date).Days + 1);
    }

    [Fact]
    public async Task TodayHourlyAggregation_Generates24HourlyBuckets_WithRealCounters()
    {
        var today = DateTime.UtcNow.Date;

        // Hour 10 cumulative counter progression
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(10).AddMinutes(1),
            InterfaceName = "wlo1",
            BytesReceived = 1_000_000,
            BytesSent = 200_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(10).AddMinutes(50),
            InterfaceName = "wlo1",
            BytesReceived = 3_000_000, // Delta = 2,000,000
            BytesSent = 700_000       // Delta = 500,000
        });

        // Hour 14 cumulative counter progression
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(14).AddMinutes(5),
            InterfaceName = "wlo1",
            BytesReceived = 3_000_000,
            BytesSent = 700_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(14).AddMinutes(45),
            InterfaceName = "wlo1",
            BytesReceived = 6_000_000, // Delta = 3,000,000
            BytesSent = 1_700_000     // Delta = 1,000,000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        Assert.Equal(HistoryPeriodType.Today, vm.SelectedPeriod);
        Assert.Equal(24, vm.HistoricalChartPoints.Count);
        Assert.Equal(2_000_000, vm.HistoricalChartPoints[10].DownloadBytes);
        Assert.Equal(500_000, vm.HistoricalChartPoints[10].UploadBytes);
        Assert.Equal(3_000_000, vm.HistoricalChartPoints[14].DownloadBytes);
        Assert.Equal(1_000_000, vm.HistoricalChartPoints[14].UploadBytes);
        Assert.Equal(0, vm.HistoricalChartPoints[0].DownloadBytes);
    }

    [Fact]
    public async Task SevenDayAggregation_Produces7Points_AndHandlesZeroUsageDays()
    {
        var targetDay = DateTime.UtcNow.Date.AddDays(-2);
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = targetDay.AddHours(1),
            InterfaceName = "wlo1",
            BytesReceived = 10_000_000,
            BytesSent = 2_000_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = targetDay.AddHours(20),
            InterfaceName = "wlo1",
            BytesReceived = 60_000_000, // Delta = 50,000,000
            BytesSent = 12_000_000     // Delta = 10,000,000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);

        Assert.Equal(7, vm.HistoricalChartPoints.Count);
        var activePoint = vm.HistoricalChartPoints.FirstOrDefault(p => p.DownloadBytes == 50_000_000);
        Assert.NotNull(activePoint);
        Assert.Equal(10_000_000, activePoint.UploadBytes);

        // Zero usage days must still have 0 bytes and valid label
        var zeroPoint = vm.HistoricalChartPoints.FirstOrDefault(p => p.DownloadBytes == 0 && p.UploadBytes == 0);
        Assert.NotNull(zeroPoint);
    }

    [Theory]
    [InlineData(2023, 2, 28)] // Non-leap year February (28 days)
    [InlineData(2024, 2, 29)] // Leap year February (29 days)
    [InlineData(2024, 4, 30)] // 30-day month (April)
    [InlineData(2024, 8, 31)] // 31-day month (August)
    public async Task MonthSelection_GeneratesExactDayCount_ForVariousMonths(int year, int month, int expectedDays)
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();
        vm.SelectedMonth = new MonthSelectItem
        {
            Year = year,
            Month = month,
            DisplayName = $"{year}-{month:D2}"
        };

        await vm.LoadAsync(showLoading: false);

        Assert.Equal(expectedDays, vm.HistoricalChartPoints.Count);
        Assert.Equal(expectedDays, vm.MonthlyBreakdownItems.Count);
        Assert.Equal(1, vm.HistoricalChartPoints[0].Timestamp.Day);
        Assert.Equal(expectedDays, vm.HistoricalChartPoints[^1].Timestamp.Day);
    }

    [Fact]
    public async Task MonthSwitching_ReloadsAllData_WithoutStaleValues()
    {
        // Month 1: 2024-01 with 10MB DL + 2MB UL delta
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = new DateTime(2024, 1, 15, 1, 0, 0, DateTimeKind.Utc),
            InterfaceName = "wlo1",
            BytesReceived = 1_000_000,
            BytesSent = 500_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = new DateTime(2024, 1, 15, 20, 0, 0, DateTimeKind.Utc),
            InterfaceName = "wlo1",
            BytesReceived = 11_000_000, // Delta = 10MB
            BytesSent = 2_500_000      // Delta = 2MB
        });

        // Month 2: 2024-02 with 50MB DL + 5MB UL delta
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = new DateTime(2024, 2, 10, 1, 0, 0, DateTimeKind.Utc),
            InterfaceName = "wlo1",
            BytesReceived = 100_000_000,
            BytesSent = 10_000_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = new DateTime(2024, 2, 10, 20, 0, 0, DateTimeKind.Utc),
            InterfaceName = "wlo1",
            BytesReceived = 150_000_000, // Delta = 50MB
            BytesSent = 15_000_000      // Delta = 5MB
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();

        // Load Month 1
        vm.SelectedMonth = new MonthSelectItem { Year = 2024, Month = 1, DisplayName = "January 2024" };
        await vm.LoadAsync(showLoading: false);
        string month1Total = vm.TotalUsageText;
        Assert.False(string.IsNullOrEmpty(month1Total));
        Assert.NotEqual("0 B", month1Total);

        // Switch to Month 2
        vm.SelectedMonth = new MonthSelectItem { Year = 2024, Month = 2, DisplayName = "February 2024" };
        await vm.LoadAsync(showLoading: false);
        string month2Total = vm.TotalUsageText;
        Assert.NotEqual(month1Total, month2Total);
        Assert.Equal(29, vm.HistoricalChartPoints.Count);
    }

    [Fact]
    public async Task EmptyMonth_RendersCleanZeroState_WithoutMockData()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();
        vm.SelectedMonth = new MonthSelectItem { Year = 2020, Month = 5, DisplayName = "May 2020" };
        await vm.LoadAsync(showLoading: false);

        Assert.Equal("0 B", vm.TotalUsageText);
        Assert.Equal("0 B", vm.TotalDownloadedText);
        Assert.Equal("0 B", vm.TotalUploadedText);
        Assert.Equal(31, vm.HistoricalChartPoints.Count);
        Assert.All(vm.HistoricalChartPoints, p => Assert.Equal(0, p.TotalBytes));
    }

    [Fact]
    public async Task DownloadUploadTotal_AggregationAndShares_CalculatedAccurately()
    {
        var today = DateTime.UtcNow.Date;
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(2),
            InterfaceName = "wlo1",
            BytesReceived = 100_000,
            BytesSent = 50_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(18),
            InterfaceName = "wlo1",
            BytesReceived = 850_000, // Delta = 750,000
            BytesSent = 300_000     // Delta = 250,000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);

        Assert.Equal("75.0% of total", vm.DownloadShareText);
        Assert.Equal("25.0% of total", vm.UploadShareText);
    }

    [Fact]
    public async Task PeriodSwitching_ResetsAndRegeneratesBarChartGeometry_WithNoStaleData()
    {
        var today = DateTime.UtcNow.Date;
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(12).AddMinutes(1),
            InterfaceName = "wlo1",
            BytesReceived = 1_000_000,
            BytesSent = 500_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(12).AddMinutes(40),
            InterfaceName = "wlo1",
            BytesReceived = 11_000_000, // Delta = 10MB
            BytesSent = 2_500_000      // Delta = 2MB
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // 1. Switch to Today
        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(24, vm.HistoricalChartPoints.Count);
        Assert.True(vm.HistoricalChartPoints[12].DownloadBarHeight > 0);

        // 2. Switch to 7 Days
        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(7, vm.HistoricalChartPoints.Count);

        // 3. Switch to Month
        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);
        Assert.True(vm.HistoricalChartPoints.Count >= 28);
    }

    [Fact]
    public async Task SearchText_FiltersApplicationsAndSessions_Accurately()
    {
        var now = DateTime.UtcNow;
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Dialog 4G",
            InterfaceName = "wlan0",
            ConnectionType = "Wi-Fi",
            StartTime = now.AddHours(-2),
            EndTime = now,
            BytesDownloaded = 500_000,
            BytesUploaded = 100_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "eth0",
            ConnectionType = "Ethernet",
            StartTime = now.AddHours(-4),
            EndTime = now.AddHours(-2),
            BytesDownloaded = 1_000_000,
            BytesUploaded = 200_000
        });

        await _dbContext.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "chrome",
            Pid = 1234,
            StartTimeTicks = now.Ticks,
            Timestamp = now.AddHours(-1),
            BytesDownloaded = 800_000,
            BytesUploaded = 150_000,
            DataSource = "Nethogs"
        });

        await _dbContext.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "spotify",
            Pid = 5678,
            StartTimeTicks = now.Ticks,
            Timestamp = now.AddHours(-1),
            BytesDownloaded = 300_000,
            BytesUploaded = 50_000,
            DataSource = "Nethogs"
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        await vm.LoadAsync(showLoading: false);

        Assert.True(vm.FilteredNetworkSessions.Count >= 2);
        Assert.True(vm.FilteredApplications.Count >= 2);

        // Filter by "Dialog"
        vm.SearchText = "Dialog";
        Assert.Single(vm.FilteredNetworkSessions);
        Assert.Equal("Dialog 4G", vm.FilteredNetworkSessions.First().DisplayName);
        Assert.Empty(vm.FilteredApplications);

        // Filter by "Spotify"
        vm.SearchText = "spotify";
        Assert.Empty(vm.FilteredNetworkSessions);
        Assert.Single(vm.FilteredApplications);
        Assert.Equal("spotify", vm.FilteredApplications.First().ProcessName);

        // Clear search
        vm.SearchText = "";
        Assert.True(vm.FilteredNetworkSessions.Count >= 2);
        Assert.True(vm.FilteredApplications.Count >= 2);
    }

    [Fact]
    public async Task SortingOptions_ReordersApplications_Correctly()
    {
        var now = DateTime.UtcNow;
        await _dbContext.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "appA",
            Pid = 111,
            StartTimeTicks = now.Ticks,
            Timestamp = now.AddHours(-1),
            BytesDownloaded = 100_000,
            BytesUploaded = 900_000, // Highest upload
            DataSource = "Nethogs"
        });

        await _dbContext.Repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "appB",
            Pid = 222,
            StartTimeTicks = now.Ticks,
            Timestamp = now.AddHours(-1),
            BytesDownloaded = 800_000, // Highest download
            BytesUploaded = 50_000,
            DataSource = "Nethogs"
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        await vm.LoadAsync(showLoading: false);

        Assert.True(vm.FilteredApplications.Count >= 2);

        // Sort by Download
        vm.SelectedSortOption = "Download (Desc)";
        Assert.Equal("appB", vm.FilteredApplications.First().ProcessName);

        // Sort by Upload
        vm.SelectedSortOption = "Upload (Desc)";
        Assert.Equal("appA", vm.FilteredApplications.First().ProcessName);
    }

    [Fact]
    public async Task HoverCalculation_LocatesClosestSample_AndSetsTooltipData()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        await vm.LoadAsync(showLoading: false);

        Assert.NotEmpty(vm.HistoricalChartPoints);

        var targetPoint = vm.HistoricalChartPoints[2];
        vm.UpdateHoverPosition(targetPoint.CanvasX, 50.0);

        Assert.True(vm.IsHoverActive);
        Assert.Equal(targetPoint.CanvasX, vm.HoverX);
        Assert.Equal(targetPoint.Label, vm.HoverTimestampText);

        vm.ClearHover();
        Assert.False(vm.IsHoverActive);
    }
}
