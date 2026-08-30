@echo off
setlocal
title ProBooks publish
cd /d "%~dp0"

echo.
echo Publishing a single Windows exe to publish\win
echo First run downloads extra files. Wait.
echo.

dotnet publish src\ContainerManagement -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win
if errorlevel 1 (
  echo Publish failed. Copy the error above.
  pause
  exit /b 1
)

echo.
echo Done. Run publish\win\ProBooks.exe
echo You can copy that folder to another PC. The shop data still lives in Documents\ProBooks.
pause
