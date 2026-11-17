param(
    [string]$BaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'

$page = Invoke-WebRequest -Uri "$BaseUrl/" -UseBasicParsing
if ($page.StatusCode -ne 200 -or $page.Content -notmatch 'MindSprintAI') {
    throw 'Frontend smoke check failed.'
}
Write-Output 'PASS frontend returns the MindSprint page'

$health = Invoke-RestMethod -Uri "$BaseUrl/healthz"
if ($health.status -ne 'ok') {
    throw 'API health check failed.'
}
Write-Output 'PASS API health check through the frontend proxy'

$database = docker compose exec -T db sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
if ($LASTEXITCODE -ne 0) {
    throw 'PostgreSQL readiness check failed.'
}
Write-Output "PASS PostgreSQL is ready: $database"