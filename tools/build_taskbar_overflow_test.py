#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tools/build_taskbar_overflow_test.py
编译并运行 TaskbarOverflowTests 回归测试套件，
涵盖实证回放、拥挤浮动、拒绝降级、自动回栏、饱和处理与设置页面最小烟测。
严格遵循零黑框约束（creationflags=0x08000000）与非交互桌面隔离。
支持可选基线对比与 fresh clone 自动构建。
"""

import os
import sys
import subprocess
import io
import argparse
import hashlib

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8', errors='replace')

CREATE_NO_WINDOW = 0x08000000

def find_csc():
    candidates = [
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe"),
        r"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        r"C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe",
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c
    raise FileNotFoundError("未找到系统内置 csc.exe 编译器")

def run_cmd(cmd, cwd=None, env=None, check=True):
    print(f"[RUN] {' '.join(cmd) if isinstance(cmd, list) else cmd}")
    res = subprocess.run(
        cmd,
        cwd=cwd,
        env=env,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=CREATE_NO_WINDOW
    )
    if check and res.returncode != 0:
        print(f"[ERROR exit={res.returncode}]\nSTDOUT:\n{res.stdout}\nSTDERR:\n{res.stderr}")
        raise subprocess.CalledProcessError(res.returncode, cmd, res.stdout, res.stderr)
    return res

def build_windicator(root_dir, out_dir=None):
    if not out_dir:
        out_dir = os.path.join(root_dir, "out", "tests", "target")
    print(f"=== [构建] 从源码构建 WindicatorPlus 到隔离目录: {out_dir} ===")
    bat_path = os.path.join(root_dir, "build.bat")
    env = os.environ.copy()
    env["WINDICATOR_BUILD_OUT"] = out_dir
    res = run_cmd([bat_path], cwd=root_dir, env=env, check=True)
    if res.stdout:
        print(res.stdout.strip())
    exe_path = os.path.join(out_dir, "WindicatorPlus.exe")
    if not os.path.isfile(exe_path):
        raise FileNotFoundError(f"编译产物未找到: {exe_path}")
    print(f"[SUCCESS] 已生成目标程序: {exe_path}")
    return exe_path

def ensure_interop_dll(root_dir, csc_path, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    interop_dll = os.path.join(out_dir, "UIAutomationClientInterop.dll")
    if os.path.isfile(interop_dll):
        return interop_dll

    print("=== [生成] 正在生成 UIAutomationClientInterop.dll ===")
    gen_cs = os.path.join(root_dir, "tools", "GenerateUiaInterop.cs")
    gen_exe = os.path.join(out_dir, "GenerateUiaInterop.exe")
    cmd_gen = [
        csc_path,
        "-nologo",
        "-target:exe",
        f"-out:{gen_exe}",
        gen_cs
    ]
    run_cmd(cmd_gen, cwd=root_dir, check=True)
    run_cmd([gen_exe, interop_dll], cwd=root_dir, check=True)
    if not os.path.isfile(interop_dll):
        raise FileNotFoundError(f"未能生成 interop dll: {interop_dll}")
    print(f"[SUCCESS] 已生成: {interop_dll}")
    return interop_dll

def build_baseline_test_runner(root_dir, csc_path):
    print("=== 编译 TaskbarOverflowBaselineTests runner ===")
    out_dir = os.path.join(root_dir, "out", "tests")
    os.makedirs(out_dir, exist_ok=True)
    out_exe = os.path.join(out_dir, "TaskbarOverflowBaselineTests.exe")
    cs_file = os.path.join(root_dir, "tests", "TaskbarOverflowBaselineTests.cs")
    manifest = os.path.join(root_dir, "app.manifest")

    cmd = [
        csc_path,
        "-nologo",
        "-target:exe",
        "-optimize+",
        f"-win32manifest:{manifest}",
        "-r:System.dll",
        "-r:System.Drawing.dll",
        f"-out:{out_exe}",
        cs_file
    ]
    res = run_cmd(cmd, cwd=root_dir, check=True)
    if res.stdout:
        print(res.stdout.strip())
    print(f"[SUCCESS] 已生成基线测试 runner: {out_exe}")
    return out_exe

def build_test_runner(root_dir, csc_path, interop_dll):
    print("=== 编译 TaskbarOverflowTests 回归测试 runner ===")
    out_dir = os.path.join(root_dir, "out", "tests")
    os.makedirs(out_dir, exist_ok=True)
    out_exe = os.path.join(out_dir, "TaskbarOverflowTests.exe")
    cs_file = os.path.join(root_dir, "tests", "TaskbarOverflowTests.cs")
    manifest = os.path.join(root_dir, "app.manifest")

    source_files = [
        cs_file,
        os.path.join(root_dir, "NativeMethods.cs"),
        os.path.join(root_dir, "RuntimeTrace.cs"),
        os.path.join(root_dir, "DesktopWatcher.cs"),
        os.path.join(root_dir, "DesktopLoopSwitcher.cs"),
        os.path.join(root_dir, "ConfigManager.cs"),
        os.path.join(root_dir, "NativeTaskbarAutomation.cs"),
        os.path.join(root_dir, "TaskbarSpaceMonitor.cs"),
        os.path.join(root_dir, "WatermarkWindow.cs"),
        os.path.join(root_dir, "RenameDialog.cs"),
        os.path.join(root_dir, "ModernDarkControls.cs"),
        os.path.join(root_dir, "SettingsForm.cs"),
        os.path.join(root_dir, "TrayIconManager.cs")
    ]

    wpf_dirs = [
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF"),
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF"),
    ]
    lib_arg = "-lib:" + ",".join([d for d in wpf_dirs if os.path.isdir(d)])

    cmd = [
        csc_path,
        "-nologo",
        "-target:exe",
        "-optimize+",
        f"-win32manifest:{manifest}",
        f"-link:{interop_dll}",
        lib_arg,
        "-r:System.dll",
        "-r:System.Drawing.dll",
        "-r:System.Windows.Forms.dll",
        "-r:UIAutomationClient.dll",
        "-r:UIAutomationTypes.dll",
        "-r:WindowsBase.dll",
        f"-out:{out_exe}"
    ] + source_files

    res = run_cmd(cmd, cwd=root_dir, check=True)
    if res.stdout:
        print(res.stdout.strip())
    print(f"[SUCCESS] 已生成测试 runner: {out_exe}")
    return out_exe

def build_native_tests(root_dir, csc_path, interop_dll):
    print("=== 编译 NativeTaskbarAutomationTests runner ===")
    out_dir = os.path.join(root_dir, "out", "tests")
    os.makedirs(out_dir, exist_ok=True)
    out_exe = os.path.join(out_dir, "NativeTaskbarAutomationTests.exe")
    cs_file = os.path.join(root_dir, "tests", "NativeTaskbarAutomationTests.cs")
    manifest = os.path.join(root_dir, "app.manifest")

    source_files = [
        cs_file,
        os.path.join(root_dir, "NativeMethods.cs"),
        os.path.join(root_dir, "RuntimeTrace.cs"),
        os.path.join(root_dir, "NativeTaskbarAutomation.cs"),
        os.path.join(root_dir, "TaskbarSpaceMonitor.cs")
    ]

    wpf_dirs = [
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\WPF"),
        os.path.expandvars(r"%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\WPF"),
    ]
    lib_arg = "-lib:" + ",".join([d for d in wpf_dirs if os.path.isdir(d)])

    cmd = [
        csc_path,
        "-nologo",
        "-target:exe",
        "-optimize+",
        f"-win32manifest:{manifest}",
        f"-link:{interop_dll}",
        lib_arg,
        "-r:System.dll",
        "-r:System.Drawing.dll",
        "-r:System.Windows.Forms.dll",
        "-r:UIAutomationClient.dll",
        "-r:UIAutomationTypes.dll",
        "-r:WindowsBase.dll",
        f"-out:{out_exe}"
    ] + source_files

    res = run_cmd(cmd, cwd=root_dir, check=True)
    if res.stdout:
        print(res.stdout.strip())
    print(f"[SUCCESS] 已生成 NativeTaskbarAutomationTests runner: {out_exe}")
    return out_exe

def run_baseline_comparison(baseline_runner, old_exe, new_exe, root_dir):
    print("\n=== 运行 TaskbarOverflow 真实程序集基线比对 ===")

    if old_exe and os.path.isfile(old_exe):
        print(f"\n--- [可选基线] 运行真实旧版程序集基线红灯测试: {old_exe} ---")
        res_old = run_cmd([baseline_runner, "--target", old_exe], cwd=root_dir, check=False)
        print(res_old.stdout.strip())
        if res_old.stderr:
            print(res_old.stderr.strip())

        if res_old.returncode != 1:
            raise AssertionError(f"旧版基线缺陷断言失败！预期 exit=1（红灯），实际 exit={res_old.returncode}")
        if "[BASELINE DEFECT OBSERVED]" not in res_old.stdout:
            raise AssertionError("旧版输出未包含 [BASELINE DEFECT OBSERVED]，未检测到预期的饱和缺失缺陷！")
        print("[CONFIRMED RED] 旧版程序集确证基线红灯（退出码严格为 1，任务栏饱和被拒判为 Invalid）")
    else:
        print("[INFO] 未提供或未找到旧版程序集，跳过旧版缺陷红灯对比（可选基线模式）")

    # 验证新版真实程序集通过 (严格断言: returncode==0, 输出含有 BASELINE TEST PASSED)
    print(f"\n--- 运行真实新版候选程序集绿灯测试: {new_exe} ---")
    res_new = run_cmd([baseline_runner, "--target", new_exe], cwd=root_dir, check=False)
    print(res_new.stdout.strip())
    if res_new.stderr:
        print(res_new.stderr.strip())

    if res_new.returncode != 0:
        raise AssertionError(f"新版回归测试失败！预期 exit=0（绿灯），实际 exit={res_new.returncode}")
    if "[BASELINE TEST PASSED]" not in res_new.stdout:
        raise AssertionError("新版输出未包含 [BASELINE TEST PASSED]！")
    print("[CONFIRMED GREEN] 新版程序集确证饱和修复（退出码严格为 0，任务栏饱和合法封顶 1225）")

def run_tests(test_runner, root_dir):
    print("\n=== 运行 TaskbarOverflowTests 回归测试套件 (隔离桌面状态机) ===")
    res = run_cmd([test_runner], cwd=root_dir, check=True)
    print(res.stdout.strip())
    if res.stderr:
        print(res.stderr.strip())
    print(f"[PASSED] TaskbarOverflowTests 全部通过 (exit={res.returncode})")

def run_native_tests(native_runner, root_dir):
    print("\n=== 运行 NativeTaskbarAutomationTests 策略测试 ===")
    res = run_cmd([native_runner], cwd=root_dir, check=True)
    print(res.stdout.strip())
    if res.stderr:
        print(res.stderr.strip())
    print(f"[PASSED] NativeTaskbarAutomationTests 全部通过 (exit={res.returncode})")

def run_settings_smoke(root_dir, csc_path, target_exe):
    print("\n=== 运行 SettingsComposition 最小烟测 ===")
    out_dir = os.path.join(root_dir, "out", "tests")
    os.makedirs(out_dir, exist_ok=True)
    out_exe = os.path.join(out_dir, "SettingsCompositionTests.exe")
    cs_file = os.path.join(root_dir, "tests", "SettingsCompositionTests.cs")
    manifest = os.path.join(root_dir, "app.manifest")

    cmd = [
        csc_path,
        "-nologo",
        "-target:exe",
        "-optimize+",
        f"-win32manifest:{manifest}",
        "-r:System.dll",
        "-r:System.Drawing.dll",
        "-r:System.Windows.Forms.dll",
        f"-out:{out_exe}",
        cs_file
    ]
    res = run_cmd(cmd, cwd=root_dir, check=True)
    if res.stdout:
        print(res.stdout.strip())

    res_test = run_cmd([out_exe, "--target", target_exe], cwd=root_dir, check=True)
    print(res_test.stdout.strip())
    if res_test.stderr:
        print(res_test.stderr.strip())
    print(f"[PASSED] SettingsComposition 最小烟测全部通过 (exit={res_test.returncode})")

def main():
    parser = argparse.ArgumentParser(description="编译并运行 TaskbarOverflowTests 回归测试套件")
    parser.add_argument("--target", help="待测试目标 WindicatorPlus.exe 路径")
    parser.add_argument("--old-target", help="可选旧版基线 WindicatorPlus.exe 路径")
    args = parser.parse_args()

    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    print(f"工作根目录: {root_dir}")
    csc_path = find_csc()
    print(f"使用编译器: {csc_path}")

    # 1. 确定目标 exe 与旧版基线
    target_exe = args.target
    if not target_exe:
        candidate_root = os.path.join(root_dir, "WindicatorPlus.exe")
        candidate_v1315 = os.path.join(root_dir, "out", "v1.3.15", "WindicatorPlus.exe")
        if os.path.isfile(candidate_v1315):
            target_exe = candidate_v1315
        elif os.path.isfile(candidate_root):
            target_exe = candidate_root
        else:
            target_exe = build_windicator(root_dir)

    with open(target_exe, "rb") as f:
        target_sha = hashlib.sha256(f.read()).hexdigest()
    print(f"目标程序: {target_exe} (sha256={target_sha})")

    old_exe = args.old_target
    if not old_exe:
        candidate_old = os.path.join(root_dir, "out", "v1.3.14", "WindicatorPlus.exe")
        if os.path.isfile(candidate_old):
            old_exe = candidate_old

    # 2. 准备 interop dll
    interop_dll = ensure_interop_dll(root_dir, csc_path, os.path.join(root_dir, "out", "tests"))

    # 3. 编译测试 runners
    baseline_runner = build_baseline_test_runner(root_dir, csc_path)
    overflow_runner = build_test_runner(root_dir, csc_path, interop_dll)
    native_runner = build_native_tests(root_dir, csc_path, interop_dll)

    # 4. 运行真实程序集基线比对
    run_baseline_comparison(baseline_runner, old_exe, target_exe, root_dir)

    # 5. 运行 TaskbarOverflowTests 回归测试套件（隔离桌面状态机）
    run_tests(overflow_runner, root_dir)

    # 6. 运行 NativeTaskbarAutomationTests 策略测试
    run_native_tests(native_runner, root_dir)

    # 7. Settings 最小烟测
    run_settings_smoke(root_dir, csc_path, target_exe)

    print("\n[ALL FINISHED] 全部基线比对、回归测试与烟测执行通过！")

if __name__ == "__main__":
    main()
