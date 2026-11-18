#Requires -Version 7.3
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$composeSource = Join-Path $repo 'compose.selfhost.yaml'
$dockerfileSource = Join-Path $repo 'Dockerfile.selfhost'
$composeText = Get-Content -LiteralPath $composeSource -Raw
$selfhostScript = Get-Content -LiteralPath (Join-Path $repo 'scripts/selfhost.ps1') -Raw
$dockerfile = Get-Content -LiteralPath $dockerfileSource -Raw

if (!(Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker CLI is required for Compose config validation.' }
if ($selfhostScript -notmatch 'SELFHOST_PORT' -or $selfhostScript -notmatch '127\.0\.0\.1' -or
    $selfhostScript -notmatch '8081') {
    throw 'Start script must validate loopback binding and explain how to choose another host port.'
}
if ($dockerfile -notmatch 'window\.location\.origin' -or $dockerfile -notmatch 'sameOrigin: true') {
    throw 'Self-host frontend runtime config must route API calls through the page origin.'
}
if ($composeText -notmatch 'profiles:\s*\[public\]' -or $selfhostScript -notmatch 'up -d --build --wait --wait-timeout 240 db web') {
    throw 'Default startup must leave the public tunnel profile disabled.'
}

$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase "mindsprint-selfhost-port-check-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $tempRoot
$resolvedRoot = [System.IO.Path]::GetFullPath($tempRoot)
$expectedParent = [System.IO.Path]::GetFullPath($tempBase).TrimEnd([char[]]@('\', '/'))
$actualParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $resolvedRoot)).TrimEnd([char[]]@('\', '/'))
if ($actualParent -ne $expectedParent) { throw 'Temporary test directory escaped the system temp directory.' }

try {
    Copy-Item -LiteralPath $composeSource -Destination (Join-Path $tempRoot 'compose.selfhost.yaml')
    Copy-Item -LiteralPath $dockerfileSource -Destination (Join-Path $tempRoot 'Dockerfile.selfhost')
    $secretDirectory = Join-Path $tempRoot '.secrets.selfhost'
    $null = New-Item -ItemType Directory -Path $secretDirectory
    [System.IO.File]::WriteAllText((Join-Path $secretDirectory 'gemini-api-key'), '')

    $cases = @(
        [pscustomobject]@{ Name = 'default'; Port = 8080; SetPort = $false },
        [pscustomobject]@{ Name = '8080'; Port = 8080; SetPort = $true },
        [pscustomobject]@{ Name = '8081'; Port = 8081; SetPort = $true }
    )
    foreach ($testCase in $cases) {
        $port = $testCase.Port
        $envFile = Join-Path $tempRoot ".env.$($testCase.Name)"
        $envLines = @(
            'DB_PASSWORD=0123456789abcdef0123456789abcdef',
            ('JWT_KEY=' + ('a' * 64)),
            'DATABASE_NAME=mindsprint_port_check',
            'GOOGLE_CLIENT_ID='
        )
        if ($testCase.SetPort) { $envLines += "SELFHOST_PORT=$port" }
        [System.IO.File]::WriteAllLines($envFile, $envLines, [System.Text.UTF8Encoding]::new($false))

        $configOutput = & docker compose -p "mindsprint-port-check-$($testCase.Name)" --project-directory $tempRoot `
            --env-file $envFile -f (Join-Path $tempRoot 'compose.selfhost.yaml') config --format json 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Docker Compose config validation failed for SELFHOST_PORT=$port; no container was started."
        }
        $configJson = [string]::Join("`n", @($configOutput | ForEach-Object { [string]$_ }))
        $config = $configJson | ConvertFrom-Json
        $webBindings = @($config.services.web.ports)
        if ($webBindings.Count -ne 1 -or
            [string]$webBindings[0].host_ip -ne '127.0.0.1' -or
            [int]$webBindings[0].target -ne 8080 -or
            [string]$webBindings[0].published -ne [string]$port) {
            throw "Unexpected web port mapping for SELFHOST_PORT=$port."
        }
        $dbPorts = @()
        if ($config.services.db.PSObject.Properties.Name -contains 'ports') {
            $dbPorts = @($config.services.db.ports | Where-Object { $null -ne $_ })
        }
        if ($dbPorts.Count -ne 0) {
            throw 'SQL Server unexpectedly has a published host port.'
        }
        if ($config.services.web.environment.Google__AllowedOrigins__0 -ne "http://127.0.0.1:$port") {
            throw "Google localhost origin did not follow SELFHOST_PORT=$port."
        }
        Write-Output "PASS $($testCase.Name) port config maps 127.0.0.1:$port to web:8080; SQL Server has no host port."
    }

    $heldPort = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $heldPort.Start()
    $busyPort = [int]$heldPort.LocalEndpoint.Port
    $portProbe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $busyPort)
    $conflictDetected = $false
    try {
        $portProbe.Start()
    } catch [System.Net.Sockets.SocketException] {
        $conflictDetected = $true
    } finally {
        if ($portProbe.Server.IsBound) { $portProbe.Stop() }
        $heldPort.Stop()
    }
    if (!$conflictDetected) { throw 'Busy loopback port was not detected by the port preflight probe.' }
    Write-Output 'PASS busy loopback port is rejected by the same TCP bind probe used before startup.'

    Write-Output 'PASS self-host frontend API base is same-origin; public tunnel is not enabled by default.'
    Write-Output 'This test only renders Compose configuration; it starts no containers and touches no database.'
}
finally {
    if (Test-Path -LiteralPath $resolvedRoot) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
