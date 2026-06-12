@echo off
chcp 65001 >nul
setlocal

set "DEBUG_EXE=E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\bin\Debug\net10.0-windows\CrystalRelayTwitchOsc.exe"

if not exist "%DEBUG_EXE%" (
    echo Debug executable not found:
    echo   %DEBUG_EXE%
    echo.
    echo Build the project in Debug configuration first:
    echo   dotnet build "E:\!!!Program to work on\Proper Crystal Relay\VrcTwitchOscBridge\VrcTwitchOscBridge.csproj"
    pause
    exit /b 1
)

echo Launching Crystal Relay DEBUG build...
echo   %DEBUG_EXE%
start "" "%DEBUG_EXE%"
endlocal
