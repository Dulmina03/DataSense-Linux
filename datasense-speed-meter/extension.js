import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import St from 'gi://St';
import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Clutter from 'gi://Clutter';
import { SpeedMonitor } from './speed-monitor.js';

export default class DataSenseSpeedMeter extends Extension {
    enable() {
        console.log('[DataSenseSpeedMeter] enable() started');
        this._monitor = new SpeedMonitor();
        this._timeoutId = 0;
        this._reloadTimeoutId = 0;
        this._fileMonitor = null;
        this._clickHandlerId = 0;
        this._hoverHandlerId = 0;
        this._currentBoxId = 'right';
        this._download = 0;
        this._upload = 0;
        this._totalDownloaded = 0;
        this._totalUploaded = 0;
        this._activeInterface = 'Unknown';
        this._hasContractTelemetry = false;
        this._config = this._defaultConfig();

        const runtimeDir = GLib.get_user_runtime_dir();
        this._contract = Gio.File.new_for_path(`${runtimeDir}/DataSense/speed-meter.json`);

        // Setup top bar indicator exactly as in e22ba65
        this._indicator = new PanelMenu.Button(0.0, 'DataSenseSpeedMeter', false);
        this._label = new St.Label({
            text: '↓ 0 B/s ↑ 0 B/s',
            y_align: Clutter.ActorAlign.CENTER
        });
        this._indicator.add_child(this._label);

        this._buildPopupMenu();

        Main.panel.addToStatusArea('datasense-speed-meter', this._indicator);
        console.log('[DataSenseSpeedMeter] panel insertion successful');

        this._clickHandlerId = this._indicator.connect('button-press-event', (_actor, event) => {
            if (event.get_button() === 1)
                this._handleClickAction();
            return Clutter.EVENT_PROPAGATE;
        });
        this._hoverHandlerId = this._indicator.connect('notify::hover', () => this._render());

        this._startFileMonitor();
        this._loadContract();
        this._render();
        this._restartTimeout();
    }

    _defaultConfig() {
        return {
            enabled: true,
            showDownload: true,
            showUpload: true,
            showIcons: true,
            compactMode: true,
            units: 'Auto',
            precision: '1 decimal',
            colorMode: 'Theme colors',
            singleColor: '#d8e4f2',
            downloadColor: '#62d2a2',
            uploadColor: '#f4b860',
            themeColor: '#d8e4f2',
            themeDownloadColor: '#62d2a2',
            themeUploadColor: '#f4b860',
            refreshIntervalMs: 1000,
            size: 'Medium',
            fontWeight: 'Normal',
            position: 'Right area',
            clickAction: 'Open Dashboard',
            showDetailsOnHover: true
        };
    }

    _buildPopupMenu() {
        this._networkItem = new PopupMenu.PopupMenuItem('Network: Unknown', { reactive: false });
        this._indicator.menu.addMenuItem(this._networkItem);

        this._connectionItem = new PopupMenu.PopupMenuItem('Connection: Unknown', { reactive: false });
        this._indicator.menu.addMenuItem(this._connectionItem);

        this._statusItem = new PopupMenu.PopupMenuItem('Status: Connected', { reactive: false });
        this._indicator.menu.addMenuItem(this._statusItem);

        this._indicator.menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());

        this._monitoringItem = new PopupMenu.PopupMenuItem('DataSense: Checking...', { reactive: false });
        this._indicator.menu.addMenuItem(this._monitoringItem);

        this._indicator.menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());

        let openDataSense = new PopupMenu.PopupMenuItem('Open DataSense');
        openDataSense.connect('activate', () => this._launchDataSense());
        this._indicator.menu.addMenuItem(openDataSense);
    }

    _startFileMonitor() {
        try {
            const dir = this._contract.get_parent();
            if (dir) {
                if (!dir.query_exists(null))
                    dir.make_directory_with_parents(null);
                this._fileMonitor = dir.monitor_directory(Gio.FileMonitorFlags.NONE, null);
                this._fileMonitor.set_rate_limit(50);
                this._fileMonitor.connect('changed', (_monitor, file) => {
                    if (file && file.get_basename() === 'speed-meter.json')
                        this._scheduleContractReload();
                });
            }
        } catch (e) {
            console.log(`[DataSenseSpeedMeter] file monitor unavailable: ${e}`);
        }
    }

    _scheduleContractReload() {
        if (this._reloadTimeoutId)
            GLib.Source.remove(this._reloadTimeoutId);
        this._reloadTimeoutId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 50, () => {
            this._reloadTimeoutId = 0;
            const previousInterval = this._config.refreshIntervalMs;
            this._loadContract();
            this._render();
            if (this._config.refreshIntervalMs !== previousInterval)
                this._restartTimeout();
            return GLib.SOURCE_REMOVE;
        });
    }

    _loadContract() {
        try {
            if (!this._contract || !this._contract.query_exists(null)) {
                this._hasContractTelemetry = false;
                return;
            }

            const [, contents] = this._contract.load_contents(null);
            const data = JSON.parse(new TextDecoder().decode(contents));
            if (!data || typeof data !== 'object')
                return;

            this._applyConfigFromContract(data);
            if (data.enabled === false) {
                this._hasContractTelemetry = false;
                return;
            }

            if (typeof data.download === 'number')
                this._download = Math.max(0, data.download);
            if (typeof data.upload === 'number')
                this._upload = Math.max(0, data.upload);
            if (typeof data.totalDownloaded === 'number')
                this._totalDownloaded = Math.max(0, data.totalDownloaded);
            if (typeof data.totalUploaded === 'number')
                this._totalUploaded = Math.max(0, data.totalUploaded);
            if (typeof data.activeInterface === 'string' && data.activeInterface)
                this._activeInterface = data.activeInterface;
            this._hasContractTelemetry = data.enabled !== false;
        } catch (e) {
            this._hasContractTelemetry = false;
        }
    }

    _applyConfigFromContract(data) {
        const cfg = this._config;
        if (typeof data.enabled === 'boolean')
            cfg.enabled = data.enabled;
        if (typeof data.showDownload === 'boolean')
            cfg.showDownload = data.showDownload;
        if (typeof data.showUpload === 'boolean')
            cfg.showUpload = data.showUpload;
        if (typeof data.showIcons === 'boolean')
            cfg.showIcons = data.showIcons;
        if (typeof data.compactMode === 'boolean')
            cfg.compactMode = data.compactMode;
        if (typeof data.units === 'string' && data.units)
            cfg.units = data.units;
        if (typeof data.precision === 'string' && data.precision)
            cfg.precision = data.precision;
        if (typeof data.colorMode === 'string' && data.colorMode)
            cfg.colorMode = data.colorMode;
        if (typeof data.singleColor === 'string' && data.singleColor)
            cfg.singleColor = data.singleColor;
        if (typeof data.downloadColor === 'string' && data.downloadColor)
            cfg.downloadColor = data.downloadColor;
        if (typeof data.uploadColor === 'string' && data.uploadColor)
            cfg.uploadColor = data.uploadColor;
        if (typeof data.themeColor === 'string' && data.themeColor)
            cfg.themeColor = data.themeColor;
        if (typeof data.themeDownloadColor === 'string' && data.themeDownloadColor)
            cfg.themeDownloadColor = data.themeDownloadColor;
        if (typeof data.themeUploadColor === 'string' && data.themeUploadColor)
            cfg.themeUploadColor = data.themeUploadColor;
        if (typeof data.refreshIntervalMs === 'number' && data.refreshIntervalMs > 0)
            cfg.refreshIntervalMs = data.refreshIntervalMs;
        if (typeof data.size === 'string' && data.size)
            cfg.size = data.size;
        if (typeof data.fontWeight === 'string' && data.fontWeight)
            cfg.fontWeight = data.fontWeight;
        if (typeof data.position === 'string' && data.position)
            cfg.position = data.position;
        if (typeof data.clickAction === 'string' && data.clickAction)
            cfg.clickAction = data.clickAction;
        if (typeof data.showDetailsOnHover === 'boolean')
            cfg.showDetailsOnHover = data.showDetailsOnHover;
    }

    _checkDataSenseStatus() {
        try {
            const [, stdout, , status] = GLib.spawn_command_line_sync('pgrep -f DataSense');
            if (status === 0 && stdout && stdout.length > 0)
                return 'Active';
            return 'Not Running';
        } catch (e) {
            return 'Unknown';
        }
    }

    _formatSpeed(bytesPerSec, units = 'Auto', precision = '1 decimal') {
        const decimals = precision === '0 decimals' ? 0 : precision === '2 decimals' ? 2 : 1;
        if (units === 'Auto') {
            if (bytesPerSec < 1024) return `${Math.round(bytesPerSec)} B/s`;
            if (bytesPerSec < 1048576) return `${(bytesPerSec / 1024).toFixed(decimals)} KB/s`;
            if (bytesPerSec < 1073741824) return `${(bytesPerSec / 1048576).toFixed(decimals)} MB/s`;
            return `${(bytesPerSec / 1073741824).toFixed(decimals)} GB/s`;
        }
        const unitsMap = {
            'B/s': [1, 'B/s'], 'KB/s': [1024, 'KB/s'], 'MB/s': [1024 ** 2, 'MB/s'], 'GB/s': [1024 ** 3, 'GB/s'],
            'bits/s': [1 / 8, 'bits/s'], 'Kbit/s': [1000 / 8, 'Kbit/s'], 'Mbit/s': [1000 ** 2 / 8, 'Mbit/s'], 'Gbit/s': [1000 ** 3 / 8, 'Gbit/s']
        };
        const [divisor, suffix] = unitsMap[units] || [1, 'B/s'];
        return `${(bytesPerSec / divisor).toFixed(decimals)} ${suffix}`;
    }

    _formatBytes(bytes) {
        if (bytes < 1024) return `${Math.round(bytes)} B`;
        if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1073741824) return `${(bytes / 1048576).toFixed(1)} MB`;
        return `${(bytes / 1073741824).toFixed(1)} GB`;
    }

    _escapeMarkup(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    _sanitizeColor(value, fallback) {
        return /^#[0-9A-Fa-f]{6}$/.test(value || '') ? value : fallback;
    }

    _colorFor(kind) {
        const cfg = this._config;
        if (cfg.colorMode === 'Single color')
            return this._sanitizeColor(cfg.singleColor, '#d8e4f2');
        if (cfg.colorMode === 'Separate colors') {
            return kind === 'download'
                ? this._sanitizeColor(cfg.downloadColor, '#62d2a2')
                : this._sanitizeColor(cfg.uploadColor, '#f4b860');
        }
        return kind === 'download'
            ? this._sanitizeColor(cfg.themeDownloadColor || cfg.themeColor, '#62d2a2')
            : this._sanitizeColor(cfg.themeUploadColor || cfg.themeColor, '#f4b860');
    }

    _applyStyle(extra = '') {
        const sizeMap = { Small: '11px', Medium: '13px', Large: '16px' };
        const weightMap = { Normal: '400', Medium: '500', Bold: '700' };
        const fontSize = sizeMap[this._config.size] || sizeMap.Medium;
        const fontWeight = weightMap[this._config.fontWeight] || weightMap.Normal;
        this._label.set_style(`font-size: ${fontSize}; font-weight: ${fontWeight};${extra}`);
    }

    _applyPosition() {
        if (!this._indicator)
            return;
        const boxId = this._config.position === 'Left area'
            ? 'left'
            : this._config.position === 'Center area'
                ? 'center'
                : 'right';

        const boxes = {
            left: Main.panel._leftBox,
            center: Main.panel._centerBox,
            right: Main.panel._rightBox
        };
        const target = boxes[boxId];
        if (!target)
            return;

        const actor = this._indicator;
        const parent = actor.get_parent();
        if (parent === target) {
            this._currentBoxId = boxId;
            return;
        }
        try {
            if (parent)
                parent.remove_child(actor);
            if (boxId === 'right')
                target.insert_child_at_index(actor, 0);
            else
                target.add_child(actor);
            this._currentBoxId = boxId;
        } catch (e) {
            console.log(`[DataSenseSpeedMeter] position '${this._config.position}' could not be applied: ${e}`);
        }
    }

    _handleClickAction() {
        const action = this._config.clickAction;
        if (action === 'Do nothing')
            return;
        this._launchDataSense();
    }

    _launchDataSense() {
        try {
            let context = new Gio.AppLaunchContext();
            let info = Gio.AppInfo.create_from_commandline('DataSense', 'DataSense', Gio.AppInfoCreateFlags.NONE);
            info.launch([], context);
        } catch (e) {
            console.error('Failed to launch DataSense', e);
        }
    }

    _updateSpeed() {
        this._loadContract();
        if (this._config.enabled === false) {
            this._render();
            return GLib.SOURCE_CONTINUE;
        }
        if (!this._hasContractTelemetry && this._monitor) {
            const res = this._monitor.readSpeed();
            this._download = res.download;
            this._upload = res.upload;
            this._activeInterface = res.interface;
            if (!res.active) {
                this._renderOffline(res);
                return GLib.SOURCE_CONTINUE;
            }
        }
        this._render();
        return GLib.SOURCE_CONTINUE;
    }

    _renderOffline(res) {
        if (!this._indicator)
            return;
        if (this._config.enabled === false) {
            this._indicator.hide();
            return;
        }
        this._indicator.show();
        this._applyStyle();
        this._applyPosition();
        if (this._label.clutter_text)
            this._label.clutter_text.set_use_markup(false);
        this._label.set_text('Network Offline');
        this._networkItem.label.set_text(`Network: ${res?.interface || 'None'}`);
        this._connectionItem.label.set_text('Connection: Offline');
        this._statusItem.label.set_text('Status: Disconnected');
        if (this._indicator.menu && this._indicator.menu.isOpen)
            this._monitoringItem.label.set_text(`DataSense: ${this._checkDataSenseStatus()}`);
    }

    _render() {
        if (!this._indicator || !this._label)
            return;

        if (this._config.enabled === false) {
            this._indicator.hide();
            return;
        }
        this._indicator.show();
        this._applyStyle();
        this._applyPosition();

        const cfg = this._config;
        const dl = this._formatSpeed(this._download, cfg.units, cfg.precision);
        const ul = this._formatSpeed(this._upload, cfg.units, cfg.precision);
        const dlText = cfg.showDownload ? `${cfg.showIcons ? '↓ ' : ''}${dl}` : '';
        const ulText = cfg.showUpload ? `${cfg.showIcons ? '↑ ' : ''}${ul}` : '';
        const sep = (dlText && ulText) ? (cfg.compactMode ? ' ' : '  ') : '';
        let display = `${dlText}${sep}${ulText}`;
        if (!display)
            display = '';

        const hovering = this._indicator.hover && cfg.showDetailsOnHover;
        if (hovering) {
            const details = `${this._activeInterface}  ↓ ${this._formatBytes(this._totalDownloaded)}  ↑ ${this._formatBytes(this._totalUploaded)}`;
            display = display ? `${display} · ${details}` : details;
        }

        if ((dlText || ulText) && !hovering) {
            const parts = [];
            if (dlText)
                parts.push(`<span foreground="${this._colorFor('download')}">${this._escapeMarkup(dlText)}</span>`);
            if (ulText)
                parts.push(`<span foreground="${this._colorFor('upload')}">${this._escapeMarkup(ulText)}</span>`);
            this._label.clutter_text.set_use_markup(true);
            this._label.clutter_text.set_markup(parts.join(this._escapeMarkup(sep)));
        } else {
            this._label.clutter_text.set_use_markup(false);
            this._label.set_text(display);
            if (cfg.colorMode === 'Single color') {
                const color = this._sanitizeColor(cfg.singleColor, '#d8e4f2');
                this._applyStyle(` color: ${color};`);
            }
        }

        this._networkItem.label.set_text(`Network: ${this._activeInterface || 'Unknown'}`);
        this._connectionItem.label.set_text('Connection: Active');
        this._statusItem.label.set_text('Status: Connected');

        if (this._indicator.menu && this._indicator.menu.isOpen)
            this._monitoringItem.label.set_text(`DataSense: ${this._hasContractTelemetry ? 'Active' : this._checkDataSenseStatus()}`);
    }

    _restartTimeout() {
        if (this._timeoutId) {
            GLib.Source.remove(this._timeoutId);
            this._timeoutId = 0;
        }
        const interval = Math.max(250, this._config.refreshIntervalMs || 1000);
        this._timeoutId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, interval, () => this._updateSpeed());
    }

    disable() {
        if (this._timeoutId) {
            GLib.Source.remove(this._timeoutId);
            this._timeoutId = null;
        }
        if (this._reloadTimeoutId) {
            GLib.Source.remove(this._reloadTimeoutId);
            this._reloadTimeoutId = 0;
        }
        if (this._fileMonitor) {
            this._fileMonitor.cancel();
            this._fileMonitor = null;
        }
        if (this._indicator && this._clickHandlerId)
            this._indicator.disconnect(this._clickHandlerId);
        if (this._indicator && this._hoverHandlerId)
            this._indicator.disconnect(this._hoverHandlerId);
        this._clickHandlerId = 0;
        this._hoverHandlerId = 0;
        if (this._indicator) {
            this._indicator.destroy();
            this._indicator = null;
        }
        this._monitor = null;
        this._contract = null;
    }
}
