#!/usr/bin/env bash
# (NK6) bash tools/run-tests.sh [--filter ...]; only the test project's configured databases.
set -u
set -o pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd -- "$repo_root" || exit 1
results_root="$repo_root/TestResults"
run_directory="$results_root/$(date +%Y%m%d-%H%M%S)"
while [[ -e "$run_directory" ]]; do
    sleep 1
    run_directory="$results_root/$(date +%Y%m%d-%H%M%S)"
done
mkdir -p -- "$run_directory" || exit 1
started=$(date +%s)
dotnet test tests/MindSprintAI_ASP.NET_MVC.Tests --logger 'trx;LogFileName=results.trx' --results-directory "$run_directory" "$@" 2>&1 |
    tee "$run_directory/console.log"
test_exit_code=${PIPESTATUS[0]}
{
    printf 'Exit code: %s\nDuration: %s seconds\n' "$test_exit_code" "$(( $(date +%s) - started ))"
    if [[ -f "$run_directory/results.trx" ]]; then
        sed -n 's/.*<Counters \([^>]*\)\/>.*/Totals: \1/p' "$run_directory/results.trx"
        printf 'Failed tests:\n'
        # TRX puts each UnitTestResult on one line; names stay XML-escaped for safe plain text.
        sed -n '/<UnitTestResult .*outcome="Failed"/s/.*testName="\([^"]*\)".*/\1/p' "$run_directory/results.trx"
    else
        printf 'No trx produced: see console.log (build/runner failure).\n'
    fi
} > "$run_directory/summary.txt"
# (NK6) Retention: 20 newest runs. Direct timestamp children only; ignore links.
mapfile -t runs < <(find "$results_root" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | grep -E '^[0-9]{8}-[0-9]{6}$' | sort -r)
for old_run in "${runs[@]:20}"; do
    [[ "$old_run" =~ ^[0-9]{8}-[0-9]{6}$ && ! -L "$results_root/$old_run" ]] || continue
    rm -rf -- "$results_root/$old_run"
done
exit "$test_exit_code"
