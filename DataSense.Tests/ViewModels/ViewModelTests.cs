using System;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using DataSense.Tests.Services;
using DataSense.ViewModels;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class ViewModelTests
{
    [Fact]
    public async Task NetworkAnalyticsViewModel_InitialState_IsClean()
    {
        using var context = await TestDatabaseFactory.CreateAsync();
        var analytics = new AnalyticsService(context.Repository);
        var monitor = new MockNetworkMonitorWorker();
        var connService = new MockNetworkConnectionService();
        var intelService = new NetworkIntelligenceService(analytics, connService);
        var procIntelService = new ProcessNetworkIntelligenceService(context.Repository, new Moq.Mock<ILinuxProcessResolver>().Object);
        var correlationService = new Moq.Mock<IApplicationNetworkCorrelationService>().Object;
        var vm = new NetworkAnalyticsViewModel(analytics, monitor, intelService, procIntelService, correlationService, new LiveMonitoringEngine());

        Assert.Equal(AnalyticsPeriod.Last7Days, vm.SelectedPeriod);
        Assert.False(vm.IsCurrentlyConnected);
        Assert.Equal("—", vm.CurrentConnectionType);
        Assert.Equal("—", vm.LiveDownloadSpeed);
    }
}
