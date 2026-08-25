using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Helpers;
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
    public async Task TodayFourHourAggregation_GeneratesExactly6FourHourBuckets_WithRealCounters()
    {
        var today = DateTime.UtcNow.Date;

        // Hour 10 cumulative counter progression (belongs to Bucket 2: 08–12)
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

        // Hour 14 cumulative counter progression (belongs to Bucket 3: 12–16)
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
        Assert.Equal(6, vm.HistoricalChartPoints.Count);

        // Bucket 2: 08–12
        Assert.Equal("08–12", vm.HistoricalChartPoints[2].Label);
        Assert.Equal(2_000_000, vm.HistoricalChartPoints[2].DownloadBytes);
        Assert.Equal(500_000, vm.HistoricalChartPoints[2].UploadBytes);

        // Bucket 3: 12–16
        Assert.Equal("12–16", vm.HistoricalChartPoints[3].Label);
        Assert.Equal(3_000_000, vm.HistoricalChartPoints[3].DownloadBytes);
        Assert.Equal(1_000_000, vm.HistoricalChartPoints[3].UploadBytes);

        // Bucket 0: 00–04
        Assert.Equal("00–04", vm.HistoricalChartPoints[0].Label);
        Assert.Equal(0, vm.HistoricalChartPoints[0].DownloadBytes);
    }

    [Fact]
    public async Task SevenDayAggregation_Produces7Points_AndHandlesZeroUsageDays()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectLast7Days();
        var (start, _) = vm.ComputeDateRange();
        var targetDay = start.Date.AddDays(1); // Tuesday of Monday->Sunday week

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

        await vm.LoadAsync(showLoading: false);

        Assert.Equal(7, vm.HistoricalChartPoints.Count);
        Assert.Equal("Mon", vm.HistoricalChartPoints[0].Label);
        Assert.Equal("Sun", vm.HistoricalChartPoints[6].Label);

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
        Assert.Equal(1, vm.HistoricalChartPoints[0].Timestamp.Day);
        Assert.Equal(expectedDays, vm.HistoricalChartPoints[^1].Timestamp.Day);

        // Chart #2 remains 12 months
        Assert.Equal(12, vm.TwelveMonthChartPoints.Count);
    }

    [Fact]
    public async Task TwelveMonthUsageBreakdown_AlwaysMaintains12Months_JanThroughDec()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        await vm.LoadAsync(showLoading: false);

        Assert.Equal(12, vm.TwelveMonthChartPoints.Count);
        Assert.Equal("Jan", vm.TwelveMonthChartPoints[0].Label);
        Assert.Equal("Dec", vm.TwelveMonthChartPoints[11].Label);
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

        // 1. Switch to Today (6 buckets)
        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(6, vm.HistoricalChartPoints.Count);
        Assert.True(vm.HistoricalChartPoints[3].DownloadBarHeight > 0);

        // 2. Switch to 7 Days (7 buckets)
        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(7, vm.HistoricalChartPoints.Count);

        // 3. Switch to Month (28-31 buckets)
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
        Assert.True(vm.ApplicationBreakdownItems.Count >= 2);

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
        Assert.Equal(targetPoint.FullTitle, vm.HoverTimestampText);

        vm.ClearHover();
        Assert.False(vm.IsHoverActive);

        // Also test TwelveMonth hover
        Assert.NotEmpty(vm.TwelveMonthChartPoints);
        var targetMonthPoint = vm.TwelveMonthChartPoints[3];
        vm.UpdateTwelveMonthHoverPosition(targetMonthPoint.CanvasX, 50.0);

        Assert.True(vm.IsTwelveMonthHoverActive);
        Assert.Equal(targetMonthPoint.CanvasX, vm.TwelveMonthHoverX);
        Assert.Equal(targetMonthPoint.FullTitle, vm.TwelveMonthHoverTimestampText);

        vm.ClearTwelveMonthHover();
        Assert.False(vm.IsTwelveMonthHoverActive);
    }

    [Fact]
    public async Task ApplicationUsageBreakdown_SynchronizesWithPeriodAndMonth_AccuratelyCalculatingDlUlTotalShare()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Add application records for Today
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 2_000_000,
                BytesUploaded = 500_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "code",
                BytesDownloaded = 1_000_000,
                BytesUploaded = 500_000,
                Timestamp = today.AddHours(3)
            }
        });

        // Add NetworkUsage so totalUsage aligns
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(1),
            InterfaceName = "eth0",
            BytesReceived = 10_000_000,
            BytesSent = 1_000_000
        });
        await _dbContext.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = today.AddHours(4),
            InterfaceName = "eth0",
            BytesReceived = 13_000_000, // Delta = 3,000,000
            BytesSent = 2_000_000       // Delta = 1,000,000 -> Total = 4,000,000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // 1. TODAY
        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        Assert.Equal("Application network usage for today", vm.ApplicationBreakdownSubtitle);
        Assert.Equal(2, vm.Applications.Count);

        var chrome = vm.Applications.FirstOrDefault(a => a.ProcessName == "chrome");
        Assert.NotNull(chrome);
        Assert.Equal(2_000_000, chrome.DownloadBytes);
        Assert.Equal(500_000, chrome.UploadBytes);
        Assert.Equal(2_500_000, chrome.TotalBytes);
        // Total period traffic is 4,000,000. Chrome share: 2,500,000 / 4,000,000 = 62.5%
        Assert.Equal(62.5, chrome.PercentageOfTotal, 1);

        // 2. 7 DAYS
        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal("Application network usage for the last 7 days", vm.ApplicationBreakdownSubtitle);

        // 3. MONTH
        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);
        Assert.Contains("Application network usage for", vm.ApplicationBreakdownSubtitle);
    }

    [Fact]
    public async Task NetworkSessions_DisplaysLiveSession_AndGroupsMonthlyByNetworkName()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 2 sessions on "SLT Fiber"
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "Wi-Fi",
            StartTime = startOfMonth.AddDays(2).AddHours(8),
            EndTime = startOfMonth.AddDays(2).AddHours(12),
            BytesDownloaded = 4_000_000,
            BytesUploaded = 1_000_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "Wi-Fi",
            StartTime = startOfMonth.AddDays(3).AddHours(14),
            EndTime = null, // Live active session!
            BytesDownloaded = 2_000_000,
            BytesUploaded = 500_000
        });

        // 1 session on "Dialog 4G"
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Dialog 4G",
            InterfaceName = "usb0",
            ConnectionType = "Cellular",
            StartTime = startOfMonth.AddDays(1).AddHours(9),
            EndTime = startOfMonth.AddDays(1).AddHours(11),
            BytesDownloaded = 1_000_000,
            BytesUploaded = 200_000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // Load Month
        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);

        // 1. Network Usage state verified
        Assert.True(vm.HasNetworkUsage);
        Assert.Equal("MONTHLY NETWORK USAGE", vm.NetworkUsageSectionTitle);

        // 2. Monthly aggregated groups: Exactly 2 networks
        Assert.Equal(2, vm.NetworkUsageItems.Count);
        var slt = vm.NetworkUsageItems.FirstOrDefault(m => m.NetworkName == "SLT Fiber");
        Assert.NotNull(slt);
        Assert.Equal(6_000_000, slt.BytesDownloaded); // 4M + 2M
        Assert.Equal(1_500_000, slt.BytesUploaded);   // 1M + 0.5M
        Assert.Equal(7_500_000, slt.TotalBytes);

        var dialog = vm.NetworkUsageItems.FirstOrDefault(m => m.NetworkName == "Dialog 4G");
        Assert.NotNull(dialog);
        Assert.Equal(1_000_000, dialog.BytesDownloaded);
        Assert.Equal(200_000, dialog.BytesUploaded);
        Assert.Equal(1_200_000, dialog.TotalBytes);

        // 3. Monthly totals
        Assert.NotEmpty(vm.MonthlyTotalDownloadText);
        Assert.NotEmpty(vm.MonthlyTotalUploadText);
        Assert.NotEmpty(vm.MonthlyTotalUsageText);
    }

    [Fact]
    public async Task ApplicationUsageBreakdown_MultiProcessAggregation_SumsDlUlAndCalculatesExactShare()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        // Multiple slices of the same app across the day
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 1_000_000,
                BytesUploaded = 200_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 2_000_000,
                BytesUploaded = 300_000,
                Timestamp = today.AddHours(4)
            },
            new ProcessUsageRecord
            {
                ProcessName = "firefox",
                BytesDownloaded = 1_000_000,
                BytesUploaded = 500_000,
                Timestamp = today.AddHours(3)
            }
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

        Assert.True(vm.HasApplications);
        Assert.Equal(2, vm.Applications.Count);

        // Chrome: 3M dl + 500k ul = 3.5M total
        var chrome = vm.Applications.FirstOrDefault(a => a.ProcessName == "chrome");
        Assert.NotNull(chrome);
        Assert.Equal(3_000_000, chrome.DownloadBytes);
        Assert.Equal(500_000, chrome.UploadBytes);
        Assert.Equal(3_500_000, chrome.TotalBytes);

        // Firefox: 1M dl + 500k ul = 1.5M total
        var firefox = vm.Applications.FirstOrDefault(a => a.ProcessName == "firefox");
        Assert.NotNull(firefox);
        Assert.Equal(1_000_000, firefox.DownloadBytes);
        Assert.Equal(500_000, firefox.UploadBytes);
        Assert.Equal(1_500_000, firefox.TotalBytes);

        // Grand app total = 3.5M + 1.5M = 5.0M
        // Chrome share: 3.5M / 5.0M = 70.0%
        // Firefox share: 1.5M / 5.0M = 30.0%
        Assert.Equal(70.0, chrome.PercentageOfTotal, 1);
        Assert.Equal(30.0, firefox.PercentageOfTotal, 1);
        Assert.Equal(100.0, chrome.PercentageOfTotal + firefox.PercentageOfTotal, 1);
    }

    [Fact]
    public async Task ApplicationUsageBreakdown_MonthSwitching_ReloadsCorrectMonthTelemetry()
    {
        var now = DateTime.UtcNow;
        var augStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var julStart = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        // August record: steam (5 GB)
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "steam",
                BytesDownloaded = 5_000_000_000,
                BytesUploaded = 100_000_000,
                Timestamp = augStart.AddDays(5)
            }
        });

        // July record: discord (2 GB)
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "discord",
                BytesDownloaded = 1_800_000_000,
                BytesUploaded = 200_000_000,
                Timestamp = julStart.AddDays(10)
            }
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();

        // 1. Select August 2026
        vm.SelectedMonth = new MonthSelectItem { Year = 2026, Month = 8, DisplayName = "August 2026" };
        await vm.LoadAsync(showLoading: false);

        Assert.True(vm.HasApplications);
        Assert.Single(vm.Applications);
        Assert.Equal("steam", vm.Applications[0].ProcessName);
        Assert.Equal(5_100_000_000, vm.Applications[0].TotalBytes);

        // 2. Switch to July 2026
        vm.SelectedMonth = new MonthSelectItem { Year = 2026, Month = 7, DisplayName = "July 2026" };
        await vm.LoadAsync(showLoading: false);

        Assert.True(vm.HasApplications);
        Assert.Single(vm.Applications);
        Assert.Equal("discord", vm.Applications[0].ProcessName);
        Assert.Equal(2_000_000_000, vm.Applications[0].TotalBytes);
    }

    [Fact]
    public async Task ApplicationUsageBreakdown_EmptyState_DisplaysNoFabricatedApps()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        Assert.False(vm.HasApplications);
        Assert.Empty(vm.Applications);
        Assert.Empty(vm.FilteredApplications);
        Assert.Equal("0 B", vm.TotalApplicationUsageText);
    }

    [Fact]
    public async Task ApplicationUsageBreakdown_SearchAndSorting_FunctionsAccurately()
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 5_000_000,
                BytesUploaded = 100_000,
                Timestamp = today.AddHours(1)
            },
            new ProcessUsageRecord
            {
                ProcessName = "vscode",
                BytesDownloaded = 1_000_000,
                BytesUploaded = 8_000_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "spotify",
                BytesDownloaded = 2_000_000,
                BytesUploaded = 50_000,
                Timestamp = today.AddHours(3)
            }
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

        Assert.Equal(3, vm.Applications.Count);

        // Sort by Total (Desc): vscode (9M) > chrome (5.1M) > spotify (2.05M)
        vm.SelectedSortOption = "Total (Desc)";
        Assert.Equal("vscode", vm.FilteredApplications[0].ProcessName);

        // Sort by Download (Desc): chrome (5M) > spotify (2M) > vscode (1M)
        vm.SelectedSortOption = "Download (Desc)";
        Assert.Equal("chrome", vm.FilteredApplications[0].ProcessName);

        // Sort by Upload (Desc): vscode (8M) > chrome (100k) > spotify (50k)
        vm.SelectedSortOption = "Upload (Desc)";
        Assert.Equal("vscode", vm.FilteredApplications[0].ProcessName);

        // Search filtering: "spo"
        vm.SearchText = "spo";
        Assert.Single(vm.FilteredApplications);
        Assert.Equal("spotify", vm.FilteredApplications[0].ProcessName);

        // Search non-existent: "xyz999"
        vm.SearchText = "xyz999";
        Assert.Empty(vm.FilteredApplications);
        Assert.False(vm.HasApplications);
    }

    [Fact]
    public async Task PeriodTransitions_Today_SevenDays_Month_CorrectlyFiltersApplications()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        int diffToMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = today.AddDays(-diffToMonday);

        // Record 1: Today -> "app_today"
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "app_today",
                BytesDownloaded = 100_000,
                BytesUploaded = 10_000,
                Timestamp = today.AddHours(1)
            }
        });

        // Record 2: Yesterday (if different from today, within current week) or Monday
        var weekDay = monday == today ? today : monday.AddHours(2);
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "app_week",
                BytesDownloaded = 500_000,
                BytesUploaded = 50_000,
                Timestamp = weekDay
            }
        });

        // Record 3: Past month (e.g. 60 days ago)
        var pastMonthDate = today.AddDays(-60);
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "app_past_month",
                BytesDownloaded = 900_000,
                BytesUploaded = 90_000,
                Timestamp = pastMonthDate
            }
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // 1. TODAY -> Should ONLY contain app_today (or app_week if monday == today)
        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);
        Assert.Contains(vm.Applications, a => a.ProcessName == "app_today");
        Assert.DoesNotContain(vm.Applications, a => a.ProcessName == "app_past_month");

        // 2. 7 DAYS -> Should contain weekly apps, not past month
        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);
        Assert.Contains(vm.Applications, a => a.ProcessName == "app_today");
        Assert.DoesNotContain(vm.Applications, a => a.ProcessName == "app_past_month");

        // 3. Switch to past month item
        vm.SelectMonth();
        vm.SelectedMonth = new MonthSelectItem
        {
            Year = pastMonthDate.Year,
            Month = pastMonthDate.Month,
            DisplayName = pastMonthDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
        };
        await vm.LoadAsync(showLoading: false);
        Assert.Contains(vm.Applications, a => a.ProcessName == "app_past_month");
        Assert.DoesNotContain(vm.Applications, a => a.ProcessName == "app_today");
    }

    [Fact]
    public async Task RapidPeriodSwitching_ConcurrencyGuard_EnsuresLatestSelectionWins()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
        var pastMonth = today.AddMonths(-2);

        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "app_today_only",
                BytesDownloaded = 1_000_000,
                BytesUploaded = 100_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "app_past_only",
                BytesDownloaded = 2_000_000,
                BytesUploaded = 200_000,
                Timestamp = pastMonth.AddDays(5)
            }
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // Rapidly switch periods
        vm.SelectMonth();
        vm.SelectedMonth = new MonthSelectItem
        {
            Year = pastMonth.Year,
            Month = pastMonth.Month,
            DisplayName = pastMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture)
        };
        var task1 = vm.LoadAsync(showLoading: true);

        vm.SelectToday();
        var task2 = vm.LoadAsync(showLoading: true);

        await Task.WhenAll(task1, task2);

        // The final displayed applications MUST match TODAY
        Assert.True(vm.HasApplications);
        Assert.Contains(vm.Applications, a => a.ProcessName == "app_today_only");
        Assert.DoesNotContain(vm.Applications, a => a.ProcessName == "app_past_only");
    }

    [Fact]
    public async Task OverviewAndCharts_MatchApplicationBreakdownTotals_Daily7DayMonthly()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // Add distinct process usage records across today
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 4_000_000,
                BytesUploaded = 1_000_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "firefox",
                BytesDownloaded = 2_000_000,
                BytesUploaded = 500_000,
                Timestamp = today.AddHours(4)
            }
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        // 1. TODAY
        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        // Total app usage: 6M dl + 1.5M ul = 7.5M
        Assert.Equal(vm.TotalUsageText, vm.TotalApplicationUsageText);
        Assert.Equal(vm.TotalDownloadedText, vm.TotalApplicationDownloadText);
        Assert.Equal(vm.TotalUploadedText, vm.TotalApplicationUploadText);

        // Network usage must match total usage
        Assert.True(vm.HasNetworkUsage);
        Assert.Equal(6_000_000, vm.NetworkUsageItems.Sum(n => n.BytesDownloaded));
        Assert.Equal(1_500_000, vm.NetworkUsageItems.Sum(n => n.BytesUploaded));
        Assert.Equal(7_500_000, vm.NetworkUsageItems.Sum(n => n.TotalBytes));

        // Chart #1 sum must match
        long chart1TotalDl = vm.HistoricalChartPoints.Sum(p => p.DownloadBytes);
        long chart1TotalUl = vm.HistoricalChartPoints.Sum(p => p.UploadBytes);
        Assert.Equal(6_000_000, chart1TotalDl);
        Assert.Equal(1_500_000, chart1TotalUl);

        // 2. 7 DAYS
        vm.SelectLast7Days();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(vm.TotalUsageText, vm.TotalApplicationUsageText);
        Assert.Equal(vm.TotalDownloadedText, vm.TotalApplicationDownloadText);
        Assert.Equal(vm.TotalUploadedText, vm.TotalApplicationUploadText);
        Assert.True(vm.HasNetworkUsage);
        Assert.Equal(7_500_000, vm.NetworkUsageItems.Sum(n => n.TotalBytes));

        // 3. MONTH
        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);
        Assert.Equal(vm.TotalUsageText, vm.TotalApplicationUsageText);
        Assert.Equal(vm.TotalDownloadedText, vm.TotalApplicationDownloadText);
        Assert.Equal(vm.TotalUploadedText, vm.TotalApplicationUploadText);
        Assert.True(vm.HasNetworkUsage);
        Assert.Equal(7_500_000, vm.NetworkUsageItems.Sum(n => n.TotalBytes));
        Assert.Equal(vm.TotalUsageText, vm.MonthlyTotalUsageText);
        Assert.Equal(vm.TotalDownloadedText, vm.MonthlyTotalDownloadText);
        Assert.Equal(vm.TotalUploadedText, vm.MonthlyTotalUploadText);
    }

    [Fact]
    public async Task NetworkUsage_WithMultipleRecordedSessions_ProportionallyHarmonizesToMatchPeriodTotal()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // Seed Process Usage
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 8_000_000,
                BytesUploaded = 2_000_000,
                Timestamp = today.AddHours(2)
            }
        });

        // Seed two network sessions (e.g. 75% Wi-Fi, 25% Mobile)
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "Wi-Fi",
            StartTime = today.AddHours(1),
            EndTime = today.AddHours(3),
            BytesDownloaded = 750_000,
            BytesUploaded = 750_000
        });
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Dialog 4G",
            InterfaceName = "wlo1",
            ConnectionType = "Wi-Fi",
            StartTime = today.AddHours(3),
            EndTime = today.AddHours(5),
            BytesDownloaded = 250_000,
            BytesUploaded = 250_000
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

        Assert.True(vm.HasNetworkUsage);
        Assert.Equal(2, vm.NetworkUsageItems.Count);

        // Sum of network usage MUST equal total period usage (10,000,000)
        long totalNetDl = vm.NetworkUsageItems.Sum(n => n.BytesDownloaded);
        long totalNetUl = vm.NetworkUsageItems.Sum(n => n.BytesUploaded);
        Assert.Equal(8_000_000, totalNetDl);
        Assert.Equal(2_000_000, totalNetUl);
        Assert.Equal(10_000_000, totalNetDl + totalNetUl);

        var slt = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "SLT Fiber");
        var dialog = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "Dialog 4G");
        Assert.NotNull(slt);
        Assert.NotNull(dialog);

        // SLT Fiber had 75% share -> 6.0M dl + 1.5M ul = 7.5M
        Assert.Equal(7_500_000, slt.TotalBytes);
        // Dialog 4G had 25% share -> 2.0M dl + 0.5M ul = 2.5M
        Assert.Equal(2_500_000, dialog.TotalBytes);
    }

    [Fact]
    public async Task DashboardAndHistory_DataUsage_ExactMatch_TodayAndMonth()
    {
        var now = DateTime.UtcNow;
        var today = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);

        // Seed Process Usage
        await _dbContext.Repository.SaveProcessUsageBatchAsync(new List<ProcessUsageRecord>
        {
            new ProcessUsageRecord
            {
                ProcessName = "chrome",
                BytesDownloaded = 15_000_000,
                BytesUploaded = 5_000_000,
                Timestamp = today.AddHours(2)
            },
            new ProcessUsageRecord
            {
                ProcessName = "slack",
                BytesDownloaded = 10_000_000,
                BytesUploaded = 2_000_000,
                Timestamp = today.AddHours(4)
            }
        });

        // 1. History ViewModel Today
        var historyVm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        historyVm.SelectToday();
        await historyVm.LoadAsync(showLoading: false);

        // 2. Query Repository Summaries & AnalyticsService (used by Dashboard)
        var (dashTodayDl, dashTodayUl) = await _dbContext.Repository.GetTodaySummaryAsync();
        var (dashMonthDl, dashMonthUl) = await _dbContext.Repository.GetMonthSummaryAsync();
        var analyticsService = new AnalyticsService(_dbContext.Repository);
        var analyticsSummaryToday = await analyticsService.GetSummaryAsync(AnalyticsPeriod.Today);

        // Verify Dashboard Today summary matches History Today Overview exactly
        Assert.Equal(historyVm.TotalDownloadedText, ByteFormatter.FormatBytes(dashTodayDl));
        Assert.Equal(historyVm.TotalUploadedText, ByteFormatter.FormatBytes(dashTodayUl));
        Assert.Equal(historyVm.TotalUsageText, ByteFormatter.FormatBytes(dashTodayDl + dashTodayUl));
        Assert.Equal(historyVm.TotalUsageText, ByteFormatter.FormatBytes(analyticsSummaryToday.TotalUsage));

        // 3. History ViewModel Month
        historyVm.SelectMonth();
        await historyVm.LoadAsync(showLoading: false);

        // Verify Dashboard Month summary matches History Month Overview exactly
        Assert.Equal(historyVm.TotalDownloadedText, ByteFormatter.FormatBytes(dashMonthDl));
        Assert.Equal(historyVm.TotalUploadedText, ByteFormatter.FormatBytes(dashMonthUl));
        Assert.Equal(historyVm.TotalUsageText, ByteFormatter.FormatBytes(dashMonthDl + dashMonthUl));
    }
}
