using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Models;
using DataSense.Services;
using Xunit;

namespace DataSense.Tests.Services;

public class ProcessNetworkMonitoringTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteNetworkUsageRepository _repository;

    public ProcessNetworkMonitoringTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"datasense_proc_test_{Guid.NewGuid():N}.db");
        _repository = new SqliteNetworkUsageRepository(_dbPath);
        _repository.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void NethogsParser_ParsesValidTraceLine_Successfully()
    {
        // Arrange
        var monitor = new NethogsProcessNetworkMonitor();
        string validLine = "/usr/bin/chrome/1234/5000\t150.5\t300.2";

        // Act
        var usage = monitor.ParseNethogsLine(validLine);

        // Assert
        Assert.NotNull(usage);
        Assert.Equal("chrome", usage!.ProcessIdentifier);
        Assert.Equal(1234, usage.Pid);
        Assert.Equal("5000", usage.User);
        Assert.Equal(150.5 * 1024, usage.UploadRateBytesPerSec);
        Assert.Equal(300.2 * 1024, usage.DownloadRateBytesPerSec);
        Assert.Equal("Nethogs", usage.DataSource);
    }

    [Fact]
    public void NethogsParser_HandlesSpaceSeparatedAndMalformedLines_Gracefully()
    {
        // Arrange
        var monitor = new NethogsProcessNetworkMonitor();
        string spaceLine = "/usr/bin/code/5678/dulmina 50.0 100.0";
        string invalidLine = "Refreshing: nethogs trace mode";
        string garbageLine = "random non-nethogs string";

        // Act
        var spaceUsage = monitor.ParseNethogsLine(spaceLine);
        var invalidUsage = monitor.ParseNethogsLine(invalidLine);
        var garbageUsage = monitor.ParseNethogsLine(garbageLine);

        // Assert
        Assert.NotNull(spaceUsage);
        Assert.Equal("code", spaceUsage!.ProcessIdentifier);
        Assert.Equal(5678, spaceUsage.Pid);

        Assert.Null(invalidUsage);
        Assert.Null(garbageUsage);
    }

    [Fact]
    public void LinuxProcessResolver_HandlesInvalidOrNonExistentPid_WithoutThrowing()
    {
        // Arrange
        var resolver = new LinuxProcessResolver();

        // Act
        var invalidResult = resolver.ResolveProcessIdentity(-1);
        var nonExistentResult = resolver.ResolveProcessIdentity(999999);

        // Assert
        Assert.Null(invalidResult);
        Assert.Null(nonExistentResult); // Returns null safely for non-existent /proc/999999 entry
    }

    [Fact]
    public void ProcessIdentityInfo_GeneratesUniqueCompositeKey_ForPidReuse()
    {
        // Arrange
        var info1 = new ProcessIdentityInfo
        {
            Pid = 1234,
            ProcessName = "chrome",
            StartTimeTicks = 100000
        };

        var info2 = new ProcessIdentityInfo
        {
            Pid = 1234,
            ProcessName = "chrome",
            StartTimeTicks = 200000 // Reused PID with new start time
        };

        // Assert
        Assert.Equal("chrome_1234_100000", info1.CompositeKey);
        Assert.Equal("chrome_1234_200000", info2.CompositeKey);
        Assert.NotEqual(info1.CompositeKey, info2.CompositeKey);
    }

    [Fact]
    public async Task ProcessRepository_SavesAndRetrievesExtendedProcessUsage_WithNewColumns()
    {
        // Arrange
        var record = new ProcessUsageRecord
        {
            ProcessName = "chrome",
            ExecutablePath = "/usr/bin/google-chrome",
            UserName = "dulmina",
            Timestamp = DateTime.UtcNow,
            BytesDownloaded = 1024 * 1024,
            BytesUploaded = 512 * 1024,
            DataSource = "Nethogs"
        };

        // Act
        await _repository.SaveProcessUsageAsync(record);
        var topProcs = (await _repository.GetTopProcessesAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 10)).ToList();

        // Assert
        Assert.Single(topProcs);
        var fetched = topProcs[0];
        Assert.Equal("chrome", fetched.ProcessName);
        Assert.Equal(1024 * 1024, fetched.BytesDownloaded);
        Assert.Equal(512 * 1024, fetched.BytesUploaded);
        Assert.Equal(1536 * 1024, fetched.TotalBytes);
        Assert.Equal("/usr/bin/google-chrome", fetched.ExecutablePath);
        Assert.Equal("dulmina", fetched.UserName);
        Assert.Equal("Nethogs", fetched.DataSource);
    }

    [Fact]
    public async Task ProcessMonitorWorker_PauseAndResume_ControlsMonitoringState()
    {
        // Arrange
        var mockMonitor = new MockProcessNetworkMonitor(available: true, permissions: true);
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);

        // Act & Assert
        Assert.False(worker.IsPaused);
        worker.Pause();
        Assert.True(worker.IsPaused);
        Assert.Equal("Paused", worker.MonitoringStatus);

        worker.Resume();
        Assert.False(worker.IsPaused);
    }

    [Fact]
    public async Task ApplicationIntelligence_RespectsThreeDayHistoryRule_WhenGeneratingRecommendations()
    {
        // Arrange
        var analyticsService = new AnalyticsService(_repository);
        var patternService = new PatternAnalysisService(_repository, analyticsService);
        var appIntelService = new ApplicationIntelligenceService(_repository, analyticsService, patternService);

        // Act - Fresh database with less than 3 days of history
        var profile = await appIntelService.GetApplicationProfileAsync("chrome");
        var recommendations = (await appIntelService.GenerateApplicationRecommendationsAsync()).ToList();

        // Assert
        Assert.NotNull(profile);
        Assert.False(profile!.HasSufficientData);
        Assert.Single(recommendations);
        Assert.Contains("Not enough application history", recommendations[0].Description);
    }

    [Fact]
    public async Task ExportService_ExportsApplicationsDataInCsvAndJson()
    {
        // Arrange
        var analyticsService = new AnalyticsService(_repository);
        var appAnalyticsMock = new Moq.Mock<DataSense.Services.IApplicationAnalyticsService>();
        appAnalyticsMock.Setup(x => x.GetTopApplicationsAsync(Moq.It.IsAny<int>(), default))
            .Returns(Task.FromResult((IEnumerable<ApplicationHistoricalProfile>)new List<ApplicationHistoricalProfile>
            {
                new ApplicationHistoricalProfile
                {
                    ProcessName = "code",
                    ExecutablePath = "/usr/bin/code",
                    UserName = "dulmina",
                    LastSeen = DateTime.UtcNow,
                    DownloadBytes = 2048,
                    UploadBytes = 1024,
                    DataSource = "Nethogs"
                }
            }));
        var exportService = new ExportService(_repository, analyticsService, appAnalyticsMock.Object);

        await _repository.SaveProcessUsageAsync(new ProcessUsageRecord
        {
            ProcessName = "code",
            ExecutablePath = "/usr/bin/code",
            UserName = "dulmina",
            Timestamp = DateTime.UtcNow,
            BytesDownloaded = 2048,
            BytesUploaded = 1024,
            DataSource = "Nethogs"
        });

        string exportDir = Path.Combine(Path.GetTempPath(), $"datasense_export_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(exportDir);

        try
        {
            // Act
            var csvResult = await exportService.ExportDataAsync(new ExportOptions
            {
                DataType = ExportDataType.Applications,
                Format = ExportFormat.CSV,
                OutputDirectory = exportDir,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(1)
            });

            var jsonResult = await exportService.ExportDataAsync(new ExportOptions
            {
                DataType = ExportDataType.Applications,
                Format = ExportFormat.JSON,
                OutputDirectory = exportDir,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(1)
            });

            // Assert
            Assert.True(csvResult.Success);
            Assert.Equal(1, csvResult.RecordsExported);
            Assert.True(File.Exists(csvResult.FilePath));

            Assert.True(jsonResult.Success);
            Assert.Equal(1, jsonResult.RecordsExported);
            Assert.True(File.Exists(jsonResult.FilePath));

            string csvContent = File.ReadAllText(csvResult.FilePath);
            Assert.Contains("ProcessName,ExecutablePath,UserName", csvContent);
            Assert.Contains("code", csvContent);
            Assert.Contains("Nethogs", csvContent);
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                try { Directory.Delete(exportDir, true); } catch { }
            }
        }
    }

    private class MockProcessNetworkMonitor : IProcessNetworkMonitor
    {
        private readonly bool _available;
        private readonly bool _permissions;

        public MockProcessNetworkMonitor(bool available, bool permissions)
        {
            _available = available;
            _permissions = permissions;
        }

        public Task<bool> IsAvailableAsync() => Task.FromResult(_available);
        public Task<bool> HasPermissionsAsync() => Task.FromResult(_permissions);
        public string NethogsPath => "nethogs";

        public async IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new List<ProcessNetworkUsage>
            {
                new ProcessNetworkUsage
                {
                    ProcessIdentifier = "chrome",
                    ExecutablePath = "/usr/bin/chrome",
                    Pid = 1234,
                    User = "dulmina",
                    DownloadRateBytesPerSec = 1000,
                    UploadRateBytesPerSec = 500,
                    Timestamp = DateTime.UtcNow,
                    DataSource = "Nethogs",
                    ProcessIdentityKey = "chrome_1234_100"
                }
            };
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessMonitorWorker_IntegrateProcessUsage_HandlesPidRecycling()
    {
        // Arrange
        var mockMonitor = new CustomizableMockProcessNetworkMonitor();
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);

        var batch1 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                ExecutablePath = "/usr/bin/chrome",
                Pid = 1234,
                User = "dulmina",
                DownloadRateBytesPerSec = 1000,
                UploadRateBytesPerSec = 500,
                Timestamp = DateTime.UtcNow,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        var batch2 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "firefox",
                ExecutablePath = "/usr/bin/firefox",
                Pid = 1234, // Reused PID
                User = "dulmina",
                DownloadRateBytesPerSec = 2000,
                UploadRateBytesPerSec = 1000,
                Timestamp = DateTime.UtcNow,
                DataSource = "Nethogs",
                ProcessIdentityKey = "firefox_1234_2000" // New StartTimeTicks
            }
        };

        mockMonitor.Batches.Enqueue(batch1);
        mockMonitor.Batches.Enqueue(batch2);

        // Act
        worker.Start();
        await Task.Delay(150); // Let worker process the batches
        worker.Stop();

        // Assert
        var tracked = worker.GetTrackedProcesses().ToList();
        Assert.Equal(2, tracked.Count);

        var oldProc = tracked.FirstOrDefault(p => p.ProcessName == "chrome");
        var newProc = tracked.FirstOrDefault(p => p.ProcessName == "firefox");

        Assert.NotNull(oldProc);
        Assert.NotNull(newProc);
        Assert.Equal("Recycled", oldProc!.Status);
        Assert.Equal("New", newProc!.Status);
    }

    [Fact]
    public async Task ProcessMonitorWorker_IntegrateProcessUsage_HandlesCounterResets()
    {
        // Arrange
        var mockMonitor = new CustomizableMockProcessNetworkMonitor();
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);

        var batch1 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadBytes = 5000,
                UploadBytes = 2500,
                Timestamp = DateTime.UtcNow,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        // Next sample has lower cumulative bytes (counter reset)
        var batch2 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadBytes = 1000, // Reset!
                UploadBytes = 500,    // Reset!
                Timestamp = DateTime.UtcNow,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        // Next sample has incremented bytes
        var batch3 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadBytes = 1500, // +500
                UploadBytes = 800,    // +300
                Timestamp = DateTime.UtcNow,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        mockMonitor.Batches.Enqueue(batch1);
        mockMonitor.Batches.Enqueue(batch2);
        mockMonitor.Batches.Enqueue(batch3);

        // Act
        worker.Start();
        await Task.Delay(200);
        worker.Stop();

        // Assert
        var tracked = worker.GetTrackedProcesses().ToList();
        Assert.Single(tracked);
    }

    [Fact]
    public async Task ProcessMonitorWorker_IntegrateProcessUsage_HandlesVaryingTimeDeltas()
    {
        // Arrange
        var mockMonitor = new CustomizableMockProcessNetworkMonitor();
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);

        var now = DateTime.UtcNow;

        var batch1 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadRateBytesPerSec = 1000,
                UploadRateBytesPerSec = 500,
                Timestamp = now,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        // Duplicate or zero elapsed interval (timestamp doesn't change)
        var batch2 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadRateBytesPerSec = 1000,
                UploadRateBytesPerSec = 500,
                Timestamp = now,
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        // Negative elapsed interval (timestamp is in the past)
        var batch3 = new List<ProcessNetworkUsage>
        {
            new ProcessNetworkUsage
            {
                ProcessIdentifier = "chrome",
                Pid = 1234,
                DownloadRateBytesPerSec = 1000,
                UploadRateBytesPerSec = 500,
                Timestamp = now.AddSeconds(-5),
                DataSource = "Nethogs",
                ProcessIdentityKey = "chrome_1234_1000"
            }
        };

        mockMonitor.Batches.Enqueue(batch1);
        mockMonitor.Batches.Enqueue(batch2);
        mockMonitor.Batches.Enqueue(batch3);

        // Act
        worker.Start();
        await Task.Delay(200);
        worker.Stop();

        // Assert
        var tracked = worker.GetTrackedProcesses().ToList();
        Assert.Single(tracked);
    }

    [Fact]
    public async Task DiagnosticsService_ReportsCorrectStatus_WhenProcessMonitorUnavailable()
    {
        // Arrange
        var mockMonitor = new CustomizableMockProcessNetworkMonitor { Available = false };
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);
        var healthRegistry = new SystemHealthRegistry();
        
        var diagnosticsService = new DiagnosticsService(
            healthRegistry,
            _repository,
            null,
            null,
            worker);

        // Act
        worker.Start();
        await Task.Delay(100);
        var diagnostics = (await diagnosticsService.GetDiagnosticsAsync()).ToList();
        worker.Stop();

        // Assert
        var procComp = diagnostics.FirstOrDefault(c => c.Name == "ProcessMonitor");
        Assert.NotNull(procComp);
        Assert.Equal(SubsystemState.Unavailable, procComp!.Status);
        Assert.Contains("Install nethogs", procComp.RecommendedAction);
    }

    [Fact]
    public async Task DiagnosticsService_ReportsCorrectStatus_WhenProcessMonitorPermissionDenied()
    {
        // Arrange
        var mockMonitor = new CustomizableMockProcessNetworkMonitor { Available = true, Permissions = false };
        using var worker = new ProcessNetworkMonitorWorker(mockMonitor, _repository);
        var healthRegistry = new SystemHealthRegistry();
        
        var diagnosticsService = new DiagnosticsService(
            healthRegistry,
            _repository,
            null,
            null,
            worker);

        // Act
        worker.Start();
        await Task.Delay(100);
        var diagnostics = (await diagnosticsService.GetDiagnosticsAsync()).ToList();
        worker.Stop();

        // Assert
        var procComp = diagnostics.FirstOrDefault(c => c.Name == "ProcessMonitor");
        Assert.NotNull(procComp);
        Assert.Equal(SubsystemState.Degraded, procComp!.Status);
        Assert.Contains("Grant capabilities", procComp.RecommendedAction);
    }

    private class CustomizableMockProcessNetworkMonitor : IProcessNetworkMonitor
    {
        public bool Available { get; set; } = true;
        public bool Permissions { get; set; } = true;
        public string NethogsPath => "nethogs";

        public Queue<List<ProcessNetworkUsage>> Batches { get; } = new();

        public Task<bool> IsAvailableAsync() => Task.FromResult(Available);
        public Task<bool> HasPermissionsAsync() => Task.FromResult(Permissions);

        public async IAsyncEnumerable<IEnumerable<ProcessNetworkUsage>> StartMonitoringAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (Batches.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                yield return Batches.Dequeue();
                await Task.Delay(10);
            }
        }
    }
}
