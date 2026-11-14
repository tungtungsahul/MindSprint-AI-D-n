#Requires -Version 7.3
param([string]$Revision = 'HEAD', [string]$DockerIgnoreOverride, [string]$ExistingImage, [switch]$AuditOnly)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$sha = git -C $repo rev-parse --verify "$Revision^{commit}"
$project = 'mindsprint-review-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$run = Join-Path $repo "output/$project"
$source = Join-Path $run 'source'
$context = Join-Path $run 'context'
$null = New-Item -ItemType Directory -Path $source
$archive = Join-Path $run 'source.tar'
git -C $repo archive --format=tar "--output=$archive" $sha
tar -xf $archive -C $source
Remove-Item -LiteralPath $archive
$evidence = Join-Path $run 'evidence.txt'

function Write-Evidence([string]$Message) {
    $Message | Tee-Object -FilePath $evidence -Append
}

function Assert-Status([string]$Phase, [string]$Path, [int]$Status, [string]$Body) {
    $result = curl.exe --silent --show-error --max-time 8 --write-out "`n%{http_code} %{time_total}" "$base$Path"
    if ($result.Count -ne 2 -or $result[1] -notmatch '^(\d{3}) (\d+\.\d+)$') {
        throw "Unexpected curl output for $Path"
    }
    $actualStatus = [int]$Matches[1]
    $seconds = [double]::Parse($Matches[2], [cultureinfo]::InvariantCulture)
    $actualBody = ($result[0] | ConvertFrom-Json).status
    Write-Evidence "$([DateTimeOffset]::UtcNow.ToString('o')) $Phase $Path HTTP $actualStatus $($Matches[2])s status=$actualBody"
    if ($actualStatus -ne $Status -or $actualBody -ne $Body -or $seconds -ge 8) {
        throw "Unexpected result for $Phase $Path"
    }
}

Write-Evidence "Revision: $sha"
Write-Evidence "Project: $project"
Write-Evidence ("Docker client / daemon: " + (docker version --format '{{.Client.Version}} / {{.Server.Version}}'))
Write-Evidence (docker compose version)
Write-Evidence (python --version)
if ($DockerIgnoreOverride) {
    $ignore = Join-Path $source '.dockerignore'
    Copy-Item -LiteralPath $DockerIgnoreOverride -Destination $ignore
    Write-Evidence ("Overlay applied only to .dockerignore. SHA256: " + (Get-FileHash -LiteralPath $ignore).Hash)
}

$auditDockerfile = Join-Path $run 'Dockerfile.context-audit'
@('FROM scratch', 'COPY . /') | Set-Content -LiteralPath $auditDockerfile -Encoding ascii
$canaries = @(
    '.env.selfhost', '.secrets.selfhost/gemini-api-key',
    'backend/MindSprint.Api/appsettings.Development.json', 'frontend/.env.local',
    'backend/MindSprint.Api/appsettings.Local.json',
    'output/review-canary.log', 'backups/review-canary.bak',
    'frontend/logs/review-canary.txt', 'backend/MindSprint.Api/backups/review-canary.txt',
    'frontend/review-canary.log', 'backend/MindSprint.Api/review-canary.bak',
    'frontend/output/review-canary.txt'
)
foreach ($item in $canaries) {
    $path = Join-Path $source $item
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path)
    Set-Content -LiteralPath $path -Value 'synthetic context canary' -Encoding ascii
}

try {
    Write-Evidence 'Context audit: docker build --no-cache -f Dockerfile.context-audit --output type=local,dest=<context> <source>'
    docker build --progress plain --no-cache -f $auditDockerfile --output "type=local,dest=$context" $source
    if (!(Test-Path -LiteralPath (Join-Path $context 'frontend/build.mjs')) -or
        !(Test-Path -LiteralPath (Join-Path $context 'backend/MindSprint.Api/MindSprint.Api.csproj'))) {
        throw 'BuildKit context export did not include expected source files'
    }
    $leaks = @($canaries | Where-Object { Test-Path -LiteralPath (Join-Path $context $_) })
    if ($leaks.Count -gt 0) {
        Write-Evidence "FAIL context canaries included: $($leaks -join ', ')"
        throw 'Build context includes forbidden files'
    }
    Get-ChildItem -LiteralPath $context -File -Force -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($context, $_.FullName)
    } | Sort-Object | Set-Content -LiteralPath (Join-Path $run 'context-files.txt')
    Write-Evidence "PASS context excluded all $($canaries.Count) canaries"
} finally {
    foreach ($item in $canaries) { Remove-Item -LiteralPath (Join-Path $source $item) }
}
if ($AuditOnly) { return }

$image = $ExistingImage
if (!$image) {
    $image = "${project}:$($sha.Substring(0, 8))"
    Write-Evidence 'Build: docker build --no-cache --label org.opencontainers.image.revision=<SHA> -t <image> -f Dockerfile.selfhost <source>'
    $build = [Diagnostics.Stopwatch]::StartNew()
    docker build --progress plain --no-cache --label "org.opencontainers.image.revision=$sha" -t $image -f (Join-Path $source 'Dockerfile.selfhost') $source
    Write-Evidence "Build elapsed seconds: $([math]::Round($build.Elapsed.TotalSeconds, 2))"
} elseif ((docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' $image) -ne $sha) {
    throw 'Existing image revision differs from the source revision'
}
Write-Evidence ("Web image ID, revision: " + (docker image inspect --format '{{.Id}} {{index .Config.Labels "org.opencontainers.image.revision"}}' $image))

$manifest = docker run --pull never --rm --network none --entrypoint sh $image -c 'find /app -type f | sort'
$manifest | Set-Content -LiteralPath (Join-Path $run 'image-files.txt')
$badFiles = @($manifest | Where-Object { $_ -match '(?i)(/\.env[^/]*$|/\.secrets\.selfhost/|/appsettings\.(?!json$)[^/]+\.json$|/[^/]+\.(bak|log)$|/(logs|backups|output)/)' })
if ($badFiles.Count -or '/app/MindSprint.Api.dll' -notin $manifest -or '/app/wwwroot/index.html' -notin $manifest) {
    throw "Unexpected runtime image contents: $($badFiles -join ', ')"
}
Write-Evidence "PASS runtime /app manifest ($($manifest.Count) files). No env files, private settings, backup or log files matched."

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$base = "http://127.0.0.1:$port"
$envFile = Join-Path $source '.env.selfhost'
$secretDir = Join-Path $source '.secrets.selfhost'
$null = New-Item -ItemType Directory -Force -Path $secretDir
[IO.File]::WriteAllText((Join-Path $secretDir 'gemini-api-key'), '')
$dbKey = [Guid]::NewGuid().ToString('N')
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
@("DB_PASSWORD=$dbKey", "JWT_KEY=$jwtKey", 'DATABASE_NAME=mindsprint_review', 'GOOGLE_CLIENT_ID=') |
    Set-Content -LiteralPath $envFile -Encoding ascii
$override = Join-Path $run 'compose.verify.yaml'
@"
services:
  web:
    image: $image
    environment:
      Logging__Console__FormatterName: json
    ports: !override
      - "127.0.0.1:${port}:8080"
"@ | Set-Content -LiteralPath $override -Encoding ascii
$compose = @('compose', '-p', $project, '--project-directory', $source, '--env-file', $envFile,
    '-f', (Join-Path $source 'compose.selfhost.yaml'), '-f', $override)

try {
    Write-Evidence "Test URL: $base (isolated project/volume; SQL Server Express 2022; Production)"
    docker @compose config --quiet
    Write-Evidence 'Startup: docker compose <isolated args> up -d --no-build --wait --wait-timeout 240 db web'
    docker @compose up -d --no-build --wait --wait-timeout 240 db web
    $db = docker @compose ps -q db
    $web = docker @compose ps -q web
    Write-Evidence ("SQL Server image ID: " + (docker inspect --format '{{.Image}}' $db))
    Assert-Status 'before' '/healthz' 200 'ok'
    Assert-Status 'before' '/readyz' 200 'ready'
    Write-Evidence 'Smoke: python tests/selfhost-smoke.py <test URL>'
    python (Join-Path $source 'tests/selfhost-smoke.py') $base | ForEach-Object { Write-Evidence $_ }
    $outageStart = [DateTimeOffset]::UtcNow.ToString('o')
    try {
        Write-Evidence 'Outage: docker compose <isolated args> stop db'
        docker @compose stop db
        if ((docker inspect --format '{{.State.Running}}' $db) -ne 'false') { throw 'DB is still running' }
        Assert-Status 'db-down' '/healthz' 200 'ok'
        Assert-Status 'db-down' '/readyz' 503 'unavailable'
    } finally {
        Write-Evidence 'Recovery: docker compose <isolated args> start --wait --wait-timeout 180 db'
        $recovery = [Diagnostics.Stopwatch]::StartNew()
        docker @compose start --wait --wait-timeout 180 db
    }
    Assert-Status 'recovered' '/healthz' 200 'ok'
    Assert-Status 'recovered' '/readyz' 200 'ready'
    Write-Evidence "DB start through successful readiness, seconds: $([math]::Round($recovery.Elapsed.TotalSeconds, 3))"
    if ((docker @compose ps -q web) -ne $web -or (docker inspect --format '{{.RestartCount}}' $web) -ne '0') {
        throw 'Web restarted during DB recovery'
    }
    $webLogs = docker logs --timestamps --since $outageStart $web 2>&1
    $loggedUnhealthy = $false
    foreach ($line in $webLogs) {
        if ($line -notmatch '^([0-9TZ:+.-]+)\s+(\{.*\})$') { continue }
        $timestamp = $Matches[1]
        $entry = $Matches[2] | ConvertFrom-Json
        if ($entry.Category -notlike 'Microsoft.Extensions.Diagnostics.HealthChecks.*') { continue }
        if ($entry.Message -match 'Health check database (?:with status Unhealthy completed after|threw an unhandled exception after|timed out after) ([0-9.]+)\s*ms') {
            Write-Evidence "$timestamp [app log event $($entry.EventId)] database Unhealthy after $($Matches[1]) ms"
            $loggedUnhealthy = $true
        }
    }
    if (!$loggedUnhealthy) { throw 'Unhealthy health-check event was missing from the filtered logs' }
    Write-Evidence 'PASS DB down/up, with the same web container and zero restarts.'
} finally {
    docker @compose down --volumes
    Remove-Item -LiteralPath $envFile, (Join-Path $secretDir 'gemini-api-key')
    Write-Evidence 'Test stack and volume removed.'
}
