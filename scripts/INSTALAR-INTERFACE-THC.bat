@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "VEYONSYNC_THC_SETUP=%~f0"
fltmc >nul 2>&1
if errorlevel 1 (
  powershell.exe -NoProfile -Command "try { Start-Process -FilePath $env:VEYONSYNC_THC_SETUP -Verb RunAs -ErrorAction Stop } catch { Write-Host $_.Exception.Message; exit 1 }"
  exit /b %ERRORLEVEL%
)
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0install-thc.ps1"
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" (
  echo.
  echo Falha ao instalar a interface THC. Copie a mensagem acima.
)
pause
exit /b %RESULT%
