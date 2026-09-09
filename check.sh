#!/usr/bin/env bash
# Runs the money checks against a throwaway database. See check.bat on Windows.
cd "$(dirname "$0")"
if pgrep -x ProBooks >/dev/null 2>&1; then
  echo "ProBooks is open. Closing it so the build can write new files..."
  pkill -x ProBooks || true
  sleep 3
  pkill -9 -x ProBooks || true
fi
dotnet restore tools/MoneyChecks >/dev/null
dotnet run --project tools/MoneyChecks -c Debug
code=$?
if [ "$code" -eq 0 ]; then
  echo "All money checks passed."
else
  echo "$code money check(s) FAILED."
fi
exit $code
