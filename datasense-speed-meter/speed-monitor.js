import GLib from 'gi://GLib';
import Gio from 'gi://Gio';

export class SpeedMonitor {
    constructor() {
        this._previousRx = 0;
        this._previousTx = 0;
        this._lastTime = 0;
        this._file = Gio.File.new_for_path('/proc/net/dev');
    }

    _getInterfaceType(iface) {
        if (iface.startsWith('wl') || iface.startsWith('wlan')) return 'Wi-Fi';
        if (iface.startsWith('en') || iface.startsWith('eth')) return 'Ethernet';
        if (iface.startsWith('tun') || iface.startsWith('wg')) return 'VPN';
        return 'Unknown';
    }

    readSpeed() {
        try {
            const [, contents] = this._file.load_contents(null);
            const lines = new TextDecoder().decode(contents).split('\n');
            let totalRx = 0;
            let totalTx = 0;
            let activeInterface = 'Unknown';
            let activeType = 'Unknown';
            let maxTraffic = -1;

            for (let i = 2; i < lines.length; i++) {
                const line = lines[i].trim();
                if (!line) continue;
                
                const parts = line.split(':');
                if (parts.length < 2) continue;
                
                const interfaceName = parts[0].trim();
                // Ignore loopback and virtual interfaces
                if (interfaceName === 'lo' || interfaceName.startsWith('veth') || interfaceName.startsWith('docker') || interfaceName.startsWith('br-')) {
                    continue;
                }

                const data = parts[1].trim().split(/\s+/);
                if (data.length >= 9) {
                    const rx = parseInt(data[0], 10);
                    const tx = parseInt(data[8], 10);
                    totalRx += rx;
                    totalTx += tx;
                    
                    if (rx + tx > maxTraffic) {
                        maxTraffic = rx + tx;
                        activeInterface = interfaceName;
                        activeType = this._getInterfaceType(interfaceName);
                    }
                }
            }
            
            const now = GLib.get_monotonic_time();
            
            if (maxTraffic === -1) {
                return { download: 0, upload: 0, active: false, interface: 'None', type: 'Offline' };
            }

            if (this._lastTime === 0 || totalRx < this._previousRx || totalTx < this._previousTx) {
                this._previousRx = totalRx;
                this._previousTx = totalTx;
                this._lastTime = now;
                // Baseline reset, avoid fake spikes
                return { download: 0, upload: 0, active: true, interface: activeInterface, type: activeType };
            }

            const elapsedSec = (now - this._lastTime) / 1000000;
            
            // Protect against suspend/resume massive gaps
            if (elapsedSec > 10) {
                 this._previousRx = totalRx;
                 this._previousTx = totalTx;
                 this._lastTime = now;
                 return { download: 0, upload: 0, active: true, interface: activeInterface, type: activeType };
            }

            const download = (totalRx - this._previousRx) / elapsedSec;
            const upload = (totalTx - this._previousTx) / elapsedSec;

            this._previousRx = totalRx;
            this._previousTx = totalTx;
            this._lastTime = now;

            return { download, upload, active: true, interface: activeInterface, type: activeType };

        } catch (e) {
            console.error(e);
            return { download: 0, upload: 0, active: false, interface: 'None', type: 'Offline' };
        }
    }
}
