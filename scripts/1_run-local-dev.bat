@echo off
title AqtCoreFW — Local Dev Runner
echo.
echo   Double-click this file to run the dev script.
echo   Window will stay open even if errors occur.
echo.
where pwsh >nul 2>&1 && (
    pwsh -NoExit -ExecutionPolicy Bypass -File "%~dp01_run-local-dev.ps1" %*
) || (
    powershell -NoExit -ExecutionPolicy Bypass -File "%~dp01_run-local-dev.ps1" %*
)
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo   [ERROR] Script failed with exit code %ERRORLEVEL%
    echo.
    pause
)
