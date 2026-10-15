@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_finance_backfill_background.ps1" %*
exit /b %errorlevel%
