@echo off
title AqtCoreFW — Expose Demo to Internet
echo.
echo   ============================================
echo   AqtCoreFW — Expose Local to Internet
echo   ============================================
echo.
echo   Choose a tunnel method:
echo.
echo   [1] Cloudflare Tunnel (FREE, multi-service, custom domain)
echo       - Requires: cloudflared CLI
echo       - Install: winget install Cloudflare.cloudflared
echo.
echo   [2] ngrok (FREE tier = 1 tunnel at a time)
echo       - Requires: ngrok CLI + account
echo       - Install: winget install ngrok.ngrok
echo.
echo   [3] VS Code Port Forwarding (FREE, requires GitHub login)
echo       - Built-in to VS Code, no install needed
echo       - Open VS Code ^> Ctrl+Shift+P ^> "Forward a Port"
echo.
set /p choice="Enter choice (1/2/3): "

if "%choice%"=="1" goto cloudflare
if "%choice%"=="2" goto ngrok
if "%choice%"=="3" goto vscode
echo Invalid choice.
pause
exit /b 1

:cloudflare
echo.
echo   === Cloudflare Tunnel ===
echo.
where cloudflared >nul 2>&1 || (
    echo   cloudflared not found. Installing...
    winget install Cloudflare.cloudflared
    echo   Restart this script after install.
    pause
    exit /b 1
)
echo   Starting Cloudflare Quick Tunnel (no account needed)...
echo   This will expose Frontend on a random *.trycloudflare.com URL
echo.
echo   Option A: Quick tunnel (random URL, no signup):
echo     cloudflared tunnel --url http://localhost:5173
echo.
echo   Option B: Named tunnel (custom subdomain, needs free account):
echo     cloudflared tunnel login
echo     cloudflared tunnel create aqtcorefw-demo
echo     Then create config file (see below)
echo.
echo   Starting quick tunnel for Frontend (port 5173)...
echo   Share the URL that appears below with your demo audience.
echo.
cloudflared tunnel --url http://localhost:5173
goto end

:ngrok
echo.
echo   === ngrok ===
echo.
where ngrok >nul 2>&1 || (
    echo   ngrok not found. Installing...
    winget install ngrok.ngrok
    echo   After install, run: ngrok config add-authtoken YOUR_TOKEN
    echo   Get token at: https://dashboard.ngrok.com/get-started/your-authtoken
    pause
    exit /b 1
)
echo   Which service to expose?
echo   [1] Frontend  (localhost:5173) — for UI demo
echo   [2] API       (localhost:5001) — for API testing
echo   [3] Auth      (localhost:5002) — for OIDC testing
echo.
set /p svc="Enter choice (1/2/3): "
if "%svc%"=="1" (ngrok http 5173)
if "%svc%"=="2" (ngrok http 5001)
if "%svc%"=="3" (ngrok http 5002)
goto end

:vscode
echo.
echo   === VS Code Port Forwarding ===
echo.
echo   1. Open VS Code in this project
echo   2. Ctrl+Shift+P ^> "Ports: Forward a Port"
echo   3. Forward these ports:
echo      - 5173 (Frontend)
echo      - 5001 (API)
echo      - 5002 (Auth Server)
echo   4. Set visibility to "Public" for demo access
echo   5. Share the generated URLs
echo.
echo   Or from terminal in VS Code:
echo     code --cli tunnel --accept-server-license-terms
echo.
pause
goto end

:end
echo.
pause
