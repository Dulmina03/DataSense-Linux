#!/usr/bin/env bash
set -euo pipefail

# Determine script directory and repo root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

VERSION="${1:-1.0.0}"
ARCH="amd64"
DOTNET_RID="linux-x64"
PACKAGE_NAME="datasense"
DEB_FILE_NAME="${PACKAGE_NAME}_${VERSION}_${ARCH}.deb"

BUILD_DIR="${REPO_ROOT}/build"
PKG_DIR="${BUILD_DIR}/${PACKAGE_NAME}_${VERSION}_${ARCH}"
OUTPUT_DEB="${REPO_ROOT}/${DEB_FILE_NAME}"

echo "============================================="
echo " Building Debian Package: ${DEB_FILE_NAME}"
echo " Version:      ${VERSION}"
echo " Architecture: ${ARCH} (RID: ${DOTNET_RID})"
echo " Output:       ${OUTPUT_DEB}"
echo "============================================="

# 1. Clean previous build artifacts
rm -rf "${PKG_DIR}"
mkdir -p "${PKG_DIR}/opt/datasense"
mkdir -p "${PKG_DIR}/usr/bin"
mkdir -p "${PKG_DIR}/usr/share/applications"
mkdir -p "${PKG_DIR}/usr/share/pixmaps"
mkdir -p "${PKG_DIR}/usr/share/gnome-shell/extensions/datasense-speed-meter@dulmina.dev"
mkdir -p "${PKG_DIR}/usr/share/glib-2.0/schemas"
mkdir -p "${PKG_DIR}/DEBIAN"

# 2. Publish .NET 10 self-contained executable
echo "--> Publishing .NET 10 application (Release, ${DOTNET_RID})..."
dotnet publish "${REPO_ROOT}/DataSense.csproj" \
    -c Release \
    -r "${DOTNET_RID}" \
    --self-contained \
    -p:PublishReadyToRun=false \
    -o "${PKG_DIR}/opt/datasense"

# Remove unnecessary PDBs to save space
find "${PKG_DIR}/opt/datasense" -name "*.pdb" -type f -delete

# Ensure main executable has correct permissions
chmod 755 "${PKG_DIR}/opt/datasense/DataSense"

# 3. Create symlinks in /usr/bin
ln -sf "/opt/datasense/DataSense" "${PKG_DIR}/usr/bin/datasense"
ln -sf "/opt/datasense/DataSense" "${PKG_DIR}/usr/bin/DataSense"

# 4. Generate multi-resolution icons
echo "--> Generating application icons..."
PKG_DIR="${PKG_DIR}" python3 -c "
import os
from PIL import Image

src_icon = '${REPO_ROOT}/Assets/datasense.png'
pkg_dir = '${PKG_DIR}'
img = Image.open(src_icon).convert('RGBA')

# Save to pixmaps
os.makedirs(os.path.join(pkg_dir, 'usr', 'share', 'pixmaps'), exist_ok=True)
img.resize((256, 256), Image.Resampling.LANCZOS).save(os.path.join(pkg_dir, 'usr', 'share', 'pixmaps', 'datasense.png'), 'PNG')
img.resize((256, 256), Image.Resampling.LANCZOS).save(os.path.join(pkg_dir, 'usr', 'share', 'pixmaps', 'com.datasense.DataSense.png'), 'PNG')

# Generate standard hicolor resolutions
for res in [16, 24, 32, 48, 64, 128, 256, 512]:
    icon_dir = os.path.join(pkg_dir, 'usr', 'share', 'icons', 'hicolor', f'{res}x{res}', 'apps')
    os.makedirs(icon_dir, exist_ok=True)
    resized = img.resize((res, res), Image.Resampling.LANCZOS)
    resized.save(os.path.join(icon_dir, 'datasense.png'), 'PNG')
    resized.save(os.path.join(icon_dir, 'com.datasense.DataSense.png'), 'PNG')
print('Generated icons across standard resolutions.')
"

# 5. Desktop file
echo "--> Installing desktop file..."
cat << 'EOF' > "${PKG_DIR}/usr/share/applications/com.datasense.DataSense.desktop"
[Desktop Entry]
Type=Application
Name=DataSense
GenericName=Network Usage Monitor
Comment=Monitor your network usage and application telemetry
Exec=/opt/datasense/DataSense %u
Icon=datasense
Terminal=false
Categories=Network;Monitor;System;Utility;
StartupNotify=true
StartupWMClass=DataSense
X-GNOME-SingleWindow=true
Keywords=network;bandwidth;data;usage;monitor;telemetry;
EOF
chmod 644 "${PKG_DIR}/usr/share/applications/com.datasense.DataSense.desktop"

# 6. GNOME Shell Extension
echo "--> Bundling GNOME Shell Extension..."
if [ -d "${REPO_ROOT}/datasense-speed-meter" ]; then
    cp -r "${REPO_ROOT}/datasense-speed-meter/"* "${PKG_DIR}/usr/share/gnome-shell/extensions/datasense-speed-meter@dulmina.dev/"
    if [ -f "${REPO_ROOT}/datasense-speed-meter/schemas/org.gnome.shell.extensions.datasense-speed-meter.gschema.xml" ]; then
        cp "${REPO_ROOT}/datasense-speed-meter/schemas/org.gnome.shell.extensions.datasense-speed-meter.gschema.xml" "${PKG_DIR}/usr/share/glib-2.0/schemas/"
    fi
fi

# 7. Calculate Installed-Size in KB
INSTALLED_SIZE=$(du -sk "${PKG_DIR}" | awk '{print $1}')

# 8. Create DEBIAN/control
echo "--> Writing DEBIAN/control..."
cat << EOF > "${PKG_DIR}/DEBIAN/control"
Package: ${PACKAGE_NAME}
Version: ${VERSION}
Section: net
Priority: optional
Architecture: ${ARCH}
Installed-Size: ${INSTALLED_SIZE}
Maintainer: Dulmina <dulminajayasiri@gmail.com>
Depends: libc6, libgcc-s1, libstdc++6, libfontconfig1, libx11-6, libgl1
Recommends: nethogs, network-manager
Homepage: https://github.com/dulmina/DataSense
Description: Privacy-first network & application telemetry monitor
 DataSense is a privacy-first, local-only network and application telemetry
 dashboard tailored for Linux. It tracks data usage per network connection,
 logs bandwidth spikes, and monitors process-level payload allocations
 without relying on cloud analytics.
EOF
chmod 644 "${PKG_DIR}/DEBIAN/control"

# 9. Create DEBIAN/postinst
cat << 'EOF' > "${PKG_DIR}/DEBIAN/postinst"
#!/bin/sh
set -e

if [ "$1" = "configure" ]; then
    # Update icon cache
    if [ -x /usr/bin/gtk-update-icon-cache ]; then
        /usr/bin/gtk-update-icon-cache -f -t -q /usr/share/icons/hicolor || true
    fi

    # Update desktop database
    if [ -x /usr/bin/update-desktop-database ]; then
        /usr/bin/update-desktop-database -q /usr/share/applications || true
    fi

    # Compile GLib schemas
    if [ -x /usr/bin/glib-compile-schemas ]; then
        /usr/bin/glib-compile-schemas /usr/share/glib-2.0/schemas || true
    fi
fi

exit 0
EOF
chmod 755 "${PKG_DIR}/DEBIAN/postinst"

# 10. Create DEBIAN/postrm
cat << 'EOF' > "${PKG_DIR}/DEBIAN/postrm"
#!/bin/sh
set -e

if [ "$1" = "remove" ] || [ "$1" = "purge" ]; then
    # Update icon cache
    if [ -x /usr/bin/gtk-update-icon-cache ]; then
        /usr/bin/gtk-update-icon-cache -f -t -q /usr/share/icons/hicolor || true
    fi

    # Update desktop database
    if [ -x /usr/bin/update-desktop-database ]; then
        /usr/bin/update-desktop-database -q /usr/share/applications || true
    fi

    # Compile GLib schemas
    if [ -x /usr/bin/glib-compile-schemas ]; then
        /usr/bin/glib-compile-schemas /usr/share/glib-2.0/schemas || true
    fi
fi

exit 0
EOF
chmod 755 "${PKG_DIR}/DEBIAN/postrm"

# 11. Build the Debian package
echo "--> Building final .deb package..."
dpkg-deb --build --root-owner-group "${PKG_DIR}" "${OUTPUT_DEB}"

echo "============================================="
echo " Package successfully built!"
echo " File: ${OUTPUT_DEB}"
echo " Size: $(du -h "${OUTPUT_DEB}" | cut -f1)"
echo "============================================="
