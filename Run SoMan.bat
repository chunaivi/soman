@echo off
cd /d "%~dp0src\SoMan"
echo Building SoMan...
dotnet build -c Release --nologo -v q
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)
echo Starting SoMan...
start "" "%~dp0src\SoMan\bin\Release\net8.0-windows\SoMan.exe"
