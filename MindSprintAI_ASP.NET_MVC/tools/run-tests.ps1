# (NK6) Từ repo hoặc thư mục bất kỳ: .\tools\run-tests.ps1 [--filter ...]
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$TestArguments)
$ErrorActionPreference = 'Continue'
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resultsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'TestResults'))
$runDirectory = Join-Path $resultsRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
while (Test-Path -LiteralPath $runDirectory) {
    Start-Sleep -Seconds 1
    $runDirectory = Join-Path $resultsRoot (Get-Date -Format 'yyyyMMdd-HHmmss')
}
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$started = Get-Date
Push-Location $repoRoot
try {
    & dotnet test tests/MindSprintAI_ASP.NET_MVC.Tests --logger 'trx;LogFileName=results.trx' --results-directory $runDirectory @TestArguments 2>&1 |
        ForEach-Object { $_.ToString() } |
        Tee-Object -FilePath (Join-Path $runDirectory 'console.log')
    $testExitCode = $LASTEXITCODE
    if ($null -eq $testExitCode) { $testExitCode = 1 }
    $summary = @("Exit code: $testExitCode", "Duration: $((Get-Date) - $started)")
    $trxPath = Join-Path $runDirectory 'results.trx'
    if (Test-Path -LiteralPath $trxPath) {
        [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        $summary += "Total: $($counters.total); Passed: $($counters.passed); Failed: $($counters.failed); Not executed: $($counters.notExecuted)"
        $summary += 'Failed tests:'
        $summary += @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -eq 'Failed' | ForEach-Object testName)
    } else { $summary += 'No trx produced: see console.log (build/runner failure).' }
    $summary | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.txt') -Encoding UTF8
    # (NK6) Chỉ xóa thư mục kết quả đúng mẫu trong TestResults; không đi qua junction/symlink.
    Get-ChildItem -LiteralPath $resultsRoot -Directory | Where-Object {
        $_.Name -match '^\d{8}-\d{6}$' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
    } | Sort-Object Name -Descending | Select-Object -Skip 20 | ForEach-Object {
        $oldRun = [IO.Path]::GetFullPath($_.FullName)
        if ($oldRun.StartsWith($resultsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $oldRun -Recurse -Force
        }
    }
} finally { Pop-Location }
exit $testExitCode
