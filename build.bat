@echo off
setlocal
chcp 65001 >nul

set "ROOT_DIR=%~dp0"
if defined WINDICATOR_BUILD_OUT (
    set "OUT_DIR=%WINDICATOR_BUILD_OUT%"
) else (
    set "OUT_DIR=%ROOT_DIR%out\v1.3.12"
)
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo [Windicator+] 正在使用系统内置 .NET Framework 编译器构建 WindicatorPlus.exe (v1.3.12 候选构建)...

set "CSC_PATH=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC_PATH%" (
    set "CSC_PATH=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

if not exist "%CSC_PATH%" (
    echo [错误] 未找到系统 csc.exe 编译器: %CSC_PATH%
    exit /b 1
)

"%CSC_PATH%" -nologo -target:exe -out:"%OUT_DIR%\GenerateUiaInterop.exe" "%ROOT_DIR%tools\GenerateUiaInterop.cs"
if errorlevel 1 exit /b 1
"%OUT_DIR%\GenerateUiaInterop.exe" "%OUT_DIR%\UIAutomationClientInterop.dll"
if errorlevel 1 exit /b 1

"%CSC_PATH%" -nologo -link:"%OUT_DIR%\UIAutomationClientInterop.dll" -target:winexe -optimize+ -win32manifest:"%ROOT_DIR%app.manifest" -lib:"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF","%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF" -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:UIAutomationClient.dll -r:UIAutomationTypes.dll -r:WindowsBase.dll -out:"%OUT_DIR%\WindicatorPlus.exe" "%ROOT_DIR%NativeMethods.cs" "%ROOT_DIR%RuntimeTrace.cs" "%ROOT_DIR%DesktopWatcher.cs" "%ROOT_DIR%DesktopLoopSwitcher.cs" "%ROOT_DIR%ConfigManager.cs" "%ROOT_DIR%NativeTaskbarAutomation.cs" "%ROOT_DIR%TaskbarSpaceMonitor.cs" "%ROOT_DIR%WatermarkWindow.cs" "%ROOT_DIR%RenameDialog.cs" "%ROOT_DIR%ModernDarkControls.cs" "%ROOT_DIR%SettingsForm.cs" "%ROOT_DIR%TrayIconManager.cs" "%ROOT_DIR%Program.cs"

if %ERRORLEVEL% equ 0 (
    echo [成功] WindicatorPlus.exe 编译成功: %OUT_DIR%\WindicatorPlus.exe
    copy /y "%OUT_DIR%\WindicatorPlus.exe" "%OUT_DIR%\WindicatorPlus-v1.3.12.exe" >nul
    if errorlevel 1 (
        echo [失败] 复制 WindicatorPlus-v1.3.12.exe 出错
        exit /b 1
    )
    echo [成功] 已生成带版本可执行程序: %OUT_DIR%\WindicatorPlus-v1.3.12.exe
    exit /b 0
) else (
    echo [失败] 编译出错，退出码: %ERRORLEVEL%
    exit /b %ERRORLEVEL%
)

