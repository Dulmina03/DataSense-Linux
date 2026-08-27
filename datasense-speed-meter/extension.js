import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import St from 'gi://St';
import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Clutter from 'gi://Clutter';

export default class DataSenseSpeedMeter extends Extension {
    enable() {
        console.error('[DataSenseSpeedMeter] enable()');
        this._removeIndicator();
        this._contract = Gio.File.new_for_path(`${GLib.get_user_runtime_dir()}/DataSense/speed-meter.json`);
        this._contractDirectory = this._contract.get_parent();
        try {
            this._directoryMonitor = this._contractDirectory.monitor_directory(Gio.FileMonitorFlags.NONE, null);
            this._directoryMonitor.connect('changed', (_monitor, file) => {
                if (file && file.get_basename() === 'speed-meter.json') {
                    this._updateSpeed();
                    if (this._indicator && !this._timeoutId) this._scheduleUpdate(this._nextIntervalMs);
                }
            });
        } catch (_) {
            this._directoryMonitor = null;
        }
        this._nextIntervalMs = 1000;
        this._updateSpeed();
        if (this._indicator) this._scheduleUpdate(this._nextIntervalMs);
    }

    _scheduleUpdate(intervalMs) {
        if (this._timeoutId) GLib.Source.remove(this._timeoutId);
        this._timeoutId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, intervalMs, () => {
            this._timeoutId = null;
            this._updateSpeed();
            if (this._indicator) this._scheduleUpdate(this._nextIntervalMs);
            return GLib.SOURCE_REMOVE;
        });
    }

    _ensureIndicator(position) {
        if (this._indicator) return;
        console.error('[DataSenseSpeedMeter] creating indicator');
        this._indicator = new PanelMenu.Button(0.0, 'DataSenseSpeedMeter', false);
        this._box = new St.BoxLayout({ style_class: 'panel-status-menu-box' });
        this._downloadLabel = new St.Label({ y_align: Clutter.ActorAlign.CENTER });
        this._uploadLabel = new St.Label({ y_align: Clutter.ActorAlign.CENTER });
        this._box.add_child(this._downloadLabel);
        this._box.add_child(this._uploadLabel);
        this._indicator.add_child(this._box);
        this._indicator.reactive = true;
        this._indicator.set_opacity(255);
        this._indicatorPosition = position;
        const box = position === 'Left area' ? 'left' : position === 'Center area' ? 'center' : 'right';
        Main.panel.addToStatusArea('DataSenseSpeedMeter', this._indicator, 0, box);
        this._indicator.show();
        /* Keep the labels separate so separate download/upload colors work. */
        this._downloadLabel.set_style('margin-right: 6px;');
        console.error('[DataSenseSpeedMeter] panel insertion successful');
    }

    _setLabel(label, text, color, visible) {
        label.set_text(text);
        label.visible = visible;
        label.set_style(`margin-right: 6px;${color ? ` color: ${color};` : ''}`);
    }

    _removeIndicator() {
        if (this._indicator) {
            this._indicator.destroy();
            this._indicator = null;
            this._box = null;
            this._downloadLabel = null;
            this._uploadLabel = null;
        }
        this._clickHandler = null;
        this._indicatorPosition = null;
    }

    _formatSpeed(bytesPerSec, units, precision) {
        const decimals = precision === '0 decimals' ? 0 : precision === '2 decimals' ? 2 : 1;
        const unitsMap = {
            'B/s': [1, 'B/s'], 'KB/s': [1024, 'KB/s'], 'MB/s': [1024 ** 2, 'MB/s'], 'GB/s': [1024 ** 3, 'GB/s'],
            'bits/s': [1 / 8, 'bits/s'], 'Kbit/s': [1000 / 8, 'Kbit/s'], 'Mbit/s': [1000 ** 2 / 8, 'Mbit/s'], 'Gbit/s': [1000 ** 3 / 8, 'Gbit/s']
        };
        let divisor = 1;
        let suffix = 'B/s';
        if (units === 'Auto') {
            const prefixes = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
            while (divisor < 1024 ** 3 && bytesPerSec / divisor >= 1024) divisor *= 1024;
            suffix = prefixes[Math.round(Math.log(divisor) / Math.log(1024))];
        } else if (unitsMap[units]) {
            [divisor, suffix] = unitsMap[units];
        }
        const value = bytesPerSec / divisor;
        return `${value.toFixed(decimals)} ${suffix}`;
    }

    _formatBytes(bytes) {
        const prefixes = ['B', 'KB', 'MB', 'GB', 'TB'];
        let value = Math.max(0, bytes || 0);
        let index = 0;
        while (value >= 1024 && index < prefixes.length - 1) {
            value /= 1024;
            index++;
        }
        return `${value.toFixed(index === 0 ? 0 : 1)} ${prefixes[index]}`;
    }

    _updateSpeed() {
        try {
            if (!this._contract.query_exists(null)) {
                this._removeIndicator();
                return;
            }
            const [, contents] = this._contract.load_contents(null);
            const data = JSON.parse(new TextDecoder().decode(contents));
            if (!data.enabled) { this._removeIndicator(); return; }
            this._nextIntervalMs = [250, 500, 1000, 2000, 5000].includes(data.refreshIntervalMs) ? data.refreshIntervalMs : 1000;
            if (this._indicator && this._indicatorPosition !== data.position) this._removeIndicator();
            this._ensureIndicator(data.position || 'Right area');
            const fontSize = data.size === 'Small' ? 10 : data.size === 'Large' ? 14 : 12;
            const fontWeight = data.fontWeight === 'Bold' ? 'bold' : data.fontWeight === 'Medium' ? '500' : 'normal';
            const singleColor = data.colorMode === 'Single color'
                ? data.singleColor
                : null;
            const downloadColor = singleColor || (data.colorMode === 'Separate colors' ? data.downloadColor : data.themeDownloadColor || data.themeColor);
            const uploadColor = singleColor || (data.colorMode === 'Separate colors' ? data.uploadColor : data.themeUploadColor || data.themeColor);
            const separator = data.compactMode ? '' : '  ';
            this._setLabel(this._downloadLabel, `${data.showIcons ? '↓ ' : ''}${this._formatSpeed(data.download, data.units, data.precision)}${separator}`, downloadColor, data.showDownload);
            this._setLabel(this._uploadLabel, `${data.showIcons ? '↑ ' : ''}${this._formatSpeed(data.upload, data.units, data.precision)}`, uploadColor, data.showUpload);
            this._downloadLabel.set_style(`font-size: ${fontSize}px; font-weight: ${fontWeight}; margin-right: ${data.compactMode ? 6 : 12}px; color: ${downloadColor};`);
            this._uploadLabel.set_style(`font-size: ${fontSize}px; font-weight: ${fontWeight}; color: ${uploadColor};`);
            if (data.showDetailsOnHover) {
                this._indicator.set_tooltip_text(`Download: ${this._formatSpeed(data.download, data.units, data.precision)}\nUpload: ${this._formatSpeed(data.upload, data.units, data.precision)}\nTotal downloaded: ${this._formatBytes(data.totalDownloaded)}\nTotal uploaded: ${this._formatBytes(data.totalUploaded)}\nInterface: ${data.activeInterface}`);
            } else {
                this._indicator.set_tooltip_text('');
            }
            this._setClickAction(data.clickAction);
        } catch (error) {
            this._removeIndicator();
            console.error(`[DataSenseSpeedMeter] update failed: ${error}`);
        }
    }

    _setClickAction(action) {
        if (!this._indicator) return;
        if (this._clickHandler) this._indicator.disconnect(this._clickHandler);
        this._clickHandler = this._indicator.connect('button-press-event', () => {
            if (action === 'Do nothing') return Clutter.EVENT_PROPAGATE;
            try {
                const command = 'DataSense';
                const info = Gio.AppInfo.create_from_commandline(command, 'DataSense', Gio.AppInfoCreateFlags.NONE);
                info.launch([], new Gio.AppLaunchContext());
            } catch (e) { console.error('Failed to launch DataSense', e); }
            return Clutter.EVENT_STOP;
        });
    }

    disable() {
        if (this._timeoutId) {
            GLib.Source.remove(this._timeoutId);
            this._timeoutId = null;
        }
        if (this._directoryMonitor) {
            this._directoryMonitor.cancel();
            this._directoryMonitor = null;
        }
        this._removeIndicator();
        this._contract = null;
    }
}
