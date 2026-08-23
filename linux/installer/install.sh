#!/bin/bash

# Ensure we're not running as root, install locally
if [ "$EUID" -eq 0 ]; then
  echo "Please do not run this script as root. It installs per-user."
  exit 1
fi

echo "Installing DataSense..."

# Directories
BIN_DIR="$HOME/.local/bin"
APP_DIR="$HOME/.local/share/DataSense"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/scalable/apps"
EXT_DIR="$HOME/.local/share/gnome-shell/extensions/datasense-speed-meter@dulmina.dev"

mkdir -p "$BIN_DIR" "$APP_DIR" "$DESKTOP_DIR" "$ICON_DIR"

# Assuming the current dir is the repository root
# 1. Build DataSense
echo "Building DataSense..."
dotnet build -c Release
cp -r bin/Release/net10.0/* "$APP_DIR/"

# 2. Desktop Entry & Single Instance Wrapper
echo "Installing Desktop Entry..."
cat << 'EOF' > "$BIN_DIR/datasense-wrapper"
#!/bin/bash
# Single instance check
if pgrep -x "DataSense" > /dev/null
then
    wmctrl -a "DataSense" || echo "DataSense is already running."
    exit 0
else
    exec "$HOME/.local/share/DataSense/DataSense" "$@"
fi
EOF
chmod +x "$BIN_DIR/datasense-wrapper"

sed "s|/opt/DataSense/DataSense|$BIN_DIR/datasense-wrapper|g" linux/desktop/com.datasense.DataSense.desktop > "$DESKTOP_DIR/com.datasense.DataSense.desktop"

# 3. GNOME Extension
echo "Installing GNOME Extension..."
mkdir -p "$EXT_DIR"
cp -r datasense-speed-meter/* "$EXT_DIR/"
if [ -d "$EXT_DIR/schemas" ]; then
    glib-compile-schemas "$EXT_DIR/schemas/"
fi

echo "Installation complete!"
echo "DataSense can now be launched from your application menu."
echo "Please log out and log back in (or restart GNOME shell) to enable the top-bar extension."
