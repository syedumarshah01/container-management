#!/usr/bin/env bash
# Runs the money checks against a throwaway database. See check.bat on Windows.
cd "$(dirname "$0")"
if pgrep -x ProBooks >/dev/null 2>&1; then
  echo "ProBooks is open. Closing it so the build can write new files..."
  pkill -x ProBooks || true
  sleep 3
  pkill -9 -x ProBooks || true
fi
# One character in a page stops the build for the same reason a bad figure stops these checks, and the gate
# reads the whole repo in a second. See check.bat, which runs the same thing.
if command -v python3 >/dev/null 2>&1; then
  python3 tools/check_quotes.py || { echo "The markup gate stopped this run. It names the file and the line."; exit 3; }
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
