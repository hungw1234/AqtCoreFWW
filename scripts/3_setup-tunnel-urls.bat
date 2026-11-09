@echo off
title AqtCoreFW - Setup Tunnel URLs
echo.
echo   =============================================
echo   AqtCoreFW - Cap nhat Tunnel URLs
echo   =============================================
echo.

if "%1"=="--restore" (
    python "%~dp03_setup-tunnel-urls.py" --restore
    pause
    exit /b 0
)

echo   Nhap 3 URLs tu cloudflared tunnel output:
echo.

set /p FE_URL="  Frontend URL (vd: https://xxx.trycloudflare.com): "
set /p API_URL="  API URL      (vd: https://yyy.trycloudflare.com): "
set /p AUTH_URL="  Auth URL     (vd: https://zzz.trycloudflare.com): "

python "%~dp03_setup-tunnel-urls.py" "%FE_URL%" "%API_URL%" "%AUTH_URL%"

pause
