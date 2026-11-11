param(
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action = 'Start'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
function Protect-SecretPath([string]$Path, [bool]$IsDirectory = $false) {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
    $acl = if ($IsDirectory) { New-Object System.Security.AccessControl.DirectorySecurity } else { New-Object System.Security.AccessControl.FileSecurity }
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = if ($IsDirectory) { [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit' } else { [System.Security.AccessControl.InheritanceFlags]::None }
    foreach ($identity in @($currentUser, $system)) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', $inheritance, 'None', 'Allow')))
    }
    # Persist only the modified access rules. Audit rules require SeSecurityPrivilege.
    $item = Get-Item -LiteralPath $Path -Force
    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        $item.SetAccessControl($acl)
    } else {
        [System.IO.FileSystemAclExtensions]::SetAccessControl($item, $acl)
    }
}
Push-Location $projectRoot
try {
    docker info --format '{{.ServerVersion}}' *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Hay mo Docker Desktop va cho Docker Engine san sang, roi chay lai.' }

    $envPath = Join-Path $projectRoot '.env.selfhost'
    if (!(Test-Path -LiteralPath $envPath)) {
        if ($Action -ne 'Start') { throw 'Chua co cau hinh demo. Hay chay lenh Start truoc.' }
        $randomBytes = New-Object byte[] 32
        $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try {
            $random.GetBytes($randomBytes)
            $dbPassword = -join ($randomBytes | ForEach-Object { $_.ToString('x2') })
            $random.GetBytes($randomBytes)
            $jwtKey = -join ($randomBytes | ForEach-Object { $_.ToString('x2') })
        } finally { $random.Dispose() }
        $geminiKey = [string]$env:GEMINI_API_KEY
        if (!$geminiKey) {
            $settingsPath = Join-Path $projectRoot 'backend/MindSprint.Api/appsettings.Development.json'
            if (Test-Path -LiteralPath $settingsPath) {
                $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
                $geminiKey = [string]$settings.Gemini.ApiKey
            }
        }
        if ($geminiKey -match 'YOUR_|^\s*$') { $geminiKey = '' }
        if ($geminiKey -match "['\r\n]") { throw 'GEMINI_API_KEY khong hop le.' }
        $lines = @("DB_PASSWORD=$dbPassword", "JWT_KEY=$jwtKey", "GEMINI_API_KEY='$geminiKey'", 'GOOGLE_CLIENT_ID=')
        [System.IO.File]::WriteAllLines($envPath, $lines, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host 'Da tao .env.selfhost (cau hinh rieng, khong commit Git).'
    }

    $composeArgs = @('compose', '--env-file', $envPath, '-f', 'compose.selfhost.yaml')
    if ($Action -eq 'Status') {
        & docker @composeArgs ps --all
        if ($LASTEXITCODE -ne 0) { throw 'Khong doc duoc trang thai Docker.' }
        return
    }
    if ($Action -eq 'Stop') {
        & docker @composeArgs down
        if ($LASTEXITCODE -ne 0) { throw 'Khong dung duoc bo demo.' }
        Write-Host 'Da tat web. Du lieu database van duoc giu.'
        return
    }
    if ($Action -eq 'Start') {
        $secretDirectory = Join-Path $projectRoot '.secrets.selfhost'
        $secretPath = Join-Path $secretDirectory 'gemini-api-key'
        $null = [System.IO.Directory]::CreateDirectory($secretDirectory)
        Protect-SecretPath $secretDirectory $true
        $envLines = @(Get-Content -LiteralPath $envPath)
        $apiKey = [string]$env:GEMINI_API_KEY
        foreach ($line in $envLines) {
            if (!$apiKey -and $line -match '^GEMINI_API_KEY=(.*)$') {
                $apiKey = $Matches[1].Trim().Trim([char]39).Trim([char]34)
            }
        }
        if ($apiKey -match '[\r\n]') { throw 'Gemini key khong hop le.' }
        if ($apiKey -or !(Test-Path -LiteralPath $secretPath)) {
            [System.IO.File]::WriteAllText($secretPath, $apiKey, (New-Object System.Text.UTF8Encoding($false)))
        }
        Protect-SecretPath $secretPath
        # Migrate the key out of .env only after its private secret file is written.
        [System.IO.File]::WriteAllLines($envPath, @($envLines | Where-Object { $_ -notmatch '^GEMINI_API_KEY=' }), (New-Object System.Text.UTF8Encoding($false)))
        Protect-SecretPath $envPath
        $configJson = & docker @composeArgs config --format json
        if ($LASTEXITCODE -ne 0) { throw 'Cau hinh Docker khong hop le.' }
        $databaseName = [string](($configJson -join "`n" | ConvertFrom-Json).services.db.environment.MINDSPRINT_DATABASE)
        if ($databaseName -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') {
            throw 'DATABASE_NAME chi duoc gom chu cai, chu so va dau gach duoi, bat dau bang chu cai.'
        }
        & docker @composeArgs up -d --build --wait --wait-timeout 240 db web
        if ($LASTEXITCODE -ne 0) { throw 'Khoi dong that bai. Kiem tra loi Docker phia tren.' }
        $health = Invoke-RestMethod 'http://127.0.0.1:8080/readyz' -TimeoutSec 6
        if ($health.status -ne 'ready') { throw 'Web/database chua san sang.' }
        Write-Host 'Web local: http://localhost:8080'
        Write-Host 'Cong Docker chi mo tren localhost. Chia se HTTPS: .\scripts\share.ps1'
    }
    Write-Host 'Tat sau buoi demo: .\scripts\selfhost.ps1 -Action Stop'
} finally { Pop-Location }
