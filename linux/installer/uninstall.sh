#!/bin/bash

echo "Uninstalling DataSense..."

BIN_DIR="$HOME/.local/bin"
APP_DIR="$HOME/.local/share/DataSense"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/scalable/apps"
EXT_DIR="$HOME/.local/share/gnome-shell/extensions/datasense-speed-meter@dulmina.dev"

rm -f "$BIN_DIR/datasense-wrapper"
rm -rf "$APP_DIR"
rm -f "$DESKTOP_DIR/com.datasense.DataSense.desktop"
rm -f "$ICON_DIR/com.datasense.DataSense.svg"
rm -rf "$EXT_DIR"

echo "Uninstallation complete."
echo "Note: Your local telemetry database and preferences in ~/.config/DataSense were NOT deleted."
