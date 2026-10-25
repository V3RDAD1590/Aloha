@echo off
REM ============================================================
REM  aloha_devtool.bat - Aloha's DevTools console.
REM
REM  The inspector used to be a WinForms window pretending to be a
REM  terminal. This is the terminal. Same masthead, same verbs,
REM  same red-on-black - but it is cmd.exe, so it scrolls, selects,
REM  pipes, and resizes like every other console on the machine,
REM  and nobody has to maintain a fake one.
REM
REM  It is a plain batch file on purpose: you can read it, edit it,
REM  double-click it, or call it from anywhere. Aloha only starts it.
REM
REM  Usage:  aloha_devtool.bat [port]      (default 9333)
REM ============================================================

title Aloha DevTool
color 04

set "PORT=%~1"
if "%PORT%"=="" set "PORT=9333"

set "HERE=%~dp0"
set "PROBE=%HERE%aloha_probe.py"

if not exist "%PROBE%" (
  color 0C
  echo.
  echo   aloha_probe.py was not found next to this batch file.
  echo   Expected: "%PROBE%"
  echo.
  pause
  exit /b 1
)

REM Prefer the py launcher when present - it picks a sane interpreter on
REM machines where "python" is the Store stub that opens the Microsoft Store.
where py >nul 2>&1
if %ERRORLEVEL%==0 (
  py -3 "%PROBE%" --port %PORT% --theme devtool
) else (
  python "%PROBE%" --port %PORT% --theme devtool
)

if %ERRORLEVEL% NEQ 0 (
  color 0C
  echo.
  echo   The console exited with code %ERRORLEVEL%.
  echo   If python is missing, install it or set the python path in
  echo   Aloha's IPv7 settings - the DevTool reuses that launcher.
  echo.
)
pause
