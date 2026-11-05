@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [ERROR] .NET Framework 4.x C# compiler not found.
  exit /b 1
)

if not exist "dist" mkdir "dist"

"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe ^
  /out:"dist\AStockTicker.exe" ^
  /win32icon:"assets\app.ico" ^
  /win32manifest:"src\app.manifest" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  "src\DebugLog.cs" "src\Config.cs" "src\MarketClock.cs" "src\QuoteService.cs" ^
  "src\TrayIconFactory.cs" "src\StartupManager.cs" ^
  "src\SettingsForm.cs" "src\TickerForm.cs" "src\Program.cs" "src\AssemblyInfo.cs"

if errorlevel 1 (
  echo [ERROR] build failed.
  exit /b 1
)

if not exist "dist\config.ini" copy /y "config.default.ini" "dist\config.ini" >nul
if not exist "dist\holidays.txt" copy /y "holidays.txt" "dist\holidays.txt" >nul
if exist "README.md" copy /y "README.md" "dist\README.md" >nul
if not exist "dist\docs" mkdir "dist\docs"
if exist "docs\taskbar-preview.png" copy /y "docs\taskbar-preview.png" "dist\docs\taskbar-preview.png" >nul
if exist "docs\settings.png" copy /y "docs\settings.png" "dist\docs\settings.png" >nul

echo.
echo Build OK: dist\AStockTicker.exe
endlocal
