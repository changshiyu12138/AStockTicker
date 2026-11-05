@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "build" mkdir "build"
"%CSC%" /nologo /codepage:65001 /optimize+ /target:exe ^
  /out:"build\AStockTickerTest.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "src\Config.cs" "src\MarketClock.cs" "src\QuoteService.cs" ^
  "src\TestMain.cs" "src\AssemblyInfo.cs"
if errorlevel 1 exit /b 1
echo Build OK: build\AStockTickerTest.exe
endlocal
