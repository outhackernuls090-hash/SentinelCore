#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "C:\Program Files\SentinelCore",
    [string]$ServiceName = "SentinelServer"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing SentinelServer (win-x64)..."
$repoRoot = Split-Path -Parent $PSScriptRoot
dotnet publish "$repoRoot\SentinelServer\SentinelServer.csproj" -c Release -r win-x64 --self-contained false -o "$InstallDir"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Host "Creating Windows Service '$ServiceName'..."
sc.exe create $ServiceName binPath= "`"$InstallDir\SentinelServer.exe`"" start= auto DisplayName= "SentinelCore Server"
sc.exe description $ServiceName "SentinelCore host intrusion detection and response agent"

Write-Host "Starting service..."
Start-Service -Name $ServiceName

Write-Host ""
Write-Host "Installed. Check the service log/console output on first run for the generated Owner API key:"
Write-Host "  Get-EventLog -LogName Application -Source $ServiceName -Newest 20"
Write-Host "or run once in a console first: `"$InstallDir\SentinelServer.exe`" to see the bootstrap key printed."
