@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title Build NTFS Deleted Files Viewer

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo.
    echo ERROR: The built-in .NET Framework C# compiler was not found.
    echo Install or enable .NET Framework 4.8, then run this file again.
    echo.
    pause
    exit /b 1
)

echo Building NTFS Deleted Files Viewer...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 /codepage:65001 ^
 /out:"NTFS Deleted Files Viewer.exe" ^
 /reference:System.dll ^
 /reference:System.Core.dll ^
 /reference:System.Data.dll ^
 /reference:System.Drawing.dll ^
 /reference:System.Windows.Forms.dll ^
 "Program.cs"

if errorlevel 1 (
    echo.
    echo BUILD FAILED. Copy the compiler messages above when reporting a problem.
    echo.
    pause
    exit /b 1
)

echo Build complete. Starting the application...
start "" "NTFS Deleted Files Viewer.exe"
exit /b 0
