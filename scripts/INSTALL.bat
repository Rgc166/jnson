@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
fltmc >nul 2>&1
if errorlevel 1 (
  echo Execute este arquivo como administrador.
  pause
  exit /b 1
)
set "VEYONSYNC_INSTALLER=%~dp0"
if exist "%~dp0install-teacher.ps1" (
  set "VEYONSYNC_LAB="
  set /p "VEYONSYNC_LAB=Laboratorio, por exemplo B: "
  powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -Command "$ErrorActionPreference='Stop'; try { & (Join-Path $env:VEYONSYNC_INSTALLER 'install-teacher.ps1') -Lab $env:VEYONSYNC_LAB; exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
) else (
  powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -Command "$ErrorActionPreference='Stop'; try { & (Join-Path $env:VEYONSYNC_INSTALLER 'install-student.ps1'); exit 0 } catch { Write-Host $_.Exception.Message; exit 1 }"
)
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" echo Instalacao falhou. Confira o erro acima. Consulte docs/troubleshooting.md.
pause
exit /b %RESULT%
