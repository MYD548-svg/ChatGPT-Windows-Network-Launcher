@echo off
setlocal
cd /d "%~dp0"
title Run Isolated Tests

echo ========================================================
echo        ChatGPT Launcher Isolated Automated Tests
echo ========================================================
echo.

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo [Error] csc.exe compiler not found!
    exit /b 1
)

set "REFS=System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll,Microsoft.CSharp.dll,System.Xml.dll,System.Runtime.Serialization.dll"
set "SRCS=UIControls.cs SettingsStore.cs ProxyService.cs TimezoneHelper.cs NetworkProbeService.cs ChatGPTLocator.cs EnvironmentTransaction.cs LauncherService.cs LauncherCore.cs"

if not exist "build_test" mkdir "build_test"

echo [1/3] Compiling and running IsolatedTests (Round 2 Defense Suite) ...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 /reference:%REFS% /out:build_test\IsolatedTests.exe build_test\IsolatedTests.cs %SRCS%
if %ERRORLEVEL% neq 0 (
    echo [Failure] Compilation of IsolatedTests failed!
    exit /b 1
)
"build_test\IsolatedTests.exe"
if %ERRORLEVEL% neq 0 (
    echo [Failure] IsolatedTests failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Compiling and running TestSuite (Component Suite) ...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 /reference:%REFS% /out:build_test\TestSuite.exe build_test\TestSuite.cs %SRCS%
if %ERRORLEVEL% neq 0 (
    echo [Failure] Compilation of TestSuite failed!
    exit /b 1
)
"build_test\TestSuite.exe"
if %ERRORLEVEL% neq 0 (
    echo [Failure] TestSuite failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Compiling and running TransactionTest (Sandbox Transaction) ...
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 /reference:%REFS% /out:build_test\TransactionTest.exe build_test\TransactionTest.cs %SRCS%
if %ERRORLEVEL% neq 0 (
    echo [Failure] Compilation of TransactionTest failed!
    exit /b 1
)
"build_test\TransactionTest.exe"
if %ERRORLEVEL% neq 0 (
    echo [Failure] TransactionTest failed!
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo [SUCCESS] All isolated test suites passed with 0 errors!
echo ========================================================
exit /b 0
