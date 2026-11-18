param(
    [ValidateSet('Start', 'Stop', 'Status', 'Configure')]
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
    if ($Action -ne 'Configure') {
        docker info --format '{{.ServerVersion}}' *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Hay mo Docker Desktop va cho Docker Engine san sang, roi chay lai.' }
    }

    $envPath = Join-Path $projectRoot '.env.selfhost'
    if (!(Test-Path -LiteralPath $envPath)) {
        if ($Action -notin 'Start', 'Configure') { throw 'Chua co cau hinh demo. Hay chay lenh Configure hoac Start truoc.' }
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
        $lines = @("DB_PASSWORD=$dbPassword", "JWT_KEY=$jwtKey", 'DATABASE_NAME=mindsprint', 'SELFHOST_PORT=8080', "GEMINI_API_KEY='$geminiKey'", 'GOOGLE_CLIENT_ID=')
        $stream = [System.IO.File]::Open($envPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
        try {
            $writer = [System.IO.StreamWriter]::new($stream)
            try { foreach ($line in $lines) { $writer.WriteLine($line) } }
            finally { $writer.Dispose() }
        } finally { $stream.Dispose() }
        Protect-SecretPath $envPath
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
    $configJson = & docker @composeArgs config --format json 2>$null
    if ($LASTEXITCODE -ne 0) { throw '.env.selfhost hoac Compose khong hop le. Kiem tra DB_PASSWORD va JWT_KEY bat buoc.' }
    $config = $configJson -join "`n" | ConvertFrom-Json
    $databaseName = [string]$config.services.db.environment.MINDSPRINT_DATABASE
    if ($databaseName -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') {
        throw 'DATABASE_NAME chi duoc gom chu cai, chu so va dau gach duoi, bat dau bang chu cai.'
    }
    if ($config.services.db.environment.MSSQL_SA_PASSWORD -cnotmatch '^Ms1![A-Za-z0-9_-]{16,124}$') {
        throw 'DB_PASSWORD can 16-124 ky tu, chi gom chu cai, chu so, dau gach ngang va gach duoi.'
    }
    if (([string]$config.services.web.environment.Jwt__Key).Trim().Length -lt 32 -or
        $config.services.web.environment.Jwt__Key -match '^(replace|change[_-]?me|your|example|placeholder|doi[_-]?khoa)') {
        throw 'JWT_KEY can it nhat 32 ky tu va khong duoc dung gia tri mau.'
    }
    if ($config.services.db.environment.MSSQL_SA_PASSWORD -match '^Ms1!(replace|change[_-]?me|your|example|placeholder)') {
        throw 'DB_PASSWORD con gia tri mau. Hay dung mat khau ngau nhien.'
    }
    $webPortBinding = @($config.services.web.ports)[0]
    $webPort = 0
    if (!$webPortBinding -or $webPortBinding.host_ip -ne '127.0.0.1' -or
        [int]$webPortBinding.target -ne 8080 -or
        ![int]::TryParse([string]$webPortBinding.published, [ref]$webPort) -or
        $webPort -lt 1 -or $webPort -gt 65535) {
        throw 'Self-host phai publish web tren 127.0.0.1:<SELFHOST_PORT>:8080; khong bind ra LAN/Internet.'
    }
    if (@($config.services.db.ports).Count -gt 0) {
        throw 'SQL Server khong duoc publish cong tren host; chi web duoc truy cap qua loopback.'
    }
    if ($Action -eq 'Configure') {
        Write-Host 'Cau hinh .env.selfhost hop le.'
        return
    }

    if ($Action -eq 'Start') {
        $existingWebPort = (& docker @composeArgs port web 8080 2>$null) -join ''
        $thisStackOwnsPort = $LASTEXITCODE -eq 0 -and $existingWebPort.Trim() -eq "127.0.0.1:$webPort"
        if (!$thisStackOwnsPort) {
            $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse('127.0.0.1'), $webPort)
            try {
                $probe.Start()
            } catch [System.Net.Sockets.SocketException] {
                $ownerDetails = @()
                if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
                    $processIds = @(Get-NetTCPConnection -LocalPort $webPort -State Listen -ErrorAction SilentlyContinue |
                        Select-Object -ExpandProperty OwningProcess -Unique)
                    foreach ($processId in $processIds) {
                        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
                        if ($process) { $ownerDetails += "$($process.ProcessName) (PID $processId)" }
                    }
                }
                $ownerText = if ($ownerDetails.Count) { " Tien trinh: $($ownerDetails -join ',')." } else { '' }
                throw "Cong localhost $webPort dang duoc su dung.$ownerText Sua SELFHOST_PORT trong .env.selfhost (vi du 8081), sau do chay lai .\scripts\selfhost.ps1 Start. Ung dung van chi bind 127.0.0.1; khong mo firewall/router/tunnel."
            } finally {
                if ($probe.Server.IsBound) { $probe.Stop() }
            }
        }
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
        if ($envLines -cmatch '^GEMINI_API_KEY=') {
            [System.IO.File]::WriteAllLines($envPath, @($envLines | Where-Object { $_ -cnotmatch '^GEMINI_API_KEY=' }), (New-Object System.Text.UTF8Encoding($false)))
        }
        Protect-SecretPath $envPath
        & docker @composeArgs up -d --build --wait --wait-timeout 240 db web
        if ($LASTEXITCODE -ne 0) {
            throw "Khoi dong Docker Compose that bai. Neu cong $webPort vua bi tien trinh khac chiem, sua SELFHOST_PORT trong .env.selfhost sang cong trong (vi du 8081) va chay lai. Chi dung URL loopback; khong mo firewall/router/tunnel."
        }
        $health = Invoke-RestMethod "http://127.0.0.1:$webPort/readyz" -TimeoutSec 6
        if ($health.status -ne 'ready') { throw 'Web/database chua san sang.' }
        Write-Host "Web local: http://127.0.0.1:$webPort"
        Write-Host 'Web chi mo tren loopback; SQL Server chi nam trong mang Docker.'
    }
    Write-Host 'Tat sau buoi demo: .\scripts\selfhost.ps1 -Action Stop'
} finally { Pop-Location }
