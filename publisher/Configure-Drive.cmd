@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Drive.ps1" -Action Configure
set "ARPG_EXIT=%ERRORLEVEL%"
if not "%ARPG_EXIT%"=="0" echo Configuration did not complete. Read the message above.
pause
exit /b %ARPG_EXIT%
