@echo off
setlocal
title ProBooks publish
cd /d "%~dp0"

echo.
echo Publishing ProBooks to publish\win
echo Copy that whole folder to the customer PC. Double-click ProBooks.exe.
echo First run downloads extra files. Wait.
echo.

tasklist /FI "IMAGENAME eq ProBooks.exe" 2>nul | find /I "ProBooks.exe" >nul
if not errorlevel 1 (
  echo ProBooks is open. Closing it so files can be written...
  taskkill /IM ProBooks.exe /F >nul 2>&1
  timeout /t 2 /nobreak >nul
)

if exist publish\win (
  echo Clearing the old publish folder...
  rmdir /s /q publish\win
)

dotnet publish src\ContainerManagement -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o publish\win
if errorlevel 1 (
  echo.
  echo Publish failed.
  echo If it says a file is in use, close ProBooks and run publish.bat again.
  pause
  exit /b 1
)

echo.
echo Done. Open publish\win\ProBooks.exe
echo Copy the entire publish\win folder to the customer. Do not copy only the exe.
echo Shop data still lives in Documents\ProBooks.
pause
