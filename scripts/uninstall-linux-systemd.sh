#!/usr/bin/env bash
set -euo pipefail

if [ "$EUID" -ne 0 ]; then
  echo "Run as root (sudo)." >&2
  exit 1
fi

systemctl stop sentinelcore || true
systemctl disable sentinelcore || true
rm -f /etc/systemd/system/sentinelcore.service
systemctl daemon-reload
echo "Service removed. Data in /var/lib/sentinelcore and binaries in /opt/sentinelcore were left in place."
