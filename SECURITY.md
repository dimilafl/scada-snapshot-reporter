# Security Policy

## Sensitive Data

This is a public repository. The following must NEVER be committed:

- Real OT/ICS/SCADA data from any production or test environment
- Generated reports (HTML, CSV) from real environments
- Hostnames, IP addresses, or DNS names from real networks
- Domain names, service account names, or user accounts
- DSNs, database names, server names, or instance names
- Share names, file paths, or UNC paths from real environments
- Certificate subjects, issuers, or thumbprints from real environments
- Firewall rules, network zones, or security group information
- Vendor-specific production configuration (any vendor)
- Connection strings, credentials, tokens, or keys of any kind

## What is Safe

- Synthetic demo sample data under `samples/demo/`
- Configuration templates with placeholder values
- Collector scripts (read-only, no credentials embedded)
- Report engine source code (C#)
- Test fixtures with invented/demo data only

## Pre-Commit Checklist

Before any commit, run:

```powershell
.\scripts\public_audit.ps1
```

This script scans all files for known sensitive patterns. It must pass (zero hits)
before committing.

## Reporting a Security Issue

If you discover sensitive data in this repository, please open an issue or
contact the maintainers. Do not disclose the sensitive data publicly.
