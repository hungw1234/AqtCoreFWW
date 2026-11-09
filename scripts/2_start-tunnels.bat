@echo off
title AqtCoreFW - Start 3 Cloudflare Tunnels
echo.
echo   =============================================
echo   AqtCoreFW - Start Cloudflare Tunnels
echo   =============================================
echo.

REM Find cloudflared
set "CF="
where cloudflared >nul 2>&1 && set "CF=cloudflared"
if "%CF%"=="" if exist "C:\Program Files (x86)\cloudflared\cloudflared.exe" set "CF=C:\Program Files (x86)\cloudflared\cloudflared.exe"
if "%CF%"=="" if exist "C:\Program Files\cloudflared\cloudflared.exe" set "CF=C:\Program Files\cloudflared\cloudflared.exe"

if "%CF%"=="" (
    echo   [ERROR] cloudflared not found!
    echo   Run: winget install Cloudflare.cloudflared
    pause
    exit /b 1
)

echo   Found: %CF%
echo.
echo   Nhan phim bat ky de khoi dong 3 tunnels...
pause >nul
echo.

start "Tunnel-Auth" cmd /k ""%CF%" tunnel --url http://localhost:5002"
echo   [OK] Tunnel Auth :5002 starting...
timeout /t 3 >nul

start "Tunnel-API" cmd /k ""%CF%" tunnel --url http://localhost:5001"
echo   [OK] Tunnel API :5001 starting...
timeout /t 3 >nul

start "Tunnel-FE" cmd /k ""%CF%" tunnel --url http://localhost:5173"
echo   [OK] Tunnel FE :5173 starting...

echo.
echo   =============================================
echo   3 tunnels started! Copy URLs roi chay:
echo     scripts\3_setup-tunnel-urls.bat
echo   =============================================
echo.
pause
