# DataSense

DataSense is a privacy-first, local-only network and application telemetry dashboard tailored for Linux/Ubuntu. It tracks data usage per network connection, logs bandwidth spikes, and monitors individual application payload allocations strictly without relying on cloud analytics.

## Supported Platform
- Architecture: `linux-x64`
- Tested OS: Ubuntu 24.04 (Debian 12 compatible)
- Environment: X11 / Wayland

## Installation
To install the DataSense native Ubuntu package, run:
```bash
sudo apt install ./datasense_1.0.0_amd64.deb
```
The application will install into `/opt/datasense` and place a launcher in your application menu.

## Uninstallation
To remove DataSense:
```bash
sudo apt remove datasense
```
*Note: Uninstalling the application will **not** delete your historical telemetry data.* If you wish to wipe your historical data, manually delete: `~/.local/state/DataSense`.

## Privacy & Security
- **Local First**: DataSense uses SQLite to map bandwidth usage securely on your local disk.
- **No Uploads**: No cloud APIs are pinged. Your telemetry stays completely private.
- **Speed Test**: The optional network speed test is the **only** feature that pushes packets externally to verify ping/latency, and it only occurs upon explicit user request.

## Process Monitoring (Nethogs)
Tracking per-application data requires the `nethogs` daemon backend to read socket tables. If DataSense displays "Process monitoring requires setup", you must install `nethogs` or adjust its `setcap` permissions as outlined in our repository wiki. DataSense runs fine without it, but application-specific drill-downs will be disabled.

## Troubleshooting
- **Application does not start**: Verify your system contains X11/Wayland components (e.g. `libx11-6`).
- **No network history**: Ensure `NetworkManager` is active, as DataSense relies on `nmcli` for connection identities.
- **Charts show 'Insufficient Data'**: Simply wait; charting requires at least a few minutes of telemetry bounds to draw an Area plot.
