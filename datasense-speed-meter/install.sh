#!/bin/bash
set -e

EXT_UUID="datasense-speed-meter@dulmina.dev"
TARGET_DIR="$HOME/.local/share/gnome-shell/extensions/$EXT_UUID"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

echo "Installing DataSense Speed Meter ($EXT_UUID)..."

# Validate JS syntax before installing
node --check "$SCRIPT_DIR/extension.js"
node --check "$SCRIPT_DIR/speed-monitor.js"

mkdir -p "$TARGET_DIR"

# Copy extension files
cp "$SCRIPT_DIR/metadata.json" "$TARGET_DIR/"
cp "$SCRIPT_DIR/extension.js" "$TARGET_DIR/"
cp "$SCRIPT_DIR/speed-monitor.js" "$TARGET_DIR/"

# Copy and compile schemas
if [ -d "$SCRIPT_DIR/schemas" ]; then
    mkdir -p "$TARGET_DIR/schemas"
    cp -r "$SCRIPT_DIR/schemas/"* "$TARGET_DIR/schemas/"
    glib-compile-schemas --strict "$TARGET_DIR/schemas/"
fi

# Ensure extension UUID is in enabled-extensions setting
CURRENT_ENABLED=$(gsettings get org.gnome.shell enabled-extensions 2>/dev/null || echo "[]")
if [[ "$CURRENT_ENABLED" != *"$EXT_UUID"* ]]; then
    echo "Registering $EXT_UUID in org.gnome.shell enabled-extensions..."
    if [ "$CURRENT_ENABLED" = "@as []" ] || [ "$CURRENT_ENABLED" = "[]" ]; then
        gsettings set org.gnome.shell enabled-extensions "['$EXT_UUID']"
    else
        NEW_ENABLED=$(echo "$CURRENT_ENABLED" | sed "s/]/, '$EXT_UUID']/")
        gsettings set org.gnome.shell enabled-extensions "$NEW_ENABLED"
    fi
fi

# Attempt to enable via gnome-extensions if already indexed by GNOME Shell
gnome-extensions enable "$EXT_UUID" 2>/dev/null || true

echo "Installation complete for $EXT_UUID."
