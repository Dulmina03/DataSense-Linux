# Changelog
All notable changes to DataSense will be documented in this file.

## [1.0.0] - Release Candidate

### Core Telemetry
- Implemented robust internal SQLite metrics aggregation logic capturing sub-second network interface counters seamlessly.

### Linux Integration & Process Monitoring
- Completed native `nethogs` hooks to read socket payload chunks accurately isolating Linux processes (e.g. `chrome` vs `apt`).
- Integrated dynamic `NetworkManager` probing recognizing transition points between active interfaces (Wi-Fi, Ethernet).

### Analytics & Visualization
- Overhauled the complete UI surface integrating `LiveChartsCore.SkiaSharpView.Avalonia`.
- Deployed a highly scalable WrapPanel design supporting window viewports gracefully from 1280x720 to 4K resolutions.
- Re-styled all chart tooltips unifying the layout for byte-conversion calculations and historical timeline bounds.
- Established the 'Unified Intelligence' system aggregating application vs network usage without triggering redundant SQL pulls.

### Budget & Forecasting
- Designed the Exponentially Weighted Moving Average (EWMA) engine enabling determinable exhaustion thresholds for constrained network plans (e.g., Mobile Hotspots).

### UX & Packaging
- Introduced safe loading, unavailable, and insufficient-data visual overlays standardizing edge-case presentation.
- Output a single native Ubuntu `.deb` containing internal .NET abstractions decoupled from external dependencies.
