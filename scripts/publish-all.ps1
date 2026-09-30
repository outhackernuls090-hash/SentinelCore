$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$out = Join-Path $repoRoot "dist"
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue

Write-Host "Publishing SentinelServer for win-x64..."
dotnet publish "$repoRoot\SentinelServer\SentinelServer.csproj" -c Release -r win-x64 --self-contained false -o "$out\win-x64\SentinelServer"

Write-Host "Publishing SentinelCli for win-x64..."
dotnet publish "$repoRoot\SentinelCli\SentinelCli.csproj" -c Release -r win-x64 --self-contained false -o "$out\win-x64\SentinelCli"

Write-Host "Publishing SentinelConsole for win-x64..."
dotnet publish "$repoRoot\SentinelConsole\SentinelConsole.csproj" -c Release -r win-x64 --self-contained false -o "$out\win-x64\SentinelConsole"

Write-Host "Done. Artifacts in $out"
