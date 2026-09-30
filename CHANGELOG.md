# Changelog

## 1.0.0

- Failed-login and brute-force detection (Windows Security Event Log / Linux `journalctl`)
- Port scan detection
- Process monitoring with basic reputation flags (suspicious parent/child chains, execution from temp directories)
- Network connection monitoring
- Firewall IP blocking (Windows `netsh` / Linux `nftables`), with auto-expiry
- Incident timeline and correlation/suppression (repeated triggers roll into one incident)
- SMTP alerting with severity threshold and rate limiting
- Local SQLite storage with configurable retention
- Role-based authentication (Viewer / Analyst / Administrator / Owner), per-user API keys
- Tamper-evident, hash-chained audit log with a verify endpoint
- IP allowlist to exempt trusted hosts from auto-blocking
- Maintenance mode and dry-run mode to avoid false-positive damage
- TLS by default (self-signed certificate, auto-generated) with certificate pinning in the console/CLI
- Cross-platform agent (Windows Service / systemd)
- Admin Console (WinForms): multi-device tabs, incident timeline view, process suspend/resume/terminate,
  allowlist and user management, CSV report export, local attack simulation for testing
- SentinelCli headless client for scripting/servers without a GUI

### Known limitations (Will be added in V2)
- Ransomware-style mass file modification detection
- Persistence detection (services, scheduled tasks, registry run keys, cron, systemd timers)
- USB monitoring
- Signed/versioned policy distribution, central multi-host management server
- Process reputation is heuristic only (path/parent-based), not signature/hash based
