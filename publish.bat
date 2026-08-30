@echo off
setlocal
title ProBooks publish
cd /d "%~dp0"

echo.
echo Publishing a single Windows exe to publish\win
echo First run downloads extra files. Wait.
echo.

tasklist /FI "IMAGENAME eq ProBooks.exe" 2>nul | find /I "ProBooks.exe" >nul
if not errorlevel 1 (
  echo ProBooks is open. Closing it so the new exe can be written...
  taskkill /IM ProBooks.exe /F >nul 2>&1
  timeout /t 2 /nobreak >nul
)

dotnet publish src\ContainerManagement -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win
if errorlevel 1 (
  echo.
  echo Publish failed.
  echo If it says ProBooks.exe is being used, close ProBooks and run publish.bat again.
  pause
  exit /b 1
)

echo.
echo Done. Run publish\win\ProBooks.exe
echo You can copy that folder to another PC. The shop data still lives in Documents\ProBooks.
pause
