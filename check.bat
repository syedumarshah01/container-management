@echo off
setlocal
title ProBooks money checks
cd /d "%~dp0"

echo.
echo === ProBooks: money checks ===
echo Runs the real services against a throwaway database in your temp folder. Documents\ProBooks
echo is never opened. First run downloads packages, which takes a minute or two.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET SDK not found in PATH. Install .NET 8 SDK, then open a NEW command prompt.
  pause
  exit /b 1
)

rem The checks build the app project too, and a running copy locks its output files.
tasklist /FI "IMAGENAME eq ProBooks.exe" 2>nul | find /I "ProBooks.exe" >nul
if not errorlevel 1 (
  echo ProBooks is open. Closing it so the build can write new files...
  taskkill /IM ProBooks.exe >nul 2>&1
  timeout /t 3 /nobreak >nul
  taskkill /IM ProBooks.exe /F >nul 2>&1
)

dotnet restore tools\MoneyChecks >nul
dotnet run --project tools\MoneyChecks -c Debug
set CODE=%ERRORLEVEL%

echo.
if "%CODE%"=="0" (
  echo All money checks passed. Notes above are things to decide about, not failures.
) else (
  echo %CODE% money check^(s^) FAILED. Copy everything above into the chat.
)
pause
exit /b %CODE%
