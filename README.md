# SentinelCore v1.0

A local, cloud-free host intrusion detection and response platform for Windows and
Linux servers. Detects brute-force logins and port scans, flags suspicious processes, can block
IPs / suspend / terminate processes, and keeps everything in a tamper-evident audit log — managed
from a Windows admin console, a cross-platform CLI, or the HTTP API directly.

This is not a replacement for a full commercial EDR — it is a transparent, self-hosted, host-level
detection and response tool, matching the V1 scope of the original design document (see
[CHANGELOG](./CHANGELOG.md) for exactly what's in and what's deferred to V2).

## Components

| Project | What it is | Platforms |
|---|---|---|
| `SentinelServer` | The agent: detection engine, firewall control, HTTP API | Windows + Linux |
| `SentinelConsole` | Admin GUI (WinForms), multi-device, roles-aware | Windows |
| `SentinelCli` | Headless CLI client (`sentinel status`, `sentinel block-ip ...`) | Windows + Linux |
| `SentinelShared` | Shared DTOs + typed HTTP client used by Console and CLI | any |

## Quick start

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build.

```bash
git clone <this repo>
cd SentinelCore
dotnet restore SentinelCore.sln
```

### Run the agent locally (either OS, foreground, for trying it out)

```bash
dotnet run --project SentinelServer
```

On first run it prints a one-time Owner API key — save it, it is never shown again (only its hash
is stored). It also prints the TLS certificate's fingerprint.

### Install as a real service

- Windows (run PowerShell as Administrator): `scripts\install-windows-service.ps1`
- Linux (run as root): `scripts/install-linux-systemd.sh`

Both scripts publish a self-contained-false, framework-dependent build (needs the .NET 8 runtime
on the target machine) and register it as a service (`SentinelServer` on Windows,
`sentinelcore.service` on Linux via systemd).

### Connect with the Console (Windows)

```bash
dotnet run --project SentinelConsole
```

Enter the agent's URL (e.g. `https://192.168.1.20:47152`) and the API key, click Connect. On first
connection to a given host you'll be asked to confirm the TLS certificate's fingerprint (trust-on-
first-use) — see `docs/SECURITY.md`. Use the **+** tab to manage several agents from one window,
each with its own connection.

### Connect with the CLI (either OS)

```bash
dotnet run --project SentinelCli -- connect --host https://192.168.1.20:47152 --key sk_...
dotnet run --project SentinelCli -- status
dotnet run --project SentinelCli -- incidents
dotnet run --project SentinelCli -- block-ip 203.0.113.55 --minutes 30 --reason "manual"
```

Run `sentinel help` (or `dotnet run --project SentinelCli -- help`) for the full command list.

### Publish release binaries

```bash
scripts/publish-all.sh     # Linux/macOS host — builds linux-x64 + win-x64 server/cli, skips Console
scripts\publish-all.ps1    # Windows host — builds win-x64 server/cli/console
```

Output goes to `dist/`. CI (`.github/workflows/build.yml`) does the same on every push to `main`
and uploads the result as a build artifact.

## Configuration

All server settings are in `SentinelServer/appsettings.json` (copied next to the built binary).
Key sections: `management` (port, network allowlist), `tls`, `detection` (thresholds), `response`
(dry-run, auto-block duration), `smtp`, `retention`, `testMode`. See the comments in
`docs/API.md` and `docs/SECURITY.md` for what each controls. Users and roles are **not** in the
config file — they live in the database and are managed via the Owner-only `/api/v1/users`
endpoint, the Console's Users tab, or `sentinel users create <name> <role>`.

## Local testing without a real attack

Set `testMode.enabled: true` in `appsettings.json`, then use the Console's "Local Testing" tab, or
`sentinel simulate bruteforce 203.0.113.55` — this drives real synthetic events through the actual
detection engine (real incident, real timeline, real firewall call), so you can verify the whole
pipeline end-to-end before pointing it at production traffic. Turn `testMode.enabled` back off
before going live.

## Tests

```bash
dotnet test tests/SentinelServer.Tests/SentinelServer.Tests.csproj
```

Covers brute-force threshold/suppression logic, the detection allowlist, maintenance mode, and the
audit hash chain (including a test that tampering with a stored row is detected).

## Documentation

- `docs/API.md` — full endpoint reference and role requirements
- `docs/SECURITY.md` — TLS/pinning, auth, audit chain, what is and isn't covered
- `CHANGELOG.md` — what's in v1.0 and what's deferred to v2
  
## License

SentinelCore is distributed under a custom source-available license.

**Please read the full [LICENSE](./LICENSE) before using this software.**

In particular, commercial use, redistribution, sublicensing, commercial
hosting, and other restricted uses require a separate commercial license.

For commercial licensing inquiries, please contact:

**Arasaka Corp**  
contact@arasaka-corp.eu
