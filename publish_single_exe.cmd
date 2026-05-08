@echo off
setlocal
cd /d "%~dp0"
echo == SteamPlaylogViewer (publish single EXE) ==
dotnet restore SteamPlaylogViewer.csproj || goto :err
dotnet publish SteamPlaylogViewer.csproj -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true ^
  /p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish
if errorlevel 1 goto :err
echo Output: %CD%\publish\SteamPlaylogViewer.exe
pause
exit /b 0
:err
echo publish failed
pause
exit /b 1
