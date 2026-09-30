# SentinelServer HTTP API (v1)

Base URL: `https://<host>:47152` (TLS is on by default; the certificate is self-signed and generated
on first run — pin its fingerprint in the client on first connect).

All endpoints except `/health` require an `X-Api-Key` header. The key determines the caller's role;
endpoints enforce a **minimum** role:

| Role          | Rank | Can do |
|---------------|------|--------|
| Viewer        | 1    | Read everything (incidents, processes, network, blocked IPs, audit, allowlist) |
| Analyst       | 2    | + change incident state (acknowledge/resolve/ignore/false positive) |
| Administrator | 3    | + block/unblock IPs, suspend/resume/terminate processes, maintenance mode, allowlist edits, test email, simulate attacks |
| Owner         | 4    | + manage users (create/revoke, assign roles) |

The server's own network-level allowlist (`management.allowedNetworks` in `appsettings.json`) is a
separate, coarser control: it decides which source IPs may reach the API at all, regardless of role.

## Endpoints

| Method | Path | Min role | Notes |
|---|---|---|---|
| GET | `/health` | none | `{status, version, testMode, tls}` |
| GET | `/api/v1/status` | Viewer | host status, counts, maintenance/dry-run/test-mode flags |
| GET | `/api/v1/incidents?state=&limit=` | Viewer | |
| GET | `/api/v1/incidents/{id}/timeline` | Viewer | |
| POST | `/api/v1/actions/incident-state` | Analyst | `{incidentId, state}` |
| GET | `/api/v1/processes` | Viewer | includes `flag` for suspicious parent/child chains or temp-dir execution |
| GET | `/api/v1/network` | Viewer | active TCP connections |
| GET | `/api/v1/blocked-ips` | Viewer | |
| POST | `/api/v1/actions/block-ip` | Administrator | `{ip, reason, durationMinutes}` |
| POST | `/api/v1/actions/unblock-ip` | Administrator | `{ip}` |
| POST | `/api/v1/actions/suspend-process` | Administrator | `{pid}` |
| POST | `/api/v1/actions/resume-process` | Administrator | `{pid}` |
| POST | `/api/v1/actions/terminate-process` | Administrator | `{pid}` |
| POST | `/api/v1/actions/maintenance` | Administrator | `{enabled}` |
| GET | `/api/v1/allowlist` | Viewer | |
| POST | `/api/v1/allowlist` | Administrator | `{entry, remove}` |
| GET | `/api/v1/audit?limit=` | Viewer | |
| GET | `/api/v1/audit/verify` | Viewer | recomputes the hash chain; detects tampering |
| GET | `/api/v1/users` | Owner | |
| POST | `/api/v1/users` | Owner | `{name, role}` → returns the API key **once** |
| DELETE | `/api/v1/users/{name}` | Owner | |
| POST | `/api/v1/actions/test-email` | Administrator | sends a test SMTP alert |
| POST | `/api/v1/actions/simulate-attack` | Administrator | requires `testMode.enabled: true`; `{ip, kind: "bruteforce"\|"portscan"}` |
| GET | `/api/v1/report.csv` | Viewer | incidents as CSV |
