#!/bin/bash

EXT_UUID="datasense-speed-meter@dulmina.dev"
TARGET_DIR="$HOME/.local/share/gnome-shell/extensions/$EXT_UUID"

echo "Installing DataSense Speed Meter..."

mkdir -p "$TARGET_DIR"
cp metadata.json extension.js speed-monitor.js "$TARGET_DIR/"

if [ -d "schemas" ]; then
    cp -r schemas "$TARGET_DIR/"
    glib-compile-schemas "$TARGET_DIR/schemas/"
fi

echo "Installation complete."
echo "Please restart GNOME Shell (Alt+F2, r, Enter or log out/in on Wayland) and enable the extension."
