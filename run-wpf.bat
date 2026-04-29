@echo off
REM Launch the WPF build (Windows only).
REM Requires: .NET 10 SDK — https://dot.net
cd /d "%~dp0"
dotnet run --project DialogEditor.WPF\DialogEditor.WPF.csproj %*
