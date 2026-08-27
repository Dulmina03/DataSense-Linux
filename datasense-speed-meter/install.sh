#!/bin/bash

EXT_UUID="datasense-speed-meter@dulmina.dev"
TARGET_DIR="$HOME/.local/share/gnome-shell/extensions/$EXT_UUID"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

echo "Installing DataSense Speed Meter..."

mkdir -p "$TARGET_DIR"
cp "$SCRIPT_DIR/metadata.json" "$SCRIPT_DIR/extension.js" "$SCRIPT_DIR/speed-monitor.js" "$TARGET_DIR/"

if [ -d "$SCRIPT_DIR/schemas" ]; then
    cp -r "$SCRIPT_DIR/schemas" "$TARGET_DIR/"
    glib-compile-schemas "$TARGET_DIR/schemas/"
fi

echo "Installation complete."
echo "Please restart GNOME Shell (Alt+F2, r, Enter or log out/in on Wayland) and enable the extension."
