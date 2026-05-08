@echo off
setlocal
cd /d "%~dp0"
echo == SteamPlaylogViewer ==
dotnet restore SteamPlaylogViewer.csproj || goto :err
dotnet run --project SteamPlaylogViewer.csproj
exit /b 0
:err
echo build failed
pause
exit /b 1
