@echo off
setlocal EnableExtensions
chcp 936 >nul
cd /d "%~dp0"

set "CFG=server\WxAdmin\wxadmin.json"

echo.
echo   大面聊天 - 停止服务
echo.

rem 端口统一以 wxadmin.json 为准：管理端改过端口后，
rem 这里依然能停掉真正在跑的那个服务（此前硬编码 5000，改端口后就停不干净，
rem 下次启动会因为端口仍被占用而失败）
set "PORT=5000"
if exist "%CFG%" (
  for /f "tokens=2 delims=:, " %%p in ('findstr /i "Port" "%CFG%"') do set "PORT=%%p"
)
set /a PORTNUM=%PORT% >nul 2>&1
if errorlevel 1 set "PORT=5000"

call :killport %PORT%
call :killport 5173

echo.
echo   已全部停止
echo.
pause
endlocal
exit /b

:killport
set "P=%~1"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /r /c:":%P% .*LISTENING"') do (
    taskkill /f /pid %%p >nul 2>&1
    if not errorlevel 1 echo  [:%P%] 已停止 PID %%p
)
exit /b
