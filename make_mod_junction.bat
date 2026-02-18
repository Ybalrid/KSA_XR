@echo off
setlocal EnableExtensions

REM --- Where the junction should be (link path)
set "LINK_PATH=C:\Program Files\Kitten Space Agency\Content\KSA_XR"

REM --- Compute target from this script's directory
REM %~dp0 ends with a backslash
set "BASE_DIR=%~dp0"
set "TARGET_PATH=%BASE_DIR%bin\x64\Debug\net10.0-windows"

REM --- Normalize: remove trailing backslash from BASE_DIR if you care (not required)
REM (TARGET_PATH is fine as-is)

REM --- Check admin; if not admin, relaunch elevated
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Requesting administrator privileges...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo Script dir  : "%BASE_DIR%"
echo Target path : "%TARGET_PATH%"
echo Link path   : "%LINK_PATH%"
echo.

REM --- Sanity check: does target exist?
if not exist "%TARGET_PATH%" (
    echo ERROR: Target folder does not exist:
    echo        "%TARGET_PATH%"
    exit /b 2
)

REM --- If link path already exists, remove it (junction or directory)
if exist "%LINK_PATH%" (
    echo Removing existing path: "%LINK_PATH%"
    rmdir "%LINK_PATH%" 2>nul
    if exist "%LINK_PATH%" (
        echo ERROR: Failed to remove existing "%LINK_PATH%".
        echo        Close anything using it, and make sure you have permissions.
        exit /b 3
    )
)

REM --- Create the junction
echo Creating junction...
mklink /j "%LINK_PATH%" "%TARGET_PATH%"
if not "%errorlevel%"=="0" (
    echo ERROR: mklink failed with errorlevel %errorlevel%.
    exit /b 4
)

echo.
echo Done.
exit /b 0
