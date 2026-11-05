@echo off
setlocal EnableExtensions
title Rocky Telecom - Stop
cd /d "%~dp0"

set "API_PORT=5129"
set "WEB_PORT=4200"

echo ============================================================
echo   ROCKY TELECOM  -  stopping everything
echo ============================================================
echo.

call :killPort %WEB_PORT%
call :killPort %API_PORT%

rem The console windows started by start.bat outlive a killed listener - close them too.
for /f "tokens=2" %%t in ('tasklist /fi "WINDOWTITLE eq Rocky Telecom API*" /fo table /nh 2^>nul ^| findstr /R /C:"^cmd" ') do (
  taskkill /F /T /PID %%t >nul 2>&1
)
for /f "tokens=2" %%t in ('tasklist /fi "WINDOWTITLE eq Rocky Telecom Web*" /fo table /nh 2^>nul ^| findstr /R /C:"^cmd" ') do (
  taskkill /F /T /PID %%t >nul 2>&1
)

echo.
echo ============================================================
echo   DONE
echo ------------------------------------------------------------
call :isListening %WEB_PORT%
if "%LISTENING%"=="1" (echo   Storefront : still up on %WEB_PORT% - see the error above) else (echo   Storefront : stopped)
call :isListening %API_PORT%
if "%LISTENING%"=="1" (echo   API        : still up on %API_PORT% - see the error above) else (echo   API        : stopped)
echo ============================================================
echo.
ping -n 4 127.0.0.1 >nul
exit /b 0

rem ---------------------------------------------------------------
rem  :killPort ^<port^>   kills whatever LISTENs on the port
rem ---------------------------------------------------------------
:killPort
set "FOUND=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%~1 .*LISTENING"') do (
  set "FOUND=1"
  taskkill /F /T /PID %%p >nul 2>&1
  if not errorlevel 1 echo   - stopped process %%p listening on port %~1
)
if "%FOUND%"=="0" echo   - nothing was listening on port %~1
exit /b 0

rem ---------------------------------------------------------------
rem  :isListening ^<port^>
rem ---------------------------------------------------------------
:isListening
set "LISTENING=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%~1 .*LISTENING"') do set "LISTENING=1"
exit /b 0
