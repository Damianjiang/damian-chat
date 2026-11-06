@echo off
chcp 936 >nul
cd /d "%~dp0"

set "CFG=server\WxAdmin\wxadmin.json"

rem 端口以 wxadmin.json 为准（管理端改过端口也停得干净）
set "PORT=5000"
if exist "%CFG%" (
  for /f "tokens=2 delims=:, " %%p in ('findstr /i "Port" "%CFG%"') do set "PORT=%%p"
)
set /a PORTNUM=%PORT% >nul 2>&1
if errorlevel 1 set "PORT=5000"

rem 先关管理端：管理端退出后不会再自动拉起后端
taskkill /f /im WxAdminGui.exe >nul 2>&1

rem 再按端口停后端：主端口 / 明文跳转口(+1) / 管理口(+2)
call :killport %PORT%
set /a P2=%PORT%+1
set /a P3=%PORT%+2
call :killport %P2%
call :killport %P3%

echo.
echo   已停止（管理端 + 端口 %PORT% / %P2% / %P3%）
echo.
timeout /t 2 >nul
exit /b

:killport
set "P=%~1"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /r /c:":%P% .*LISTENING"') do (
    taskkill /f /pid %%p >nul 2>&1
    if not errorlevel 1 echo  [:%P%] 已停止 PID %%p
)
exit /b
