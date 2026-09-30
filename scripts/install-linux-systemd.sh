#!/usr/bin/env bash
set -euo pipefail

if [ "$EUID" -ne 0 ]; then
  echo "Run as root (sudo)." >&2
  exit 1
fi

INSTALL_DIR="/opt/sentinelcore"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "Checking for required tools (nft, journalctl)..."
command -v nft >/dev/null 2>&1 || echo "WARNING: nft not found - install nftables for firewall blocking to work."
command -v journalctl >/dev/null 2>&1 || echo "WARNING: journalctl not found - install systemd/journald for auth log monitoring."

echo "Publishing SentinelServer (linux-x64)..."
dotnet publish "$REPO_ROOT/SentinelServer/SentinelServer.csproj" -c Release -r linux-x64 --self-contained false -o "$INSTALL_DIR"

mkdir -p /var/lib/sentinelcore
cp "$REPO_ROOT/scripts/sentinelcore.service" /etc/systemd/system/sentinelcore.service

systemctl daemon-reload
systemctl enable sentinelcore
systemctl restart sentinelcore

echo ""
echo "Installed and started. First-run Owner API key is printed once to the service journal:"
echo "  journalctl -u sentinelcore -n 50 --no-pager | grep -A2 'API key'"
