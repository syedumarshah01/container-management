#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
echo "=== ProBooks ==="
echo "First run downloads packages (1-3 minutes). A desktop window should open."
dotnet restore src/ContainerManagement
dotnet build src/ContainerManagement -c Debug --no-restore
echo "Opening the desktop window..."
dotnet run --project src/ContainerManagement -c Debug --no-build --no-hot-reload
