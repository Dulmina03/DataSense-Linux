using System;
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
    public async Task SwitchingPeriod_ToToday_UpdatesLabelAndComputesTodayRange()
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

        Assert.Equal(HistoryPeriodType.Today, vm.SelectedPeriod);
        Assert.True(vm.IsTodayActive);
        Assert.False(vm.Is7DaysActive);
        Assert.False(vm.IsMonthActive);
        Assert.Equal("AVG / HOUR", vm.AverageUsageLabel);

        var (start, end) = vm.ComputeDateRange();
        Assert.Equal(DateTime.UtcNow.Date, start.Date);
    }

    [Fact]
    public async Task SwitchingPeriod_ToMonth_UpdatesAvailableMonthsAndRange()
    {
        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);

        Assert.Equal(HistoryPeriodType.Month, vm.SelectedPeriod);
        Assert.True(vm.IsMonthActive);
        Assert.NotEmpty(vm.AvailableMonths);
        Assert.NotNull(vm.SelectedMonth);

        var (start, end) = vm.ComputeDateRange();
        Assert.Equal(1, start.Day);
        Assert.True(end > start);
    }

    [Fact]
    public async Task SearchText_FiltersApplicationsAndSessions_Accurately()
    {
        // Seed database with sample sessions and process records
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

        // Initially both are present
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
