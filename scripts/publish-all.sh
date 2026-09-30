#!/usr/bin/env bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$REPO_ROOT/dist"
rm -rf "$OUT"

echo "Publishing SentinelServer for linux-x64..."
dotnet publish "$REPO_ROOT/SentinelServer/SentinelServer.csproj" -c Release -r linux-x64 --self-contained false -o "$OUT/linux-x64/SentinelServer"

echo "Publishing SentinelServer for win-x64..."
dotnet publish "$REPO_ROOT/SentinelServer/SentinelServer.csproj" -c Release -r win-x64 --self-contained false -o "$OUT/win-x64/SentinelServer"

echo "Publishing SentinelCli for linux-x64..."
dotnet publish "$REPO_ROOT/SentinelCli/SentinelCli.csproj" -c Release -r linux-x64 --self-contained false -o "$OUT/linux-x64/SentinelCli"

echo "Publishing SentinelCli for win-x64..."
dotnet publish "$REPO_ROOT/SentinelCli/SentinelCli.csproj" -c Release -r win-x64 --self-contained false -o "$OUT/win-x64/SentinelCli"

echo "Publishing SentinelConsole for win-x64..."
dotnet publish "$REPO_ROOT/SentinelConsole/SentinelConsole.csproj" -c Release -r win-x64 --self-contained false -o "$OUT/win-x64/SentinelConsole"

echo "Done. Artifacts in $OUT"
