@echo off
chcp 65001 >nul
echo [Firestorm-TWW-Tools] 正在编译发布版本...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo [错误] 编译失败，请检查上方日志。
    pause
    exit /b %errorlevel%
)
echo [成功] 编译完成！可执行文件位于: bin\Release\net8.0-windows\FirestormTwwMonitor.exe
pause
