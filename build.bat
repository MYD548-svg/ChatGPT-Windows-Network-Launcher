@echo off
setlocal
cd /d "%~dp0"
title Build ChatGPT Launcher

echo ========================================================
echo        Building ChatGPT Windows Launcher (EXE)
echo ========================================================
echo.

set "BUILD_ONLY=0"
if /i "%~1"=="/build-only" set "BUILD_ONLY=1"
if /i "%~1"=="--build-only" set "BUILD_ONLY=1"
if /i "%~1"=="-b" set "BUILD_ONLY=1"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [Error] C# compiler csc.exe was not found in your system!
    echo Please ensure .NET Framework 4.5 or higher is installed.
    pause
    exit /b 1
)

echo Compiler: %CSC%
echo Compiling sources into ChatGPTAntiBanLauncher.exe ...
echo.

set "REFS=System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll,Microsoft.CSharp.dll,System.Xml.dll,System.Runtime.Serialization.dll"
set "SRCS=Program.cs LauncherCore.cs UIControls.cs SettingsStore.cs ProxyService.cs TimezoneHelper.cs NetworkProbeService.cs ChatGPTLocator.cs EnvironmentTransaction.cs LauncherService.cs"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /reference:%REFS% /out:ChatGPTAntiBanLauncher.exe %SRCS%

if %ERRORLEVEL% neq 0 goto :failed

echo.
echo ========================================================
echo [Success] Compilation completed successfully!
echo Output: %~dp0ChatGPTAntiBanLauncher.exe
echo ========================================================
echo.

if "%BUILD_ONLY%"=="1" (
    echo Build only specified. Exiting without launching.
    exit /b 0
)

echo Launching application...
start "" "%~dp0ChatGPTAntiBanLauncher.exe"
exit /b 0

:failed
echo.
echo ========================================================
echo [Failure] Compilation failed. Please see error details above.
echo ========================================================
if not "%BUILD_ONLY%"=="1" pause
exit /b %ERRORLEVEL%
