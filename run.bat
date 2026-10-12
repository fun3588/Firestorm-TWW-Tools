@echo off
chcp 65001 >nul
cd /d "%~dp0"
if exist "bin\Release\net8.0-windows\FirestormTwwMonitor.exe" (
    start "" "bin\Release\net8.0-windows\FirestormTwwMonitor.exe"
) else if exist "bin\Debug\net8.0-windows\FirestormTwwMonitor.exe" (
    start "" "bin\Debug\net8.0-windows\FirestormTwwMonitor.exe"
) else (
    echo [提示] 未找到编译产物，正在自动编译并启动...
    dotnet run -c Release
)
