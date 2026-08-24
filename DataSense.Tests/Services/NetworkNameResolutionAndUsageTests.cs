using System;
using System.Linq;
using System.Threading.Tasks;
using DataSense.Database;
using DataSense.Helpers;
using DataSense.Models;
using DataSense.Services;
using DataSense.Tests.Helpers;
using DataSense.ViewModels;
using Moq;
using Xunit;

namespace DataSense.Tests.Services;

public class NetworkNameResolutionAndUsageTests : IDisposable
{
    private readonly TestDatabaseContext _dbContext;
    private readonly HistoricalAnalyticsService _historicalService;
    private readonly ApplicationAnalyticsService _appAnalyticsService;
    private readonly LinuxApplicationIconService _iconService;
    private readonly ApplicationChartColorProvider _colorProvider;
    private readonly MockNetworkMonitorWorker _monitorWorker;

    public NetworkNameResolutionAndUsageTests()
    {
        _dbContext = TestDatabaseFactory.CreateAsync().GetAwaiter().GetResult();
        _historicalService = new HistoricalAnalyticsService(_dbContext.Repository);
        _appAnalyticsService = new ApplicationAnalyticsService(_dbContext.Repository, new LinuxProcessResolver());
        _iconService = new LinuxApplicationIconService();
        _colorProvider = new ApplicationChartColorProvider();
        _monitorWorker = new MockNetworkMonitorWorker();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Network Name Resolution Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveNetworkName_WifiWithValidSsid_ReturnsSsid()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "SLT Fiber",
            ConnectionName = "SLT Fiber Profile"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "wlo1");
        Assert.Equal("SLT Fiber", resolved);
    }

    [Fact]
    public void ResolveNetworkName_WifiWithEmptySsid_FallsBackToConnectionNameOrInterface()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "—",
            ConnectionName = "Dialog 4G"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "wlan0");
        Assert.Equal("Dialog 4G", resolved);

        var detailsNoConn = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "",
            ConnectionName = "—"
        };
        var resolvedIface = NetworkSessionManager.ResolveNetworkName(detailsNoConn, "wlan0");
        Assert.Equal("Interface: wlan0", resolvedIface);
    }

    [Fact]
    public void ResolveNetworkName_PhoneHotspot_ReturnsExactHotspotName()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "Galaxy A04s",
            ConnectionName = "Galaxy A04s"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "wlo1");
        Assert.Equal("Galaxy A04s", resolved);
    }

    [Fact]
    public void ResolveNetworkName_Ethernet_ReturnsEthernet()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "ethernet",
            ConnectionName = "Wired connection 1"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "eno1");
        Assert.Equal("Ethernet", resolved);
    }

    [Fact]
    public void ResolveNetworkName_UnknownInterface_FallsBackToInterface()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "Unknown",
            WifiSsid = "—",
            ConnectionName = "—"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "eth0");
        Assert.Equal("Interface: eth0", resolved);
    }

    [Fact]
    public void ResolveNetworkName_SsidWithSpaces_PreservesSpaces()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "SLT Fiber - High Speed 5G",
            ConnectionName = "SLT Fiber Profile"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "wlp2s0");
        Assert.Equal("SLT Fiber - High Speed 5G", resolved);
    }

    [Fact]
    public void ResolveNetworkName_SsidWithSpecialCharacters_PreservesCharacters()
    {
        var details = new NetworkConnectionDetails
        {
            ConnectionType = "wifi",
            WifiSsid = "Office@5GHz_Secure#1!",
            ConnectionName = "Office Profile"
        };

        var resolved = NetworkSessionManager.ResolveNetworkName(details, "wlo1");
        Assert.Equal("Office@5GHz_Secure#1!", resolved);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Aggregation & ViewModel Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DailyNetworkUsage_CombinesMultipleSessionsOnSameNetwork()
    {
        var today = DateTime.UtcNow.Date;

        // Session 1: SLT Fiber 500MB dl, 100MB ul
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(8),
            EndTime = today.AddHours(10),
            BytesDownloaded = 500_000_000,
            BytesUploaded = 100_000_000
        });

        // Session 2: SLT Fiber 700MB dl, 150MB ul
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(11),
            EndTime = today.AddHours(13),
            BytesDownloaded = 700_000_000,
            BytesUploaded = 150_000_000
        });

        // Session 3: Galaxy A04s 300MB dl, 50MB ul
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Galaxy A04s",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(15),
            EndTime = today.AddHours(16),
            BytesDownloaded = 300_000_000,
            BytesUploaded = 50_000_000
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
        Assert.Equal("DAILY NETWORK USAGE", vm.NetworkUsageSectionTitle);
        Assert.Equal("TODAY", vm.NetworkUsageHeaderBadge);
        Assert.Equal(2, vm.NetworkUsageItems.Count);

        var slt = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "SLT Fiber");
        Assert.NotNull(slt);
        Assert.Equal(1_200_000_000, slt.BytesDownloaded); // 500M + 700M
        Assert.Equal(250_000_000, slt.BytesUploaded);     // 100M + 150M
        Assert.Equal(1_450_000_000, slt.TotalBytes);      // Download + Upload = Total
        Assert.Equal("SLT Fiber", slt.DisplayName);
        Assert.Equal("wifi • wlo1", slt.SubtitleText);

        var galaxy = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "Galaxy A04s");
        Assert.NotNull(galaxy);
        Assert.Equal(300_000_000, galaxy.BytesDownloaded);
        Assert.Equal(50_000_000, galaxy.BytesUploaded);
        Assert.Equal(350_000_000, galaxy.TotalBytes);
        Assert.Equal("Galaxy A04s", galaxy.DisplayName);
        Assert.Equal("wifi • wlo1", galaxy.SubtitleText);
    }

    [Fact]
    public async Task MonthlyNetworkUsage_CalculatesTotals_AndMatchesMonthlyFooter()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "SLT Fiber",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = startOfMonth.AddDays(1).AddHours(8),
            EndTime = startOfMonth.AddDays(1).AddHours(12),
            BytesDownloaded = 10_000_000_000,
            BytesUploaded = 2_000_000_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Dialog 4G",
            InterfaceName = "usb0",
            ConnectionType = "cellular",
            StartTime = startOfMonth.AddDays(5).AddHours(9),
            EndTime = startOfMonth.AddDays(5).AddHours(14),
            BytesDownloaded = 4_000_000_000,
            BytesUploaded = 1_000_000_000
        });

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker);

        vm.SelectMonth();
        await vm.LoadAsync(showLoading: false);

        Assert.True(vm.HasNetworkUsage);
        Assert.Equal("MONTHLY NETWORK USAGE", vm.NetworkUsageSectionTitle);
        Assert.Equal(2, vm.NetworkUsageItems.Count);

        long sumDownloaded = vm.NetworkUsageItems.Sum(n => n.BytesDownloaded);
        long sumUploaded = vm.NetworkUsageItems.Sum(n => n.BytesUploaded);
        long sumTotal = vm.NetworkUsageItems.Sum(n => n.TotalBytes);

        Assert.Equal(14_000_000_000, sumDownloaded);
        Assert.Equal(3_000_000_000, sumUploaded);
        Assert.Equal(17_000_000_000, sumTotal);

        // Verify that sumTotal equals sumDownloaded + sumUploaded
        Assert.Equal(sumTotal, sumDownloaded + sumUploaded);
    }

    [Theory]
    [InlineData("-", false)]
    [InlineData("--", false)]
    [InlineData("—", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Unknown", false)]
    [InlineData("unknown network", false)]
    [InlineData("Wi-Fi", false)]
    [InlineData("wifi", false)]
    [InlineData("Wireless", false)]
    [InlineData("Mobile Hotspot", false)]
    [InlineData("Hotspot", false)]
    [InlineData("Connected Network", false)]
    [InlineData("None", false)]
    [InlineData("Disconnected", false)]
    [InlineData("Interface: wlo1", false)]
    [InlineData("uom.wireless", true)]
    [InlineData("UoM.Wireless", true)]
    [InlineData("SLT Fiber", true)]
    [InlineData("Galaxy A04s", true)]
    [InlineData("Dialog 4G", true)]
    [InlineData("Café WiFi 5GHz", true)]
    [InlineData("Pawan's Note 12", true)]
    [InlineData("Vihangi's GALAXY S25", true)]
    public void NetworkIdentityValidator_CorrectlyValidatesNames(string input, bool expectedValid)
    {
        bool isValid = NetworkIdentityValidator.IsValidNetworkName(input);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public async Task HistoricalIntegrity_NetworkSwitch_AttributionToEachPeriod()
    {
        var today = DateTime.UtcNow.Date;

        // Sequence: uom.wireless -> Galaxy A04s -> uom.wireless
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "uom.wireless",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(9),
            EndTime = today.AddHours(12),
            BytesDownloaded = 2_000_000,
            BytesUploaded = 500_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Galaxy A04s",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(12),
            EndTime = today.AddHours(14),
            BytesDownloaded = 1_000_000,
            BytesUploaded = 200_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "uom.wireless",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(14),
            EndTime = today.AddHours(17),
            BytesDownloaded = 3_000_000,
            BytesUploaded = 800_000
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

        Assert.Equal(2, vm.NetworkUsageItems.Count);

        var uom = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "uom.wireless");
        Assert.NotNull(uom);
        Assert.Equal(5_000_000, uom.BytesDownloaded); // 2M + 3M
        Assert.Equal(1_300_000, uom.BytesUploaded);   // 500k + 800k
        Assert.Equal(6_300_000, uom.TotalBytes);

        var galaxy = vm.NetworkUsageItems.FirstOrDefault(n => n.NetworkName == "Galaxy A04s");
        Assert.NotNull(galaxy);
        Assert.Equal(1_000_000, galaxy.BytesDownloaded);
        Assert.Equal(200_000, galaxy.BytesUploaded);
        Assert.Equal(1_200_000, galaxy.TotalBytes);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. INetworkIdentityService & Unified Identity Tests
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NetworkIdentityService_WifiAndHotspot_ResolvesCorrectIdentity()
    {
        var mockConnService = new Mock<INetworkConnectionService>();
        mockConnService.Setup(c => c.GetConnectionDetailsAsync("wlo1"))
            .ReturnsAsync(new NetworkConnectionDetails
            {
                InterfaceName = "wlo1",
                ConnectionType = "wifi",
                WifiSsid = "uom.wireless",
                ConnectionName = "uom.wireless"
            });

        var service = new NetworkIdentityService(mockConnService.Object);
        var identity = await service.GetCurrentIdentityAsync("wlo1");

        Assert.Equal("uom.wireless", identity.DisplayName);
        Assert.Equal("uom.wireless", identity.Ssid);
        Assert.Equal(NetworkType.WiFi, identity.Type);
        Assert.True(identity.IsConnected);
        Assert.Equal("uom.wireless", identity.CanonicalKey);
    }

    [Fact]
    public async Task NetworkIdentityService_TemporaryDropout_MaintainsLastKnownIdentity()
    {
        var mockConnService = new Mock<INetworkConnectionService>();
        
        // First query: valid SSID
        mockConnService.SetupSequence(c => c.GetConnectionDetailsAsync("wlo1"))
            .ReturnsAsync(new NetworkConnectionDetails
            {
                InterfaceName = "wlo1",
                ConnectionType = "wifi",
                WifiSsid = "uom.wireless",
                ConnectionName = "uom.wireless"
            })
            // Second query: temporary dropout (empty SSID)
            .ReturnsAsync(new NetworkConnectionDetails
            {
                InterfaceName = "wlo1",
                ConnectionType = "wifi",
                WifiSsid = "—",
                ConnectionName = "None"
            });

        var service = new NetworkIdentityService(mockConnService.Object);

        var first = await service.GetCurrentIdentityAsync("wlo1");
        Assert.Equal("uom.wireless", first.DisplayName);

        var second = await service.GetCurrentIdentityAsync("wlo1");
        // Should preserve last known identity instead of emitting dash or Unknown Network!
        Assert.Equal("uom.wireless", second.DisplayName);
    }

    [Fact]
    public async Task NetworkIdentityService_CanonicalGrouping_MergesCaseInsensitiveNames()
    {
        var today = DateTime.UtcNow.Date;

        // Save mixed-case session records
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "uom.wireless",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(8),
            EndTime = today.AddHours(10),
            BytesDownloaded = 1_000_000,
            BytesUploaded = 200_000
        });

        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "UOM.WIRELESS",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(11),
            EndTime = today.AddHours(13),
            BytesDownloaded = 2_000_000,
            BytesUploaded = 300_000
        });

        var mockConn = new Mock<INetworkConnectionService>();
        var idService = new NetworkIdentityService(mockConn.Object);

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker,
            idService);

        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        // Grouped into exactly 1 network entry
        Assert.Single(vm.NetworkUsageItems);
        var item = vm.NetworkUsageItems[0];
        Assert.Equal("uom.wireless", item.DisplayName, ignoreCase: true);
        Assert.Equal(3_000_000, item.BytesDownloaded);
        Assert.Equal(500_000, item.BytesUploaded);
        Assert.Equal(3_500_000, item.TotalBytes);
    }

    [Fact]
    public async Task IntelligentMigration_CleansInvalidPlaceholdersSafely()
    {
        // Add a placeholder session
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "-",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow,
            BytesDownloaded = 500_000,
            BytesUploaded = 50_000
        });

        // Initialize repository triggers migration
        await _dbContext.Repository.InitializeAsync();

        var sessions = await _dbContext.Repository.GetSessionsAsync(DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(1));
        var migrated = sessions.FirstOrDefault(s => s.InterfaceName == "wlo1");
        
        Assert.NotNull(migrated);
        // Should not be "-" or empty
        Assert.False(string.IsNullOrWhiteSpace(migrated.NetworkName));
        Assert.NotEqual("-", migrated.NetworkName);
        Assert.NotEqual("--", migrated.NetworkName);
        Assert.NotEqual("—", migrated.NetworkName);
    }

    [Fact]
    public async Task NetworkSessions_InterfaceFallback_ResolvesToDashboardIdentityAndMerges()
    {
        var today = DateTime.UtcNow.Date;

        // Session 1 previously saved as "Interface: wlo1"
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "Interface: wlo1",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(9),
            EndTime = today.AddHours(11),
            BytesDownloaded = 1_500_000_000,
            BytesUploaded = 200_000_000
        });

        // Session 2 saved as "uom.wireless"
        await _dbContext.Repository.SaveSessionAsync(new NetworkSession
        {
            NetworkName = "uom.wireless",
            InterfaceName = "wlo1",
            ConnectionType = "wifi",
            StartTime = today.AddHours(11),
            EndTime = today.AddHours(13),
            BytesDownloaded = 2_000_000_000,
            BytesUploaded = 300_000_000
        });

        var mockConn = new Mock<INetworkConnectionService>();
        mockConn.Setup(c => c.GetConnectionDetailsAsync("wlo1"))
            .ReturnsAsync(new NetworkConnectionDetails
            {
                InterfaceName = "wlo1",
                ConnectionType = "wifi",
                WifiSsid = "uom.wireless",
                ConnectionName = "uom.wireless"
            });

        var idService = new NetworkIdentityService(mockConn.Object);
        _monitorWorker.ActiveInterface = "wlo1";

        var vm = new HistoryViewModel(
            _dbContext.Repository,
            _historicalService,
            _appAnalyticsService,
            _iconService,
            _colorProvider,
            _monitorWorker,
            idService);

        vm.SelectToday();
        await vm.LoadAsync(showLoading: false);

        // All sessions on wlo1 must be merged into "uom.wireless"
        Assert.Single(vm.NetworkUsageItems);
        var item = vm.NetworkUsageItems[0];
        Assert.Equal("uom.wireless", item.DisplayName);
        Assert.Equal(3_500_000_000, item.BytesDownloaded); // 1.5GB + 2.0GB
        Assert.Equal(500_000_000, item.BytesUploaded);     // 200MB + 300MB
        Assert.Equal(4_000_000_000, item.TotalBytes);
    }
}
