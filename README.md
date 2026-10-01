# SentinelCore

**Local, cloud-free host intrusion detection and response for Windows and Linux.**

SentinelCore is a self-hosted security platform for monitoring hosts, detecting suspicious activity, investigating incidents, and taking controlled response actions.

It is designed around a simple idea:

> **Your security telemetry and response controls should remain under your control.**

SentinelCore does not require a third-party cloud service. Detection happens locally, security events are stored locally, and response actions such as IP blocking or process termination are performed directly on the protected host.

It can be managed through a Windows administration console, a cross-platform CLI, or the HTTP API.

---

## Security first

SentinelCore is built to make security activity **visible, auditable, and controllable** rather than hiding everything behind an opaque service.

### 🔐 Local and cloud-free

* No mandatory cloud backend
* Host telemetry stays on infrastructure you control
* Detection runs locally
* Local SQLite event storage
* Self-hosted deployment
* Works without sending security events to a third-party service

### 🛡️ Detection

SentinelCore currently provides host-level detection for:

* Brute-force login activity
* Port scans
* Suspicious process activity
* Network activity
* Repeated or correlated security events
* Configurable detection thresholds
* Detection allowlists
* Event suppression and correlation

Detection rules and thresholds are configurable rather than hidden behind a proprietary cloud service.

### 🚨 Incident response

When a detection requires action, SentinelCore can perform controlled response actions directly on the host:

* Block an IP address
* Automatically expire firewall blocks
* Suspend a process
* Terminate a process
* Place the system into maintenance mode
* Use dry-run mode to test response without making destructive changes

Response actions are recorded so administrators can see what happened and why.

### 📜 Tamper-evident auditing

Security actions and important events are recorded in an audit log with a hash chain.

SentinelCore can verify the integrity of the stored audit history and detect modifications to previously recorded entries.

This provides a way to answer questions such as:

* What happened?
* When did it happen?
* Which action was taken?
* Who initiated the action?
* Has the recorded history been modified?

### 🔑 Authentication and authorization

SentinelCore includes:

* API-key authentication
* Role-based access control
* Owner/admin/user permissions
* Per-user API keys
* Secure storage of API-key secrets as hashes
* Owner-only user management
* Network allowlisting for management access

Administrative operations are not simply exposed as unauthenticated HTTP endpoints.

### 🔒 Protected management API

Management communication uses HTTPS/TLS.

The client supports certificate fingerprint verification using **trust-on-first-use (TOFU)** behavior.

This allows an administrator to explicitly verify the certificate presented by a SentinelCore agent before trusting that host.

See [`docs/SECURITY.md`](docs/SECURITY.md) for the complete security model and its limitations.

---

# What SentinelCore does

SentinelCore combines several host-security functions into one locally controlled platform:

```text
                    ┌───────────────────────┐
                    │       SentinelCore     │
                    │     Security Agent     │
                    └───────────┬───────────┘
                                │
             ┌──────────────────┼──────────────────┐
             │                  │                  │
             ▼                  ▼                  ▼
        Detection           Investigation       Response
             │                  │                  │
       • Brute force       • Incidents        • Block IP
       • Port scans        • Timelines         • Suspend
       • Processes         • Correlation       • Terminate
       • Network           • Audit log         • Dry-run
             │                  │                  │
             └──────────────────┼──────────────────┘
                                ▼
                     Local, auditable security
```

The result is a host-level security system that can **detect → investigate → respond → record** without requiring a cloud control plane.

---

# Why SentinelCore?

Security tooling does not always need to send your endpoint data to someone else's infrastructure.

SentinelCore is intended for organizations and administrators who want:

* Local control over security telemetry
* Transparent detection behavior
* Self-hosted deployment
* Windows and Linux support
* Direct control over response actions
* Auditable security operations
* A security tool that can be inspected and tested locally

It is particularly useful as a lightweight host-security layer for environments where control over infrastructure and data matters.

---

# This is not a commercial EDR replacement

SentinelCore is intentionally scoped as a **host-level detection and response platform**.

It does **not** currently attempt to replace the complete feature set of mature commercial EDR/XDR platforms.

For example, advanced capabilities such as large-scale centralized fleet management, ransomware behavior detection, persistence detection, USB monitoring, and other features remain outside the current V1 scope.

See [`CHANGELOG.md`](CHANGELOG.md) for the current implementation status and planned V2 work.

The goal is transparency about what SentinelCore does today—not to claim capabilities it does not have.

---

# Components

SentinelCore is divided into several components:

| Project           | Description                                                                    | Platform        |
| ----------------- | ------------------------------------------------------------------------------ | --------------- |
| `SentinelServer`  | Security agent, detection engine, firewall control and HTTP API                | Windows + Linux |
| `SentinelConsole` | Windows administration GUI with multi-device support and role-aware management | Windows         |
| `SentinelCli`     | Headless command-line administration client                                    | Windows + Linux |
| `SentinelShared`  | Shared DTOs and typed HTTP client                                              | Any             |

The architecture allows administrators to use whichever management interface fits their environment.

---

# See it work without attacking a real system

SentinelCore includes a **local test mode** so you can exercise the actual detection and response pipeline without generating a real attack.

Enable:

```json
{
  "testMode": {
    "enabled": true
  }
}
```

Then run:

```bash
sentinel simulate bruteforce 203.0.113.55
```

The synthetic events pass through the real detection pipeline.

You can then observe:

```text
Synthetic event
      ↓
Detection engine
      ↓
Detection
      ↓
Incident
      ↓
Timeline / correlation
      ↓
Response
      ↓
Audit record
```

This is useful when evaluating SentinelCore because you can see the complete workflow before connecting it to production traffic.

**Disable `testMode` before deploying to a production environment.**

---

# Quick start

## Requirements

SentinelCore currently requires the **.NET 8 SDK** to build from source.

Download the .NET 8 SDK:

https://dotnet.microsoft.com/download/dotnet/8.0

Clone the repository:

```bash
git clone <this repo>
cd SentinelCore
dotnet restore SentinelCore.sln
```

---

# Run the agent locally

For a quick evaluation, run the agent in the foreground:

```bash
dotnet run --project SentinelServer
```

On first startup, SentinelCore generates a one-time Owner API key.

**Save this key immediately.**

The complete API key is not stored and cannot be displayed again. Only its hash is retained.

The server also displays the TLS certificate fingerprint.

Keep the fingerprint available when connecting a management client.

---

# Install SentinelCore as a service

For longer-running installations, SentinelServer can be installed as a native operating-system service.

### Windows

Run PowerShell as Administrator:

```powershell
scripts\install-windows-service.ps1
```

This registers:

```text
SentinelServer
```

as a Windows service.

### Linux

Run as root:

```bash
scripts/install-linux-systemd.sh
```

This registers:

```text
sentinelcore.service
```

with systemd.

The provided installation scripts publish a framework-dependent build, so the target machine requires the .NET 8 runtime.

---

# Windows administration console

Start the Windows console:

```bash
dotnet run --project SentinelConsole
```

Enter the SentinelServer URL:

```text
https://192.168.1.20:47152
```

and your API key.

On the first connection to a host, SentinelConsole asks you to confirm the server's TLS certificate fingerprint.

This is the trust-on-first-use process described in [`docs/SECURITY.md`](docs/SECURITY.md).

The console supports multiple agents through separate tabs, allowing an administrator to manage several SentinelCore hosts from one Windows workstation.

---

# Command-line administration

SentinelCore also provides a cross-platform CLI.

Connect to an agent:

```bash
dotnet run --project SentinelCli -- connect \
  --host https://192.168.1.20:47152 \
  --key sk_...
```

Check the host:

```bash
dotnet run --project SentinelCli -- status
```

View incidents:

```bash
dotnet run --project SentinelCli -- incidents
```

Block an IP temporarily:

```bash
dotnet run --project SentinelCli -- block-ip 203.0.113.55 \
  --minutes 30 \
  --reason "manual"
```

See all available commands:

```bash
sentinel help
```

or:

```bash
dotnet run --project SentinelCli -- help
```

---

# HTTP API

SentinelCore exposes an HTTP API for automation and integration with other systems.

The API provides access to functionality including:

* Host status
* Incidents
* Processes
* Network information
* Blocked IPs
* Incident state management
* IP blocking and unblocking
* Process suspension
* Process resumption
* Process termination
* Maintenance mode
* Detection allowlists
* Audit verification
* User management
* Attack simulation
* CSV reporting

See [`docs/API.md`](docs/API.md) for the complete endpoint reference and required roles.

This makes it possible to integrate SentinelCore with custom administration tools, scripts, monitoring systems, or other internal security infrastructure.

---

# Response controls

SentinelCore is designed so that automated response does not have to mean uncontrolled response.

Response behavior can be configured through the server configuration.

Available controls include:

### Dry-run mode

Test detection and response workflows without applying destructive actions.

Useful when deploying SentinelCore into an unfamiliar environment.

### Automatic IP blocking

Detected activity can trigger an IP block according to the configured response policy.

Blocks can have an expiration period rather than remaining indefinitely.

### Process response

Administrators can suspend or terminate processes through authorized management interfaces.

### Maintenance mode

Maintenance mode can be used when expected administrative activity would otherwise generate detections.

The exact behavior and limitations of these controls are documented in [`docs/SECURITY.md`](docs/SECURITY.md).

---

# Configuration

Server configuration is stored in:

```text
SentinelServer/appsettings.json
```

Major configuration areas include:

```text
management
tls
detection
response
smtp
retention
testMode
```

These control areas include functionality such as:

* Management port
* Network allowlists
* TLS behavior
* Detection thresholds
* Response behavior
* Dry-run mode
* Automatic blocking duration
* SMTP notifications
* Data retention
* Local test mode

Users and roles are stored in the database rather than in the configuration file.

User management is available to authorized administrators through:

```text
/api/v1/users
```

the Windows Console, or the CLI:

```bash
sentinel users create <name> <role>
```

---

# Security architecture

SentinelCore's security controls are documented separately so administrators can inspect the implementation and understand its limitations.

See:

**[`docs/SECURITY.md`](docs/SECURITY.md)**

The security documentation covers:

* TLS configuration
* Certificate fingerprints
* Trust-on-first-use behavior
* API-key authentication
* API-key storage
* Role-based authorization
* Network access restrictions
* Audit-log integrity
* Hash-chain verification
* Firewall response
* Maintenance mode
* Dry-run behavior
* Security limitations

## Important security limitations

SentinelCore is not designed to magically make a host secure.

For example, local database protection, operating-system permissions, administrator privileges, host compromise, network configuration, and deployment practices remain important.

The security documentation intentionally describes both **what SentinelCore protects and what it does not protect**.

---

# Audit integrity

SentinelCore maintains a hash-chained audit history.

Conceptually:

```text
Record 1
   │
   └── hash
        ↓
Record 2
   │
   └── hash
        ↓
Record 3
   │
   └── hash
        ↓
Record 4
```

Each record is linked to the previous record.

If an existing stored record is modified, the chain can be detected as inconsistent during verification.

SentinelCore includes automated tests specifically covering this behavior, including detection of tampering with a stored audit row.

This is intended to make the audit history **tamper-evident**, not magically immutable.

---

# Testing

Run the automated test suite with:

```bash
dotnet test tests/SentinelServer.Tests/SentinelServer.Tests.csproj
```

Current tests cover areas including:

* Brute-force detection thresholds
* Detection suppression
* Detection allowlists
* Maintenance mode
* Audit hash-chain verification
* Detection of modified stored audit records

---

# Building release binaries

Linux/macOS development host:

```bash
scripts/publish-all.sh
```

This builds:

```text
linux-x64 server
linux-x64 CLI
win-x64 server
win-x64 CLI
```

and skips the Windows-only Console.

On Windows:

```powershell
scripts\publish-all.ps1
```

This builds:

```text
win-x64 server
win-x64 CLI
win-x64 console
```

Output is written to:

```text
dist/
```

CI also builds the project on pushes to `main` and uploads the resulting build artifacts.

---

# Supported environments

| Component       | Windows | Linux |
| --------------- | :-----: | :---: |
| SentinelServer  |    ✓    |   ✓   |
| SentinelCli     |    ✓    |   ✓   |
| SentinelConsole |    ✓    |   —   |
| SentinelShared  |    ✓    |   ✓   |

SentinelCore is primarily designed for server and workstation environments where administrators want direct control over host security monitoring.

---

# Current V1 scope

SentinelCore v1.0 currently focuses on:

### Detection

* Brute-force login detection
* Port-scan detection
* Suspicious process monitoring
* Network monitoring
* Detection thresholds
* Allowlists
* Correlation/suppression

### Response

* IP blocking
* Automatic block expiration
* Process suspension
* Process termination
* Maintenance mode
* Dry-run response

### Investigation

* Incidents
* Incident timelines
* Correlation
* Audit records
* Audit-chain verification
* CSV reporting

### Management

* Windows administration console
* Cross-platform CLI
* HTTP API
* Multiple managed agents
* Role-based access control
* API keys

---

# V2 and future work

Some functionality intentionally remains outside the V1 scope.

Planned or deferred areas include:

* Centralized multi-host management
* Signed/versioned policy distribution
* Persistence detection
* Ransomware-style mass file modification detection
* USB monitoring
* Hash/signature-based process reputation
* Additional detection types and integrations

See [`CHANGELOG.md`](CHANGELOG.md) for the current status of these features.

The roadmap may change as SentinelCore is tested in real environments and new requirements are identified.

---

# Who is SentinelCore for?

SentinelCore is intended for people who want a **self-hosted host-security layer** and direct control over their monitoring and response infrastructure.

Potential use cases include:

* Small and medium-sized organizations
* System administrators
* Security teams
* Internal IT teams
* Security labs
* Development and test environments
* Linux and Windows server environments
* Organizations that prefer locally controlled security telemetry
* Teams building their own security automation

It can also be useful for security researchers and administrators who want to inspect and experiment with the detection and response pipeline themselves.

---

# Try SentinelCore

The easiest way to evaluate SentinelCore is to run it locally and use the built-in test mode.

```bash
git clone <this repo>
cd SentinelCore
dotnet restore SentinelCore.sln
dotnet run --project SentinelServer
```

Then enable:

```json
"testMode": {
  "enabled": true
}
```

and run:

```bash
sentinel simulate bruteforce 203.0.113.55
```

You can then inspect the resulting detection, incident, response and audit record.

No real attack traffic is required.

---

# Documentation

| Document                               | Description                                                                 |
| -------------------------------------- | --------------------------------------------------------------------------- |
| [`docs/SECURITY.md`](docs/SECURITY.md) | Security architecture, authentication, TLS, audit integrity and limitations |
| [`docs/API.md`](docs/API.md)           | HTTP API and role requirements                                              |
| [`CHANGELOG.md`](CHANGELOG.md)         | Release history, V1 functionality and deferred features                     |
| [`LICENSE`](LICENSE)                   | Source-available license and usage restrictions                             |

---

# Security disclosures

If you discover a security vulnerability in SentinelCore, please report it responsibly rather than publicly disclosing the vulnerability before it can be investigated.

For security-related issues and vulnerability reporting, please use the project's documented security contact/process.

---

# Commercial licensing

SentinelCore is distributed under a **custom source-available license**.

This means that having access to the source code does not automatically grant every type of commercial usage.

Commercial use, redistribution, sublicensing, commercial hosting, and other restricted uses may require a separate commercial license.

**Please read the complete [`LICENSE`](LICENSE) before deploying SentinelCore in a commercial environment.**

For commercial licensing inquiries:

**Arasaka Corp**
[contact@arasaka-corp.eu](mailto:contact@arasaka-corp.eu)

---

# Contributing

Contributions, bug reports, detection ideas, testing feedback and security research are welcome, subject to the project's license and contribution guidelines.

If you are testing SentinelCore:

* Tell us what operating system you used
* Describe your deployment
* Include relevant logs where safe
* Report false positives
* Report false negatives
* Describe resource usage
* Explain what you expected to happen
* Include reproduction steps for bugs

Real-world feedback is especially valuable for improving detection quality and making SentinelCore useful in different environments.

---

# Project status

**SentinelCore v1.0**

This is an actively developing security project.

The V1 feature set is intentionally limited and documented. Future development will be driven by testing, security research, operational feedback, and practical deployment requirements.

If you are interested in testing SentinelCore in a lab, development environment, or a small number of production endpoints, feedback and evaluation reports are welcome.

---

## SentinelCore

**Local security. Transparent detection. Controlled response.**

No mandatory cloud.
No hidden detection pipeline.
Your infrastructure, your telemetry, your response controls.
