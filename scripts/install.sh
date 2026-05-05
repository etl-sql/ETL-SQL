#!/bin/bash
# ETL-SQL Workstation SDK Installer (Linux/macOS)

set -e

INSTALL_DIR="$HOME/.etl-sql"
BIN_DIR="$INSTALL_DIR/bin"
VERSION="latest"
BASE_URL="https://github.com/AmericanSuperstar/ETL-SQL/releases/download/$VERSION"

echo "--- ETL-SQL Workstation SDK Installer ---"

# 1. Detect OS and Architecture
OS_NAME=$(uname -s | tr '[:upper:]' '[:lower:]')
ARCH=$(uname -m)

if [[ "$ARCH" == "x86_64" ]]; then
    ARCH="x64"
elif [[ "$ARCH" == "arm64" || "$ARCH" == "aarch64" ]]; then
    ARCH="arm64"
fi

TAR_NAME="etl-sql-sdk-$OS_NAME-$ARCH.tar.gz"
DOWNLOAD_URL="$BASE_URL/$TAR_NAME"

# 2. Create Install Directories
if [ ! -d "$BIN_DIR" ]; then
    echo "Creating installation directory at $BIN_DIR..."
    mkdir -p "$BIN_DIR"
fi

# 3. Download SDK (Simulated)
echo "Downloading SDK from $DOWNLOAD_URL..."
# curl -L "$DOWNLOAD_URL" -o "/tmp/$TAR_NAME"

# 4. Extract Files (Simulated)
# tar -xzf "/tmp/$TAR_NAME" -C "$BIN_DIR"

# 5. Add to PATH (Update .bashrc or .zshrc)
SHELL_CONFIG=""
if [[ "$SHELL" == *"zsh"* ]]; then
    SHELL_CONFIG="$HOME/.zshrc"
elif [[ "$SHELL" == *"bash"* ]]; then
    SHELL_CONFIG="$HOME/.bashrc"
fi

if [ -n "$SHELL_CONFIG" ]; then
    if ! grep -q "$BIN_DIR" "$SHELL_CONFIG"; then
        echo "Adding $BIN_DIR to $SHELL_CONFIG..."
        echo "" >> "$SHELL_CONFIG"
        echo "# ETL-SQL SDK" >> "$SHELL_CONFIG"
        echo "export PATH=\"\$PATH:$BIN_DIR\"" >> "$SHELL_CONFIG"
        echo "PATH updated. Please run 'source $SHELL_CONFIG' or restart your terminal."
    else
        echo "$BIN_DIR is already in PATH."
    fi
else
    echo "Could not detect shell config file. Please add $BIN_DIR to your PATH manually."
fi

echo -e "\nInstallation complete!"
echo "Try running 'ETL-SQL --version' in a new terminal."
