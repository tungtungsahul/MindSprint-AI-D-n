param(
    [ValidateSet('Backup', 'Restore', 'VerifyRestore')]
    [string]$Action = 'Backup',
    [string]$BackupFile,
    [string]$Directory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'backups')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$composeArgs = @('compose', '--env-file', (Join-Path $projectRoot '.env.selfhost'), '-f', (Join-Path $projectRoot 'compose.selfhost.yaml'))
$operationId = [Guid]::NewGuid().ToString('N')
$containerFile = "/var/opt/mssql/backup/selfhost_$operationId.bak"
$restoreDatabase = "mindsprint_restore_$operationId"
$restoreAttempted = $false
$keepRestored = $false
$containerPrepared = $false
$previousEncoding = $OutputEncoding
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Invoke-DatabaseSql([string]$Query) {
    # Password stays inside the container; never put it in arguments or command output.
    $Query | & docker @composeArgs exec -T db bash -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r1 -l 5 -t 120 -W -h -1 -s "|" -x'
    if ($LASTEXITCODE -ne 0) { throw 'SQL Server operation failed. The active database was not replaced.' }
}

try {
    if ($Action -ne 'Backup') {
        if (!$BackupFile) { throw 'Restore/VerifyRestore requires -BackupFile.' }
        $source = Get-Item -LiteralPath $BackupFile
        if ($source.PSIsContainer -or $source.Extension -ne '.bak') { throw 'Choose a SQL Server .bak file.' }
    } elseif ($BackupFile) { throw 'Use -Directory to select the backup destination.' }

    # Capture config instead of printing it: it contains credentials.
    $configJson = & docker @composeArgs config --format json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read self-host configuration.' }
    $config = ($configJson -join "`n") | ConvertFrom-Json
    $database = [string]$config.services.db.environment.MINDSPRINT_DATABASE
    if (!$database) { $database = 'mindsprint' }
    if ($database -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') { throw 'DATABASE_NAME must be a SQL identifier using letters, digits and underscores.' }

    & docker @composeArgs exec -T db mkdir -p /var/opt/mssql/backup
    if ($LASTEXITCODE -ne 0) { throw 'Cannot prepare the container backup directory.' }
    $containerPrepared = $true

    if ($Action -eq 'Backup') {
        $null = Invoke-DatabaseSql "BACKUP DATABASE [$database] TO DISK = N'$containerFile' WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$containerFile' WITH CHECKSUM;"
        $destination = [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetFullPath($Directory)).FullName
        $path = Join-Path $destination "$database-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$operationId.bak"
        $partialPath = "$path.partial"
        & docker @composeArgs cp "db:$containerFile" $partialPath
        if ($LASTEXITCODE -ne 0) { throw "Backup copy failed. Incomplete file: $partialPath" }
        Move-Item -LiteralPath $partialPath -Destination $path
        [pscustomobject]@{ BackupFile = $path; Database = $database; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        return
    }

    & docker @composeArgs cp $source.FullName "db:$containerFile"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot copy the backup into SQL Server.' }
    $null = Invoke-DatabaseSql "RESTORE VERIFYONLY FROM DISK = N'$containerFile' WITH CHECKSUM;"
    $files = @(Invoke-DatabaseSql "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'$containerFile';")
    $moves = @()
    foreach ($line in $files) {
        $columns = ([string]$line).Split('|')
        if ($columns.Count -lt 3) { continue }
        $logical = $columns[0].Trim()
        $kind = $columns[2].Trim()
        if ($logical -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$' -or $kind -notin @('D', 'L')) {
            throw 'This backup has unsupported logical file names or types.'
        }
        $extension = if ($kind -eq 'L') { 'ldf' } else { 'mdf' }
        $moves += "MOVE N'$logical' TO N'/var/opt/mssql/data/${restoreDatabase}_$($moves.Count).$extension'"
    }
    if ($moves.Count -lt 2) { throw 'The backup must contain database and log files.' }
    $null = Invoke-DatabaseSql "IF DB_ID(N'$restoreDatabase') IS NOT NULL THROW 50001, 'Restore target already exists.', 1;"
    $restoreAttempted = $true
    # Always restore to fresh files and a fresh database. Never use WITH REPLACE.
    # https://learn.microsoft.com/sql/linux/migrate/tutorial-restore-backup-sql-server-container
    $null = Invoke-DatabaseSql "RESTORE DATABASE [$restoreDatabase] FROM DISK = N'$containerFile' WITH $($moves -join ', '), CHECKSUM, RECOVERY; DBCC CHECKDB ([$restoreDatabase]) WITH NO_INFOMSGS;"
    $counts = @(Invoke-DatabaseSql "SET NOCOUNT ON; USE [$restoreDatabase]; SELECT (SELECT COUNT(*) FROM dbo.Users), (SELECT COUNT(*) FROM dbo.Flashcards), (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory);")
    $keepRestored = $Action -eq 'Restore'
    [pscustomobject]@{ Database = $restoreDatabase; Verified = $true; Retained = $keepRestored; CountsUsersCardsMigrations = ($counts -join '').Trim() }
} finally {
    try {
        if ($restoreAttempted -and !$keepRestored) {
            $null = Invoke-DatabaseSql "IF DB_ID(N'$restoreDatabase') IS NOT NULL BEGIN ALTER DATABASE [$restoreDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$restoreDatabase]; END;"
        }
    } finally {
        if ($containerPrepared) {
            & docker @composeArgs exec -T db rm -f -- $containerFile
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove temporary container file $containerFile" }
        }
        $OutputEncoding = $previousEncoding
    }
}
