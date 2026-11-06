@echo off
chcp 936 >nul
cd /d "%~dp0"

set "GUI=server\WxAdminGui\bin\Debug\net8.0-windows\WxAdminGui.exe"

if not exist "%GUI%" (
    echo 找不到管理端：
    echo   %CD%\%GUI%
    echo 请先编译 server\WxAdminGui
    pause
    exit /b 1
)

start "" "%GUI%"
exit
