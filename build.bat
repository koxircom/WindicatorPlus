@echo off
setlocal
chcp 65001 >nul

set "ROOT_DIR=%~dp0"
echo [Windicator+] 正在使用系统内置 .NET Framework 编译器构建 WindicatorPlus.exe...

set "CSC_PATH=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" (
    set "CSC_PATH=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "%CSC_PATH%" (
    echo [错误] 未找到系统 csc.exe 编译器: %CSC_PATH%
    exit /b 1
)

"%CSC_PATH%" -nologo -target:winexe -optimize+ -win32manifest:"%ROOT_DIR%app.manifest" -lib:"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF","%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF" -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:UIAutomationClient.dll -r:UIAutomationTypes.dll -r:WindowsBase.dll -out:"%ROOT_DIR%WindicatorPlus.exe" "%ROOT_DIR%NativeMethods.cs" "%ROOT_DIR%DesktopWatcher.cs" "%ROOT_DIR%DesktopLoopSwitcher.cs" "%ROOT_DIR%ConfigManager.cs" "%ROOT_DIR%TaskbarSpaceMonitor.cs" "%ROOT_DIR%WatermarkWindow.cs" "%ROOT_DIR%RenameDialog.cs" "%ROOT_DIR%ModernDarkControls.cs" "%ROOT_DIR%SettingsForm.cs" "%ROOT_DIR%TrayIconManager.cs" "%ROOT_DIR%TaskbarTintOverlay.cs" "%ROOT_DIR%Program.cs"

if %ERRORLEVEL% equ 0 (
    echo [成功] WindicatorPlus.exe 编译成功: %ROOT_DIR%WindicatorPlus.exe
    exit /b 0
) else (
    echo [失败] 编译出错，退出码: %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

