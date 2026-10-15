@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_market_scan_background.ps1" %*
exit /b %errorlevel%
