@echo off
setlocal EnableExtensions
chcp 936 >nul
cd /d "%~dp0"

set "GUI=server\WxAdminGui\bin\Debug\net8.0-windows\WxAdminGui.exe"
set "CFG=server\WxAdmin\wxadmin.json"

echo.
echo  ==========================================
echo   大面聊天 - 图形管理端启动器
echo  ==========================================
echo.

rem 端口统一以 wxadmin.json 为准：管理端「修改端口」改过之后，
rem 这里打印的地址才不会和实际监听的端口对不上（此前硬编码 5000）
set "PORT=5000"
if exist "%CFG%" (
  for /f "tokens=2 delims=:, " %%p in ('findstr /i "Port" "%CFG%"') do set "PORT=%%p"
)
set /a PORTNUM=%PORT% >nul 2>&1
if errorlevel 1 set "PORT=5000"

if exist "%GUI%" goto :run

rem 图形管理端还没编译过（需要 .NET SDK）
echo  [准备] 图形管理端未编译，正在编译...
set "DOTNET=dotnet"
where dotnet >nul 2>&1
if errorlevel 1 if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
"%DOTNET%" build server\WxAdminGui -v q --nologo
if not exist "%GUI%" (
    echo  [失败] 编译失败，需要 .NET SDK
    echo         也可直接双击已编译好的 server\WxAdminGui\bin\Debug\net8.0-windows\WxAdminGui.exe
    goto :fail
)

:run
echo  [启动] 正在打开图形管理端...
start "" "%CD%\%GUI%"
echo.
echo   管理端功能：启动/停止/重启服务、端口设置、查看在线用户、
echo               安全设置、用户管理、日志/控制台
echo   关闭窗口 = 最小化到系统托盘后台常驻，托盘菜单可打开/退出
echo   网页:     http://0.0.0.0:%PORT%（监听所有网卡）
echo   本机 IP（局域网访问）:
for /f "tokens=2 delims=:" %%i in ('ipconfig ^| findstr /i "IPv4"') do for /f %%j in ("%%i") do echo     http://%%j:%PORT%
echo   端口来自 server\WxAdmin\wxadmin.json（管理端可改，改后重启服务生效）
echo   前端已由后端同源托管，无需单独启动 Node
echo.
exit /b 0

:fail
echo.
pause
exit /b 1
