@echo off
setlocal
cd /d "%~dp0"
if not exist "NTFS Deleted Files Viewer.exe" (
    call "Build and Run.cmd"
    exit /b %errorlevel%
)
start "" "NTFS Deleted Files Viewer.exe"
