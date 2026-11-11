$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dbScript = Join-Path $projectRoot 'scripts/selfhost-db.ps1'
$composeArgs = @('compose', '--env-file', (Join-Path $projectRoot '.env.selfhost'), '-f', (Join-Path $projectRoot 'compose.selfhost.yaml'))
function Query([string]$Sql) {
    $rows = $Sql | & docker @composeArgs exec -T db bash -c 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -r1 -W -h -1 -x'
    if ($LASTEXITCODE -ne 0) { throw 'Database verification failed.' }
    return ($rows -join '').Trim()
}
function MustReject([scriptblock]$Operation) {
    $rejected = $false
    try { & $Operation } catch { $rejected = $true }
    if (!$rejected) { throw 'An invalid restore request was accepted.' }
}

MustReject { & $dbScript -Action Restore }
MustReject { & $dbScript -Action Restore -BackupFile (Join-Path $projectRoot 'README.md') }
$backup = & $dbScript -Action Backup
if (!(Test-Path -LiteralPath $backup.BackupFile) -or $backup.SHA256.Length -ne 64) { throw 'Backup was not exported.' }
if ($backup.Database -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') { throw 'Invalid database name.' }
$active = $backup.Database
$before = Query "SET NOCOUNT ON; SELECT DB_ID(N'$active');"
$verified = & $dbScript -Action VerifyRestore -BackupFile $backup.BackupFile
if (!$verified.Verified -or $verified.Retained) { throw 'Restore verification failed.' }
if ((Query "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$($verified.Database)';") -ne '0') {
    throw 'Disposable restore database was not removed.'
}
$restored = $null
try {
    $restored = & $dbScript -Action Restore -BackupFile $backup.BackupFile
    if (!$restored.Verified -or !$restored.Retained -or $restored.Database -cnotmatch '^mindsprint_restore_[0-9a-f]{32}$') {
        throw 'Restore must retain a fresh, separately named database.'
    }
    foreach ($table in @('Users', 'Flashcards', 'Tasks', 'Notebooks', 'NotebookSources', '__EFMigrationsHistory')) {
        $sourceHash = Query "SET NOCOUNT ON; SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM(*)), 0) FROM [$active].dbo.[$table];"
        $restoredHash = Query "SET NOCOUNT ON; SELECT ISNULL(CHECKSUM_AGG(BINARY_CHECKSUM(*)), 0) FROM [$($restored.Database)].dbo.[$table];"
        if ($sourceHash -ne $restoredHash) { throw "Restored table differs: $table (run without concurrent edits)." }
    }
    if ((Query "SET NOCOUNT ON; SELECT DB_ID(N'$active');") -ne $before) { throw 'The active database was replaced.' }
} finally {
    if ($restored -and $restored.Database -cmatch '^mindsprint_restore_[0-9a-f]{32}$') {
        $null = Query "ALTER DATABASE [$($restored.Database)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$($restored.Database)];"
    }
}
$corrupt = Join-Path (Split-Path -Parent $backup.BackupFile) ("corrupt-" + [Guid]::NewGuid().ToString('N') + '.bak')
try {
    [System.IO.File]::WriteAllText($corrupt, 'This is not a SQL Server backup.')
    MustReject { & $dbScript -Action VerifyRestore -BackupFile $corrupt }
} finally { Remove-Item -LiteralPath $corrupt }
Write-Host 'PASS: backup checksums, isolated restore, table checksums, invalid/corrupt backup rejection and active database preservation.'
