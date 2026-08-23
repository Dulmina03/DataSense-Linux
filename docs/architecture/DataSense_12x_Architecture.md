# DataSense 12.x Architecture & Master Feature Specification

## 1. Existing Architecture Summary
DataSense 11.x operates as a local-first, privacy-respecting network and process analytics framework on Linux via `.NET 10`. It features decoupled Background Workers (NetworkManager DBus hooks, `nethogs` wrappers) writing strictly to an async `SQLite` datastore. Avalonia MVVM handles the view layer, utilizing `LiveChartsCore.SkiaSharpView.Avalonia` tightly guarded by `SemaphoreSlim` threading blocks, guaranteeing that rendering never blocks the 2-second telemetry loop.

## 2. Components That Will Be Reused
- **Core Telemetry Drivers**: `ProcessNetworkMonitorWorker`, `NetworkMonitorWorker`
- **Database Subsystem**: All existing `.db` configurations, table schemas (`NetworkSession`, `ProcessTraffic`, etc.), and Dapper/EF mapping utilities.
- **Service Interfaces**: `INetworkIntelligenceService`, `IBudgetService`, `IChartDataService`
- **Charting Foundations**: `LiveChartsCore` layout architectures, `ChartColorHelper`, `ChartThemeHelper`

## 3. Components Requiring Modification
- **DashboardViewModel.cs**: Has pivoted from a static layout class into an orchestrator reading from `DashboardLayoutService`.
- **DashboardView.axaml**: The hardcoded grids have been replaced by a dynamic `ItemsControl` wrapping a Uniform/Wrap grid to support parametric layout bindings.

## 4. New Components Required
- **IDashboardLayoutService**: Core component handling `DashboardLayout` and `DashboardWidgetConfiguration` states.
- **IWidget**: Reusable interface establishing `Title`, `MinimumSize`, and unified `Refresh()` contracts.
- **IAlertService**: Background evaluation task checking `AlertCondition` limits decoupled from immediate UI logic.

## 5. Database Changes Required
- **New Tables**:
  - `UserPreferences`: KV store for widget bounds, layout definitions, and UI theme overrides.
  - `AlertRules`: Persistent definitions for threshold alerts.
  - `AlertHistory`: Event log preventing alert spam through cooldown tracking.
- **No changes to existing telemetry bounds.**

## 6. Migration Strategy
Migrations will execute via the existing transactional schema upgrader. `AlertRules` and `UserPreferences` will initialize as standalone tables. Absolutely no `DROP TABLE` operations will run against 11.x telemetry repositories.

## 7. Dashboard 2.0 Architecture
A modular system resolving an `ObservableCollection<IWidget>`. The layout is dictated by a `DashboardLayout` configuration object pulled from JSON/SQLite. The UI uses an `ItemsControl` bound to custom `DataTemplate` selectors matching widget payload types.

## 8. Widget Architecture (Phase 12.2 Update)
**`IWidget` Contract:**
```csharp
public interface IWidget
{
    string WidgetId { get; }
    string Title { get; }
    string Description { get; }
    bool IsVisible { get; set; }
    WidgetState State { get; } 
    // WidgetState Enum: Loading, Ready, InsufficientData, Unavailable, Error
    
    int MinimumWidth { get; }
    int MinimumHeight { get; }
    
    Task RefreshAsync(CancellationToken cancellationToken);
}
```

**`WidgetDescriptor` & `IWidgetRegistry`:**
Provides central mapping `(WidgetId -> WidgetDescriptor)` containing DefaultSize constraints and UI Category data. This registry prevents duplicating widget mappings inside XAML logic.

**Refresh Architecture:**
`IWidget` does **not** create duplicate DB query timers. The `DashboardViewModel` explicitly triggers `RefreshAsync` broadcasts to the widget array only upon global heartbeat broadcasts or explicit configuration mutations.

**Error Isolation:**
A `try/catch` wrapper inside each widget's `RefreshAsync` traps exceptions locally, tripping the `WidgetState.Error` enum and displaying a localized placeholder without crashing the master `DashboardViewModel`.

## 9. Analytics Architecture
Advanced analytics (e.g. week-over-week comparisons) will leverage the `IChartDataService` extensions, running `.ConfigureAwait(false)` aggregations against the existing `ProcessTraffic` tables without creating secondary "analytics_summary" tables, preserving disk space.

## 10. Alert Architecture
An `AlertWorker` running on a separate 60-second tick evaluate rules (e.g. "Network exceeds monthly threshold"). On violation, it writes to `AlertHistory` and invokes the native Linux `notify-send` daemon via DBus.

## 11. Reporting Architecture
Will introduce a dedicated `ReportGenerationService` utilizing an external (but strictly local) PDF layout builder or CSV exporter, leveraging existing telemetry projections without UI dependency.

## 12. Personalization Architecture
Stored securely inside XDG `~/.config/DataSense/preferences.json` (or SQLite `UserPreferences`). Exposes theme variables binding dynamically to Avalonia `Application.Resources`.

## 13. Performance Strategy
Chart configurations and widget bounds will maintain the `SemaphoreSlim(1,1)` constraint. Background alerts will fire strictly out-of-band to prevent dropping standard telemetry packages.

## 14. Security Strategy
- SQL Parameterization enforced across all new `AlertRules` queries.
- No `sudo` execution paths introduced for the reporting daemon.

## 15. Testing Strategy
Unit tests split into: `WidgetTests`, `AlertConditionTests`, `LayoutServiceTests`. UI tests avoided in favor of strict MVVM bounds testing against mock `IChartDataService`.

## 16. Final 12.x Roadmap
1. **Phase 12.1**: Dashboard 2.0 Foundation *(Completed)*
2. **Phase 12.2**: Modular Widget System *(Completed)*
3. **Phase 12.3**: Advanced Interactive Analytics
4. **Phase 12.7**: Alerts & Automation
5. **Phase 12.9**: Personalization & Themes
6. **Phase 12.14**: Production Release
\n## Phase 12.3: Advanced Interactive Analytics\n- **IAnalyticsComparisonService**: Compares discrete ranges (Current Value vs Previous Value) strictly returning `InsufficientData` if historical vectors are missing rather than fabricating zeroes. Prevents NaN percentage bounds.\n- **Trend Classification**: Deterministic enumerator (`StronglyIncreasing`, `Increasing`, `Stable`, `Decreasing`, `StronglyDecreasing`).\n- **Widgets**: Introduced `TrendComparisonWidget` displaying relative percentage shifts, protected by `IWidget` failure bounds.

## Phase 12.4: Historical Explorer
- **Date-Range System**: Supports abstract `Custom Range` parameters scaling the X-Axis bounds dynamically.
- **Aggregation**: `Hourly` (1-7 days), `Daily` (30 days), `Weekly` (90+ days) resolution limits enforced to protect chart rendering buffers.
- **Interactive Drill-Down**: Exposes `Network` and `Application` filter cross-joins without triggering full-table scan Cartesian traps.
