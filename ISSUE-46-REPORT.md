# Issue #46: NFR-05 Security Verification

## Result

Issue #46 changes have been pushed to `main`. GitHub Actions run #87 for `83d8ed9` succeeded, including Release backend build, all 7 backend-check jobs, and frontend/browser tests. This report does not mark the issue accepted: Render deployment was skipped because deployment credentials are missing, and Vercel is still serving stale assets that fail the secret-artifact and AI-disclosure criteria. The live Production demo POST and deployed logs remain unverified.

Assessment time: 2026-11-04 12:41:52 +07:00 (Asia/Saigon). Git commit: `c777123e3c22e0e84c3d98f6803252a5d11fe143`. No deployed image/commit was verified.

## Acceptance status

| Criterion | Status | Evidence / remaining work |
| --- | --- | --- |
| Demo endpoint and fixed demo credentials are unavailable in Production | Pass (isolated controller harness) | `SecurityChecks`: Production demo route returns 404 without account creation; fixed demo identity is rejected at regular login; Development demo still works. |
| Production JWT/CORS configuration fails startup when missing or unsafe | Pass (configuration harness) | `SecurityChecks`: missing DB/JWT/CORS, short/placeholder JWT, localhost DB, and HTTP CORS origin are rejected; valid HTTPS allowlist is accepted. |
| Frontend build excludes fixed demo password | Local source/artifact pass; deployed fail | Legacy source flow is removed and the temporary artifact test passes, but cache-busted public `GET /js/auth.js` still has the fixed demo credential/handler pattern. Deploy Vercel, then repeat the GET/scan. |
| AI privacy disclosure is visible | Local and CI pass; deployed fail | Local source/test and CI browser job pass; cache-busted deployed `GET /notebook.js` still lacks the Gemini disclosure. Deploy Vercel and capture a browser screenshot. |
| JWT/refresh token and other-account source data are absent from AI prompts | Pass (mock-provider harness) | `TutorChecks`: cross-account source isolation and request-header token sentinels are absent from the prompt. |
| HTTPS frontend/API and live CORS headers | Pass (public deployment check) | Frontend runtime config points to the HTTPS API. Allowed Vercel Origin preflight returned `204` with the matching `Access-Control-Allow-Origin`; an untrusted Origin returned `204` without CORS allow headers. |
| Production Swagger | Pass (public deployment check) | `GET /swagger/index.html` returned `404`. |
| Production demo endpoint | Blocked | Did not POST to live production because it is not confirmed as isolated staging and could write to the database. The isolated Production controller harness passes. |
| Deployed logs and response secret scan | Blocked | No hosting log access. Do not send sensitive values or use personal data for verification. |
| No secrets in newly produced frontend artifact | Local pass; deployed fail | Temporary artifact test passes, but the live legacy JavaScript asset exposes the fixed demo credential. This does not constitute a repository-history scan. |

## Local commands

Run from the repository root. These commands passed with .NET SDK 8.0.427 and Node.js v26.13.0:

```powershell
dotnet run --project backend/SecurityChecks/SecurityChecks.csproj
dotnet run --project backend/TutorChecks/TutorChecks.csproj
node --test tests/frontend-session.test.cjs tests/frontend-text.test.cjs tests/frontend-validation.test.cjs tests/nfr05-frontend.test.cjs
```

Results: `SecurityChecks` 15/15 passed; `TutorChecks` 60/60 passed; frontend tests 22/22 passed. CI run #87 also passed the full frontend/browser suite after the expanded-chat layout adjustment. The .NET harnesses use an in-memory SQLite database; TutorChecks uses a mocked provider. No check calls Gemini or alters production data. The missing `.ToListAsync()` compile issue in `WeakAreaService` was fixed in commit `3eda0a5`. Deployment evidence must be collected separately with synthetic test accounts and redacted HAR/logs. Do not send real personal documents during verification.

Render workflow job is green but its “Trigger Render deployment” step was skipped; “Report missing deployment credentials” ran. Configure `RENDER_SERVICE_ID` and `RENDER_API_KEY` as GitHub Actions secrets to enable deployment. Vercel still requires a successful Git integration/manual deployment; public assets remain stale.

## Public deployment spot checks

At 2026-11-04 09:00:25 +07:00, the frontend at `https://mindsprint-ai-d-n-frontend.vercel.app` returned successfully and its public runtime config pointed to `https://mindsprint-api-zyur.onrender.com`. API `/healthz` returned `200` with `{"status":"ok"}`. The Vercel origin received the expected CORS allow-origin header; `https://untrusted.example` received none. `/swagger/index.html` returned `404`.

At 2026-11-04 12:41:52 +07:00, cache-busted public requests still returned the fixed-demo pattern/handler in `/js/auth.js` and no Gemini disclosure in `/notebook.js`. No credential is reproduced here. Do not invoke the live demo POST until isolated staging is available.

## Git-Connected Copy

The Git-connected clone is `C:\Users\Windows\tuan1\MindSprint-AI-D-n-clone`. Commits `4bbdcae`, `3eda0a5`, `83d8ed9`, and `c777123` are pushed to `origin/main`; the clone is synchronized. CI passed on `83d8ed9`. Vercel assets are still stale, and Render deployment credentials are missing.
