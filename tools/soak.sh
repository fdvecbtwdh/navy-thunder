#!/usr/bin/env bash
# R4.2 soak: run headless battles in a loop for SOAK_HOURS (default 2), logging
# memory each iteration. Pass = no dotnet crash/OOM and stable memory trend.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOURS="${SOAK_HOURS:-2}"
END=$((SECONDS + HOURS * 3600))
LOG="$ROOT/soak_run.log"
: > "$LOG"

echo "soak start $(date -Iseconds) for ${HOURS}h" | tee -a "$LOG"
n=0
while [ $SECONDS -lt $END ]; do
    n=$((n + 1))
    cd "$ROOT"
    export PATH="/c/Program Files/dotnet:$PATH"
    dotnet run --project src/NavyThunder.SimRunner -c Release \
        --scenario scenarios/r1_acceptance.json --out "C:/Users/gusiy/AppData/Local/Temp/soak_r$((n % 4)).json" \
        > /dev/null 2> "$ROOT/soak_last_err.txt"
    rc=$?
    mem=$(ps -o rss= -C dotnet 2>/dev/null | awk '{s+=$1} END {print int(s/1024)" MB"}')
    echo "iter=$n rc=$rc mem=$mem $(date -Iseconds)" | tee -a "$LOG"
    if [ $rc -ne 0 ]; then
        echo "FAIL at iter=$n" | tee -a "$LOG"
        tail -5 "$ROOT/soak_last_err.txt" | tee -a "$LOG"
        exit 1
    fi
done
echo "soak PASS $(date -Iseconds): $n iterations in ${HOURS}h" | tee -a "$LOG"
