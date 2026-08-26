using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using DataSense.ViewModels;
using Moq;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public async Task SettingsViewModel_InitialCategory_IsAppearance()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var forecastMock = new Mock<IForecastService>();
        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget());

        var vm = new SettingsViewModel(forecastMock.Object, repository: context.Repository);

        Assert.Equal(SettingsCategory.Appearance, vm.SelectedCategory);
        Assert.True(vm.IsAppearanceSelected);
        Assert.False(vm.IsDashboardSelected);
        Assert.False(vm.IsAnalyticsSelected);
        Assert.False(vm.IsNetworkSelected);
        Assert.False(vm.IsDataSelected);
        Assert.False(vm.IsNotificationsSelected);
        Assert.False(vm.IsDataLimitsSelected);
        Assert.False(vm.IsSystemSelected);
        Assert.Equal("Appearance", vm.CategoryTitle);
    }

    [Fact]
    public async Task SettingsViewModel_SelectCategory_SwitchesCorrectly()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var forecastMock = new Mock<IForecastService>();
        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget());

        var vm = new SettingsViewModel(forecastMock.Object, repository: context.Repository);

        // Test each of the 8 categories
        var categories = new[]
        {
            SettingsCategory.Appearance,
            SettingsCategory.Dashboard,
            SettingsCategory.Analytics,
            SettingsCategory.Network,
            SettingsCategory.Data,
            SettingsCategory.Notifications,
            SettingsCategory.DataLimits,
            SettingsCategory.System
        };

        foreach (var cat in categories)
        {
            vm.SelectCategoryCommand.Execute(cat);
            Assert.Equal(cat, vm.SelectedCategory);
            Assert.False(string.IsNullOrWhiteSpace(vm.CategoryTitle));
            Assert.False(string.IsNullOrWhiteSpace(vm.CategoryDescription));
        }
    }

    [Fact]
    public void ThemeService_AvailableThemes_HasAllSixThemes()
    {
        var themeService = new ThemeService();
        var themes = themeService.AvailableThemes;

        Assert.Equal(6, themes.Count);
        Assert.Contains(themes, t => t.Id == "Neon Space");
        Assert.Contains(themes, t => t.Id == "Deep Violet");
        Assert.Contains(themes, t => t.Id == "Cyber Ocean");
        Assert.Contains(themes, t => t.Id == "Aurora");
        Assert.Contains(themes, t => t.Id == "Cyber Pink");
        Assert.Contains(themes, t => t.Id == "Arctic Light");
    }

    [Fact]
    public async Task ThemeService_ApplyTheme_UpdatesCurrentThemeId()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var themeService = new ThemeService(context.Repository);

        themeService.ApplyTheme("Cyber Ocean");
        Assert.Equal("Cyber Ocean", themeService.CurrentThemeId);
        Assert.Equal("🌊  Cyber Ocean", themeService.CurrentTheme.FormattedName);
    }

    [Fact]
    public async Task ClearHistory_DestructivelyClearsTelemetry_AndUpdatesCount()
    {
        using var context = await TestDatabaseFactory.CreateAsync();

        // Seed records
        await context.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "TestNet",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow,
            BytesDownloaded = 1_000_000,
            BytesUploaded = 200_000
        });

        int countBefore = await context.Repository.GetTotalRecordCountAsync();
        Assert.True(countBefore >= 1);

        var forecastMock = new Mock<IForecastService>();
        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget());

        var vm = new SettingsViewModel(forecastMock.Object, repository: context.Repository);
        
        vm.RequestClearHistoryCommand.Execute(null);
        Assert.True(vm.IsConfirmingClearHistory);

        await vm.ConfirmClearHistoryAsync();

        int countAfter = await context.Repository.GetTotalRecordCountAsync();
        Assert.Equal(0, countAfter);
        Assert.False(vm.IsConfirmingClearHistory);
        Assert.Contains("cleared successfully", vm.ClearHistoryStatusText);
    }

    [Fact]
    public async Task DataLimits_CalculatesLiveUsageAndPercentages()
    {
        using var context = await TestDatabaseFactory.CreateAsync();

        // Seed records for today
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = DateTime.UtcNow.Date.AddHours(1),
            InterfaceName = "wlo1",
            BytesReceived = 2_000_000_000, // 2 GB
            BytesSent = 500_000_000        // 0.5 GB
        });
        await context.Repository.SaveUsageAsync(new NetworkUsage
        {
            Timestamp = DateTime.UtcNow.Date.AddHours(2),
            InterfaceName = "wlo1",
            BytesReceived = 4_500_000_000, // +2.5 GB
            BytesSent = 1_000_000_000      // +0.5 GB -> Total 3.0 GB
        });

        var forecastMock = new Mock<IForecastService>();
        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget
        {
            Enabled = true,
            DailyLimitBytes = 5L * 1024 * 1024 * 1024,   // 5 GB
            MonthlyLimitBytes = 100L * 1024 * 1024 * 1024 // 100 GB
        });

        var vm = new SettingsViewModel(forecastMock.Object, repository: context.Repository);
        await vm.LoadSettingsAsync();

        Assert.Equal(5, vm.DailyLimitGb);
        Assert.Equal(100, vm.MonthlyLimitGb);
        Assert.True(vm.DailyUsagePercent > 0);
        Assert.True(vm.MonthlyUsagePercent > 0);
        Assert.Equal("5.0 GB", vm.DailyLimitText);
        Assert.Equal("100.0 GB", vm.MonthlyLimitText);
    }

    [Fact]
    public async Task Dashboard_WidgetPreferences_PersistAndLoad()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var forecastMock = new Mock<IForecastService>();
        forecastMock.Setup(f => f.GetBudgetAsync()).ReturnsAsync(new DataBudget());

        var vm = new SettingsViewModel(forecastMock.Object, repository: context.Repository);
        await vm.LoadSettingsAsync();

        // Initially defaults are true
        Assert.True(vm.ShowSummaryCards);
        Assert.True(vm.ShowNetworkChart);
        Assert.True(vm.ShowTopConsumers);
        Assert.True(vm.ShowLiveProcessTraffic);

        // Change values
        vm.ShowSummaryCards = false;
        vm.ShowNetworkChart = false;
        vm.ShowTopConsumers = false;
        vm.ShowLiveProcessTraffic = false;

        // Save
        await vm.SaveSettingsAsync();

        // Create new instance and load
        var vm2 = new SettingsViewModel(forecastMock.Object, repository: context.Repository);
        await vm2.LoadSettingsAsync();

        Assert.False(vm2.ShowSummaryCards);
        Assert.False(vm2.ShowNetworkChart);
        Assert.False(vm2.ShowTopConsumers);
        Assert.False(vm2.ShowLiveProcessTraffic);
    }
}
