param(
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action = 'Start'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $projectRoot '.env.selfhost'
$composeArgs = @('compose', '--env-file', $envPath, '-f', (Join-Path $projectRoot 'compose.selfhost.yaml'), '--profile', 'public')
$statePath = Join-Path $projectRoot '.public.selfhost.json'

if ($Action -eq 'Stop') {
    & docker @composeArgs stop tunnel
    if ($LASTEXITCODE -ne 0) { throw 'Khong dung duoc tunnel.' }
    if (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath }
    Write-Host 'Da tat link Internet. Web localhost va SQL Server van chay.'
    return
}
if ($Action -eq 'Start') {
    & (Join-Path $PSScriptRoot 'selfhost.ps1')
    & docker @composeArgs up -d --no-recreate tunnel
    if ($LASTEXITCODE -ne 0) { throw 'Khong khoi dong duoc HTTPS tunnel.' }
}
$tunnelId = (& docker @composeArgs ps -q --status running tunnel) -join ''
if (!$tunnelId) { throw 'Tunnel khong chay. Dung lenh Start de chia se lai.' }
$startedAt = (& docker inspect --format '{{.State.StartedAt}}' $tunnelId) -join ''
if ($LASTEXITCODE -ne 0) { throw 'Khong doc duoc thoi diem khoi dong tunnel.' }
$url = $null
for ($attempt = 0; $attempt -lt 45; $attempt++) {
    $logs = (& docker @composeArgs logs --no-color --since $startedAt --tail 100 tunnel 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Khong doc duoc trang thai tunnel.' }
    $urls = [regex]::Matches($logs, 'https://[a-z0-9-]+\.trycloudflare\.com')
    if ($urls.Count -gt 0) { $url = $urls[$urls.Count - 1].Value; break }
    if ($Action -eq 'Status') { break }
    Start-Sleep -Seconds 1
}
if (!$url) { throw 'Chua co link tunnel. Kiem tra Docker va ket noi Internet; chay lai lenh Status.' }
if ($Action -eq 'Start') {
    $envLines = @(Get-Content -LiteralPath $envPath | Where-Object { $_ -notmatch '^PUBLIC_ORIGIN=' })
    [System.IO.File]::WriteAllLines($envPath, @($envLines) + "PUBLIC_ORIGIN=$url", (New-Object System.Text.UTF8Encoding($false)))
    & docker @composeArgs up -d --no-deps --wait --wait-timeout 90 web
    if ($LASTEXITCODE -ne 0) { throw 'Khong cap nhat duoc dia chi HTTPS cho web.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            if ((Invoke-RestMethod "$url/readyz" -TimeoutSec 5).status -eq 'ready') { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (!$ready) { throw 'Tunnel da tao nhung HTTPS chua truy cap duoc. Chay Status de lay link va thu lai.' }
}
[System.IO.File]::WriteAllText($statePath, (@{ url = $url } | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
$aliasPath = Join-Path $projectRoot '.public.alias.selfhost.json'
if (Test-Path -LiteralPath $aliasPath) {
    $alias = Get-Content -LiteralPath $aliasPath -Raw | ConvertFrom-Json
    if ($alias.upstream -eq $url) {
        Write-Host "Link ngan: $($alias.url)"
    } else {
        Write-Warning 'Tunnel da doi dia chi. Can cap nhat UPSTREAM_ORIGIN cua Sites de link ngan hoat dong lai.'
        Write-Host "Tunnel moi: $url"
    }
} else { Write-Host "Link chia se HTTPS: $url" }
if (Test-Path -LiteralPath $aliasPath) {
    Write-Host 'May va Docker phai dang chay. Ten ngan giu nguyen; can dong bo dich neu tunnel doi link.'
} else {
    Write-Host 'May va Docker phai dang chay. Link co the doi khi tunnel khoi dong lai.'
}
Write-Host 'Tat chia se: .\scripts\share.ps1 -Action Stop'
