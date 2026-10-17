@echo off
rem DshShell installer launcher.
rem Double-click this file, or run: install.cmd -Start
chcp 65001 >nul
cd /d "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 (
  echo Windows PowerShell not found. Please run install.ps1 manually.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" echo Installer exited with code %RC%.
pause
