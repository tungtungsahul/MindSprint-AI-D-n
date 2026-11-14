# Issue #119 - Laptop Operations Runbook Evidence

Date: 2026-11-15 (Asia/Saigon)
Issue: https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/119
Branch: `docs/issue-119-operations`
Base: `origin/main` at `452426d00559bd98bfebb13704006062ee7aa7c0`.
Baseline: SQL Server Express 2022 and localhost-only Production stack, accepted in issue #105 / PR #122 and integrated into main. The old PostgreSQL wording in #119 is superseded for the laptop stack; PostgreSQL hosting elsewhere is not changed.

## Runbook additions

Updated [the self-host operator guide](../SELF-HOST-DEMO.md) with:

- Start, stop, status and restart instructions, including readiness verification.
- Safe update procedure from a clean approved checkout: record source/image, backup and verify, fast-forward source, rebuild, and run smoke.
- Recovery guidance that restores to a new database and warns that restoring data does not reverse EF migrations or prove code/schema compatibility.
- Troubleshooting for Docker Desktop/daemon unavailable, port 8080 in use, database readiness failure, missing Gemini key/network access, and low disk space.
- Explicit safeguards not to print/share secrets, remove Docker volumes, run `down -v`, or delete backups as a routine fix.

## Verification Performed

| Check | Result | Notes |
|---|---|---|
| PowerShell parser over fenced commands in `docs/SELF-HOST-DEMO.md` | PASS: 17 blocks parsed | Syntax-only; Docker commands were not executed. |
| Runbook command/topology assertions | PASS: 11 assertions | Checked documented restart/update/backup/troubleshooting commands against existing scripts and SQL Server Production Compose config. |
| Relative Markdown links in the runbook | PASS: 7 checked | Fenced code excluded from link parsing. |
| NUL/control byte scan and `git diff --check` | PASS | No NUL bytes or whitespace errors. |
| `python -m pytest -q` | PASS: 48 passed | Repository regression suite; not a Docker runtime test. |
| Docker Compose, clean checkout start, restart, backup/restore, live browser/API/DB smoke | BLOCKED | Docker CLI/Engine is not installed/available in this environment. No live stack or test volume was touched. |
| Node/.NET build checks | BLOCKED / not required by docs-only change | Node.js is unavailable; the current .NET host has runtime only and no SDK. |

Baseline runtime evidence is available in the [10 Nov SQL Server self-host report](selfhost-2026-11-10.md), which records a clean image build, SQL Server/browser/API smoke and backup/restore checks. That evidence belongs to the baseline image/commit, not this documentation branch, and is not represented as a new run of the procedures above.

## Acceptance Status

The operator guidance and its PowerShell syntax/link consistency checks are complete. The requested live trial from a clean checkout/configuration, including actual restart, update, DB recovery and disk/port fault scenarios, remains blocked until run on a laptop with Docker Desktop and a disposable test volume. Do not mark the runtime scenarios PASS based on static checks or inherited baseline results. No Windows reboot/sleep, production database or real user data was used for this work.