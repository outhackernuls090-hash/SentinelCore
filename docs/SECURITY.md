# Security model

- **Transport**: TLS is on by default (`tls.enabled` in `appsettings.json`). The server generates a
  self-signed certificate on first run and stores it at `sentinel-tls.pfx` in its data directory.
  Because it is self-signed, clients (Console, CLI) use trust-on-first-use (TOFU): on first connect
  they show the certificate's SHA-256 fingerprint and ask you to confirm it, then pin it for that
  host. If the fingerprint ever changes unexpectedly, the client warns loudly instead of silently
  accepting a new certificate — treat that as a possible reinstall (expected) or interception
  (investigate).
- **Authentication**: every request needs an `X-Api-Key` header. Keys are generated per-user,
  128 bits of entropy, and only their SHA-256 hash is stored server-side — the raw key is shown
  exactly once (at creation) and cannot be retrieved again.
- **Authorization**: four roles (Viewer, Analyst, Administrator, Owner), enforced server-side on
  every endpoint (see `docs/API.md`). The Console/CLI also hide or grey out actions the current
  role can't perform, but the server is the actual enforcement point.
- **Network allowlist**: `management.allowedNetworks` in `appsettings.json` restricts which source
  IPs may reach the management API at all, independent of API keys. Restart required to change it.
- **Audit log**: every state-changing action is recorded with a SHA-256 hash chain (each entry's
  hash covers the previous entry's hash plus its own fields), so any row edited or deleted after
  the fact is detectable via `GET /api/v1/audit/verify`. This does not prevent tampering by someone
  with direct file-system access to the SQLite database — it makes it detectable.
- **Firewall actions**: are scoped, named (`SENTINEL_BLOCK_*` on Windows, a dedicated `nftables` set
  on Linux) and reversible; auto-blocks expire automatically (`response.autoBlockDurationMinutes`).
- **Maintenance mode**: suppresses automatic response actions (but not detection/logging) — use it
  during planned backups or known noisy events, rather than disabling detection outright.
- **Dry-run mode**: (`response.dryRun`) logs what the engine *would* do without touching the
  firewall — recommended for validating detection rules before enabling automatic response on a
  new host.

## Not covered by this release

Password hashing here uses SHA-256 for API-key lookups (appropriate since these are
already-random, 256-bit generated secrets, not user-chosen passwords). There is no encryption at
rest for the SQLite database itself — protect the data directory with filesystem permissions
(the installers set the service to run as `root`/`SYSTEM`, and the DB is not world-readable by
default on Linux under `/var/lib/sentinelcore`).
