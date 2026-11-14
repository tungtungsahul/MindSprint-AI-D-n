# Issue #109 - Verification Evidence

Date: 2026-11-15 (Asia/Saigon)
Issue: https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/109
Branch: `fix/issue-109-same-origin`
Implementation commit: `7db0309c0020c7ec3be99ec0b63fcc61b5e759f9`
Base: current `origin/main` after merged self-host baseline PR #122 and issue #106 PR #123.
Laptop DB baseline: SQL Server Express 2022 per issue #105 and PR #122, not the superseded PostgreSQL wording in the original #109 description.

## Change

The self-host image already builds the frontend into the ASP.NET `wwwroot` and sets its API base to `window.location.origin`. This change marks that runtime explicitly as same-origin and makes the client ignore a previously saved `api-base` override in this mode. This prevents a laptop browser that used an older frontend/API port split from continuing to call `localhost:5000`. Cloud builds keep their configured API origin.

## Checks Run For This Change

| Check | Result | Scope |
|---|---|---|
| Browser check, same-origin mode | PASS | Loaded the changed `frontend/api.js` in the integrated browser; mocked `fetch`; with cached `http://localhost:5000`, `getMe()` requested `/api/auth/me`. No API or DB was contacted. |
| Browser check, cloud mode | PASS | With the cache cleared and API base `https://api.example.test`, `getMe()` requested `https://api.example.test/api/auth/me`. `fetch` was mocked. |
| `python -m pytest -q` | PASS: 48 passed | Repository Python regression suite. |
| PowerShell baseline assertions | PASS | Confirmed Compose enables Production and `Hosting:ServeFrontend`, uses SQL Server, binds web to `127.0.0.1:8080`, and the self-host image packages frontend assets and same-origin runtime config. |
| `git diff --check` | PASS | No whitespace errors before commit. |
| `node --test tests/frontend-same-origin.test.cjs` | BLOCKED | Node.js is unavailable on the host. A focused CI regression test was added but was not run locally. |
| `dotnet run --project backend/SecurityChecks/SecurityChecks.csproj` | BLOCKED | .NET runtime 8.0.3 is installed; no .NET SDK is installed, so the C# configuration harness/build could not run. |
| Docker build / Compose startup and recovery tests | BLOCKED | Docker CLI/Engine is unavailable. No image was built from implementation commit `7db0309c`. |
| Production browser -> API -> SQL Server smoke on this commit | NOT RUN | Requires the Docker image from this exact commit and a disposable test database. |

## Existing Baseline Evidence

The [self-host verification report for 10 Nov](https://github.com/tungtungsahul/MindSprint-AI-D-n/blob/0cbbce18843270c555dae8d8f60e93dfe543af0c/docs/testing/selfhost-2026-11-10.md) records real Production browser/API/SQL Server smoke checks for the accepted laptop baseline. It includes:

- Candidate baseline source SHA: `497e6a4eb3752f4e460caf0836d6a544cee8208a`.
- Clean baseline image: `mindsprint-issue105:497e6a4`, ID `sha256:776ece715fa1b67f01aebbc01a559efcd2008a28c0ee19dff92c110f746cbf15`.
- Reported checks include same-origin static/API access, SQL Server readiness, Production Swagger/demo restrictions, and authenticated browser workflows.

These are inherited baseline results, not an image or runtime result for `7db0309c`. The linked report is the source for its commands, environment and detailed results; do not treat its image ID as the issue #109 image.

## Startup Configuration Coverage And Remaining Work

The baseline already has fail-fast Compose requirements for `DB_PASSWORD` and `JWT_KEY`; `scripts/selfhost.ps1 -Action Configure` validates secret length/placeholders; `SecurityChecks` covers missing DB/JWT/CORS and allows no CORS allowlist only when `Hosting:ServeFrontend` is enabled. Those checks were not re-executed in this environment because .NET/Docker are unavailable. The requested negative startup cases, valid-config recovery, and localhost browser/API/DB smoke must still be run against a disposable stack built from this commit. Capture redacted logs and browser evidence, then request reviewer @James-Lloyd20 to compare the results.

No production database, real account, Gemini provider, or secret was used for the checks in this report. Issue #109 is not claimed complete or accepted by this report.