@echo off
title Klausurfix
echo ========================================================
echo   Klausurfix wird gestartet...
echo   100%% Offline, verschluesselt und lokal auf Ihrem Surface.
echo ========================================================
echo.

cd /d "%~dp0"

:: Start the backend service in the background
start "" /b dotnet run --project src/Klausurfix.Web/Klausurfix.Web.csproj --launch-profile "" > nul 2>&1

:: Wait 2 seconds for the server to bind to loopback
timeout /t 2 /nobreak > nul

:: Open Edge or default browser
start http://localhost:5000

echo Klausurfix laeuft unter http://localhost:5000
echo Schliessen Sie dieses Fenster erst, wenn Sie die Arbeit beenden moechten.
echo.
pause

