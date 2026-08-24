using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using DataSense.ViewModels;
using Xunit;

namespace DataSense.Tests.ViewModels;

public class MockSpeedTestService : ISpeedTestService
{
    public double MockPing { get; set; } = 18.0;
    public double MockDownload { get; set; } = 82.4;
    public double MockUpload { get; set; } = 24.7;

    public Task<double> TestPingAsync(CancellationToken cancellationToken) => Task.FromResult(MockPing);

    public Task<double> TestDownloadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
    {
        progressCallback(MockDownload * 0.5);
        progressCallback(MockDownload);
        return Task.FromResult(MockDownload);
    }

    public Task<double> TestUploadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
    {
        progressCallback(MockUpload * 0.5);
        progressCallback(MockUpload);
        return Task.FromResult(MockUpload);
    }
}

public class SpeedTestViewModelTests : IDisposable
{
    private readonly TestDatabaseContext _dbContext;
    private readonly MockSpeedTestService _mockSpeedService;
    private readonly MockNetworkMonitorWorker _mockWorker;
    private readonly MockNetworkConnectionService _mockConnService;

    public SpeedTestViewModelTests()
    {
        _dbContext = TestDatabaseFactory.CreateAsync().GetAwaiter().GetResult();
        _mockSpeedService = new MockSpeedTestService();
        _mockWorker = new MockNetworkMonitorWorker();
        _mockConnService = new MockNetworkConnectionService();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public void InitialState_HasValidDefaultsAndTitle()
    {
        var vm = new SpeedTestViewModel(_mockSpeedService, _dbContext.Repository, _mockWorker, null, _mockConnService);

        Assert.Equal("Speed Test", vm.Title);
        Assert.Equal("READY", vm.DisplayPhaseText);
        Assert.Equal("RUN SPEED TEST", vm.ActionButtonText);
        Assert.False(vm.IsTesting);
        Assert.NotNull(vm.MeterBackgroundArc);
        Assert.NotNull(vm.MeterOuterRing);
        Assert.NotNull(vm.MeterInnerRing);
        Assert.NotNull(vm.MeterInnerDomeArc);
        Assert.NotNull(vm.ConcentricRing1);
        Assert.NotNull(vm.ConcentricRing2);
        Assert.NotNull(vm.ConcentricRing3);
        Assert.NotNull(vm.ConcentricRing4);
        Assert.NotEmpty(vm.ScaleTicks);
        Assert.NotEmpty(vm.OsPlatform);
    }

    [Fact]
    public void GaugeGeometry_CalculatesAccurateArc()
    {
        var geom = SpeedTestViewModel.CreateArcGeometry(180, 160, 120, 150, 120);

        Assert.NotNull(geom);
        Assert.NotNull(geom.Figures);
        Assert.Single(geom.Figures);
        Assert.False(geom.Figures[0].IsClosed);
        Assert.NotNull(geom.Figures[0].Segments);
        Assert.Single(geom.Figures[0].Segments);
    }

    [Fact]
    public async Task StartTestAsync_ExecutesAllPhasesAndPersistsRecord()
    {
        var vm = new SpeedTestViewModel(_mockSpeedService, _dbContext.Repository, _mockWorker, null, _mockConnService);

        await vm.StartTestAsync();

        Assert.False(vm.IsTesting);
        Assert.Equal(SpeedTestStage.Completed, vm.CurrentStage);
        Assert.Equal("COMPLETED", vm.DisplayPhaseText);
        Assert.Equal("10.3 MB/s", vm.DownloadSpeedText);
        Assert.Equal("10.3", vm.DownloadValueText);
        Assert.Equal("3.1 MB/s", vm.UploadSpeedText);
        Assert.Equal("3.1", vm.UploadValueText);
        Assert.Equal("18 ms", vm.PingText);
        Assert.Equal("18", vm.PingValueText);
        Assert.Equal("Good", vm.OverallQuality);
        Assert.Equal("MB/s", vm.DisplayUnitText);
        Assert.True(vm.HasRealtimeGraphData);

        // Verify record persisted in database
        var history = (await _dbContext.Repository.GetSpeedTestsAsync(10)).ToList();
        Assert.Single(history);
        Assert.Equal(10.3, Math.Round(history[0].DownloadSpeedMbps, 1));
        Assert.Equal(3.1, Math.Round(history[0].UploadSpeedMbps, 1));
        Assert.Equal(18.0, history[0].PingMs);
    }

    [Fact]
    public async Task CancelTest_SetsCancelledState()
    {
        var blockingService = new BlockingSpeedTestService();
        var vm = new SpeedTestViewModel(blockingService, _dbContext.Repository, _mockWorker, null, _mockConnService);

        var testTask = vm.StartTestAsync();
        vm.CancelTest();

        await testTask;

        Assert.False(vm.IsTesting);
        Assert.Equal(SpeedTestStage.Cancelled, vm.CurrentStage);
        Assert.Equal("CANCELLED", vm.DisplayPhaseText);
    }

    private class BlockingSpeedTestService : ISpeedTestService
    {
        public async Task<double> TestPingAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            return 20.0;
        }

        public Task<double> TestDownloadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
        {
            return Task.FromResult(50.0);
        }

        public Task<double> TestUploadAsync(Action<double> progressCallback, CancellationToken cancellationToken)
        {
            return Task.FromResult(20.0);
        }
    }
}
