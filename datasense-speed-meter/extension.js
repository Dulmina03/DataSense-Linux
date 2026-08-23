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
        this._monitor = new SpeedMonitor();
        
        // Setup top bar indicator
        this._indicator = new PanelMenu.Button(0.0, 'DataSenseSpeedMeter', false);
        this._label = new St.Label({
            text: '↓ 0 B/s ↑ 0 B/s',
            y_align: Clutter.ActorAlign.CENTER
        });
        this._indicator.add_child(this._label);
        
        // Popup Menu Construction
        this._buildPopupMenu();

        Main.panel.addToStatusArea('datasense-speed-meter', this._indicator);

        this._timeoutId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 1000, () => {
            this._updateSpeed();
            return GLib.SOURCE_CONTINUE;
        });
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
        openDataSense.connect('activate', () => {
            try {
                let context = new Gio.AppLaunchContext();
                let info = Gio.AppInfo.create_from_commandline('DataSense', 'DataSense', Gio.AppInfoCreateFlags.NONE);
                info.launch([], context);
            } catch (e) {
                console.error('Failed to launch DataSense', e);
            }
        });
        this._indicator.menu.addMenuItem(openDataSense);
    }

    _checkDataSenseStatus() {
        try {
            // Lightweight check via pgrep to see if the C# process is alive
            const [, stdout, stderr, status] = GLib.spawn_command_line_sync('pgrep -f DataSense.dll');
            if (status === 0 && stdout && stdout.length > 0) {
                return 'Active';
            }
            return 'Not Running';
        } catch (e) {
            return 'Unknown';
        }
    }

    _formatSpeed(bytesPerSec) {
        if (bytesPerSec < 1024) return `${Math.round(bytesPerSec)} B/s`;
        if (bytesPerSec < 1048576) return `${(bytesPerSec / 1024).toFixed(0)} KB/s`;
        if (bytesPerSec < 1073741824) return `${(bytesPerSec / 1048576).toFixed(1)} MB/s`;
        return `${(bytesPerSec / 1073741824).toFixed(2)} GB/s`;
    }

    _updateSpeed() {
        if (!this._monitor) return;
        const res = this._monitor.readSpeed();
        
        if (!res.active) {
            this._label.set_text('Network Offline');
            this._networkItem.label.set_text('Network: None');
            this._connectionItem.label.set_text('Connection: Offline');
            this._statusItem.label.set_text('Status: Disconnected');
        } else {
            const dl = this._formatSpeed(res.download);
            const ul = this._formatSpeed(res.upload);
            this._label.set_text(`↓ ${dl}  ↑ ${ul}`);
            
            this._networkItem.label.set_text(`Network: ${res.interface}`);
            this._connectionItem.label.set_text(`Connection: ${res.type}`);
            this._statusItem.label.set_text('Status: Connected');
        }
        
        // Only check DataSense status if the popup is open to save CPU
        if (this._indicator.menu.isOpen) {
            const dsStatus = this._checkDataSenseStatus();
            this._monitoringItem.label.set_text(`Monitoring: ${dsStatus}`);
        }
    }

    disable() {
        if (this._timeoutId) {
            GLib.Source.remove(this._timeoutId);
            this._timeoutId = null;
        }
        if (this._indicator) {
            this._indicator.destroy();
            this._indicator = null;
        }
        this._monitor = null;
    }
}
