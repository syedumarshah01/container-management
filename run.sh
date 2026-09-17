#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
echo "=== ProBooks ==="
echo "First run downloads packages (1-3 minutes). A desktop window should open."
# A running copy keeps the build from writing its own output files.
if pgrep -x ProBooks >/dev/null 2>&1; then
  echo "ProBooks is open. Closing it so the build can write new files..."
  pkill -x ProBooks || true
  sleep 3
  pkill -9 -x ProBooks || true
fi

dotnet restore src/ContainerManagement
dotnet build src/ContainerManagement -c Debug --no-restore
echo "Opening the desktop window..."
dotnet run --project src/ContainerManagement -c Debug --no-build --no-hot-reload
