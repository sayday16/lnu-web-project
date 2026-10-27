@echo off
setlocal
set "ROOT=%~dp0"

echo Building compute node...
dotnet build "%ROOT%backend\LNU_ComputeNode\LNU_ComputeNode.csproj" -nologo -v q
if errorlevel 1 (
    echo Build failed.
    exit /b 1
)

start "node-1" /D "%ROOT%backend\LNU_ComputeNode" cmd /k dotnet run --no-build --no-launch-profile --urls http://localhost:6001 --Node:Id=node-1
start "node-2" /D "%ROOT%backend\LNU_ComputeNode" cmd /k dotnet run --no-build --no-launch-profile --urls http://localhost:6002 --Node:Id=node-2

echo.
echo Compute nodes started:
echo   node-1: http://localhost:6001/status
echo   node-2: http://localhost:6002/status
