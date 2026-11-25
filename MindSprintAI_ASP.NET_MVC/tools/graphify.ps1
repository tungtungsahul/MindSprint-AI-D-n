# Run from any directory. Uses the uv-installed executable if PATH is stale.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$graphifyCommand = Get-Command graphify -CommandType Application -ErrorAction SilentlyContinue
$graphifyExecutable = if ($graphifyCommand) { $graphifyCommand.Source } else { Join-Path $env:USERPROFILE '.local\bin\graphify.exe' }
if (-not (Test-Path -LiteralPath $graphifyExecutable)) {
    throw 'Graphify is missing. Install graphifyy with uv, then restart the terminal.'
}
Push-Location -LiteralPath $projectRoot
try {
    & $graphifyExecutable @args
    $graphifyExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $graphifyExitCode
