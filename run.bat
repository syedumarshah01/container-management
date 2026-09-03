@echo off
setlocal
title ProBooks
cd /d "%~dp0"

echo.
echo === ProBooks ===
echo First run downloads packages. That can take 1-3 minutes. Wait for a WINDOW, not a website.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET SDK not found in PATH.
  echo Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
  echo Then close this window, open a NEW command prompt, and run run.bat again.
  pause
  exit /b 1
)

echo [1/3] Restoring packages...
dotnet restore src\ContainerManagement
if errorlevel 1 (
  echo Restore failed. Copy the error above.
  pause
  exit /b 1
)

echo.
echo [2/3] Building...
dotnet build src\ContainerManagement -c Debug --no-restore
if errorlevel 1 (
  echo Build failed. Copy the error above.
  pause
  exit /b 1
)

set "EXE=%~dp0src\ContainerManagement\bin\Debug\net8.0\ProBooks.exe"
if not exist "%EXE%" (
  echo Build succeeded but ProBooks.exe was not found at:
  echo %EXE%
  pause
  exit /b 1
)

echo.
echo [3/3] Opening the desktop window...
start "" "%EXE%"
exit /b 0
