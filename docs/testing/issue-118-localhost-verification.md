# Issue #118 — localhost port and access verification

**Assigned to:** Minh Chiến. **Current baseline inspected:** `f4c5488` (`main`, 2026-11-18).
**Scope:** Docker self-host frontend/API/SQL Server on the host laptop, default URL
`http://127.0.0.1:8080`. This issue replaces the former #49 work item and does not
represent acceptance of the old public-service availability requirement.

## Changes prepared

- `compose.selfhost.yaml` accepts `SELFHOST_PORT` (default 8080) and keeps the host
  binding fixed to `127.0.0.1`; SQL Server has no published host port.
- `scripts/selfhost.ps1` validates the generated Compose binding, checks for a busy
  loopback port before startup, identifies the owning process when possible, and gives
  the operator a safe alternate-port instruction. Readiness and the printed browser URL
  use the selected port.
- `Dockerfile.selfhost` already configures the frontend API base from
  `window.location.origin`; changing only the host port keeps frontend and API same-origin.
- `docs/SELF-HOST-DEMO.md` documents the local-only URL, port-change procedure, and scope.
- `tests/selfhost-port-config.ps1` renders Compose configuration for ports 8080 and 8081
  without starting containers or connecting to a real database.

## Local verification

Run the static configuration check:

```powershell
pwsh -File tests/selfhost-port-config.ps1
```

### Results from the Codex checkout

- PowerShell parser for `scripts/selfhost.ps1`: **Pass**.
- Compose config for the default port, explicit `SELFHOST_PORT=8080`, and
  `SELFHOST_PORT=8081`: **Pass**; web maps from `127.0.0.1:<port>` to container port
  8080, SQL Server has no host port, and the Google localhost origin follows the selected
  port.
- Synthetic loopback bind-collision probe: **Pass**; a second listener cannot bind the
  same loopback port. This validates the OS-level probe only; the live script behavior is
  still listed below for the owner laptop.
- Static check: **Pass**; self-host frontend API base is `window.location.origin`, and
  the default Start command does not enable the `public` tunnel profile.
- `git diff --check`: **Pass**.
- Docker containers, a database, and the currently running server were **not** started,
  changed, inspected, or used by these checks.

The current GitHub issue body contains an older sentence describing PostgreSQL. The
current `main` handoff and closed dependencies #53/#112 use SQL Server Express 2022; this
port task leaves the database provider unchanged and follows the current Compose baseline.

On the owner-operated host laptop, verify the actual port and browser routing using an
isolated test volume/database. Do not use an existing user database for fault tests.

1. Confirm the selected host port is free and run `scripts/selfhost.ps1 Start`.
2. Record `docker compose --env-file .env.selfhost -f compose.selfhost.yaml ps` and
   `docker compose --env-file .env.selfhost -f compose.selfhost.yaml port web 8080`.
   The published address must be `127.0.0.1:<SELFHOST_PORT>`; `db` must have no host port.
3. Open the URL printed by the script. In browser developer tools, verify HTML/assets and
   application API requests all use the same origin and the configured host port.
4. For the busy-port case, start a disposable listener on the selected loopback port and
   rerun `Start`. Confirm the script reports the conflict without starting/replacing the
   stack. Stop only that disposable test listener, set `SELFHOST_PORT=8081`, and start
   again. Confirm the page/API work at `http://127.0.0.1:8081` and Compose still binds
   only loopback.
5. Save sanitized Compose output, listener evidence, browser request evidence, source SHA,
   image ID, Docker/Compose versions, and the commands/results for the issue reviewer.

The live listener, browser-origin, and occupied-port cases remain **Blocked for owner
execution**. The Codex work environment has not started the owner's Docker stack, inspected
or changed the running server, modified Windows networking, or touched any real volume.
Do not open firewall/router ports or a tunnel for this localhost issue; HTTPS public access
and a public uptime SLO are outside its acceptance criteria.
