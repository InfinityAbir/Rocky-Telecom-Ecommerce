@echo off
setlocal EnableExtensions
title Rocky Telecom - Start
cd /d "%~dp0"

set "API_PORT=5129"
set "WEB_PORT=4200"
set "WEB_URL=http://localhost:%WEB_PORT%"
set "API_HEALTH=http://localhost:%API_PORT%/health"

echo ============================================================
echo   ROCKY TELECOM  -  one-click development start
echo ============================================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [X] .NET SDK not found. Install .NET 10 SDK and run this file again.
  goto :fail
)

where npm >nul 2>&1
if errorlevel 1 (
  echo [X] Node.js / npm not found. Install Node.js 20+ and run this file again.
  goto :fail
)

if not exist "frontend\node_modules" (
  echo [*] First run: installing frontend dependencies ^(this takes a minute^)...
  pushd frontend
  call npm install --no-audit --no-fund
  if errorlevel 1 (
    popd
    echo [X] npm install failed. Fix the error above and try again.
    goto :fail
  )
  popd
  echo.
)

rem ---------------------------------------------------------------
rem  API
rem ---------------------------------------------------------------
call :isListening %API_PORT%
if "%LISTENING%"=="1" (
  echo [i] API is already listening on port %API_PORT% - reusing it.
) else (
  echo [*] Starting API on %API_HEALTH% ...
  start "Rocky Telecom API" /D "%~dp0backend\RockyTelecom.Api" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --urls http://localhost:%API_PORT%"
)

rem ---------------------------------------------------------------
rem  Storefront
rem ---------------------------------------------------------------
call :isListening %WEB_PORT%
if "%LISTENING%"=="1" (
  echo [i] Storefront is already listening on port %WEB_PORT% - reusing it.
) else (
  echo [*] Starting storefront on %WEB_URL% ...
  start "Rocky Telecom Web" /D "%~dp0frontend" cmd /k npm start
)

echo.
echo [*] Waiting for both servers to answer...

set /a tries=0
:wait
set /a tries+=1

call :isListening %API_PORT%
if "%LISTENING%"=="0" (
  if %tries% GEQ 180 goto :timeout
  ping -n 3 127.0.0.1 >nul
  goto :wait
)

call :isListening %WEB_PORT%
if "%LISTENING%"=="0" (
  if %tries% GEQ 180 goto :timeout
  ping -n 3 127.0.0.1 >nul
  goto :wait
)

rem The port opens a moment before the first bundle is ready - give it a beat.
ping -n 5 127.0.0.1 >nul

echo.
echo ============================================================
echo   READY
echo ------------------------------------------------------------
echo   Storefront : %WEB_URL%
echo   Admin      : %WEB_URL%/admin
echo   API        : http://localhost:%API_PORT%/api/v1
echo   Swagger    : http://localhost:%API_PORT%/swagger
echo.
echo   Dev admin  : 01700000000  /  Rocky#2026
echo   ^(seeded from appsettings.Development.json^)
echo ------------------------------------------------------------
echo   Stop everything with stop.bat
echo ============================================================
echo.

start "" "%WEB_URL%"
goto :eof

:timeout
echo.
echo [X] The servers did not come up in time.
echo     Check the two windows that opened ^(API / Web^) for the actual error.
goto :fail

:fail
echo.
pause
exit /b 1

rem ---------------------------------------------------------------
rem  :isListening ^<port^>   sets LISTENING=1 when something listens
rem ---------------------------------------------------------------
:isListening
set "LISTENING=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%~1 .*LISTENING"') do set "LISTENING=1"
exit /b 0
