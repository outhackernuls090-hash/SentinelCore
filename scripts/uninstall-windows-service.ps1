#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "SentinelServer"
)

$ErrorActionPreference = "Stop"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName
    Write-Host "Service '$ServiceName' removed."
} else {
    Write-Host "Service '$ServiceName' not found."
}
