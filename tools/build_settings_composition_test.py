#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tools/build_settings_composition_test.py
编译 WindicatorPlus (v1.3.15) 并编译运行 SettingsCompositionTests 回归测试套件。
严格遵循零黑框约束（creationflags=0x08000000）。
支持可选旧版基线对比与 fresh clone 自动构建。
"""

import os
import sys
import subprocess
import io
import argparse

# 强制 UTF-8 标准输出
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
    print(f"=== [1/3] 从源码构建 WindicatorPlus 到隔离目录: {out_dir} ===")
    bat_path = os.path.join(root_dir, "build.bat")
    env = os.environ.copy()
    env["WINDICATOR_BUILD_OUT"] = out_dir
    res = run_cmd([bat_path], cwd=root_dir, env=env, check=True)
    if res.stdout:
        print(res.stdout.strip())
    exe_path = os.path.join(out_dir, "WindicatorPlus.exe")
    if not os.path.isfile(exe_path):
        raise FileNotFoundError(f"编译产物未找到: {exe_path}")
    print(f"[SUCCESS] 已生成测试目标程序: {exe_path}")
    return exe_path

def build_test_runner(root_dir, csc_path):
    print("=== [2/3] 编译 SettingsCompositionTests 回归测试 runner ===")
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
    print(f"[SUCCESS] 已生成测试 runner: {out_exe}")
    return out_exe

def run_tests(test_runner, root_dir, target_exe, old_target=None):
    print("=== [3/3] 运行 SettingsCompositionTests 样式与隔离回归测试 ===")

    # 1. 验证旧版基线 (若存在则运行基线对比，预期包含 WS_EX_COMPOSITED 0x02000000 导致断言失败)
    if not old_target:
        candidate_old = os.path.join(root_dir, "WindicatorPlus-v1.3.13.exe")
        if os.path.isfile(candidate_old):
            old_target = candidate_old

    if old_target and os.path.isfile(old_target):
        print(f"\n--- 测试 A: 针对旧版基线运行 (预期断言失败: WS_EX_COMPOSITED 存在) ---")
        res_old = run_cmd([test_runner, "--target", old_target], cwd=root_dir, check=False)
        print(res_old.stdout.strip())
        if res_old.stderr:
            print(res_old.stderr.strip())
        if res_old.returncode != 0 and ("WS_EX_COMPOSITED" in res_old.stdout or "WS_EX_COMPOSITED" in res_old.stderr):
            print(f"[PASSED BASELINE] 确认旧版基线确实包含 WS_EX_COMPOSITED 导致断言失败 (exit={res_old.returncode})")
        else:
            raise RuntimeError(f"旧版基线测试未能按预期检测到 WS_EX_COMPOSITED 失败 (exit={res_old.returncode})")
    else:
        print(f"[INFO] 未提供或未找到旧版基线程序，跳过基线缺陷对比（可选基线模式）")

    # 2. 验证目标版本程序通过 (无 WS_EX_COMPOSITED，6页独占，卡片无重叠，重开不泄露，无持久化副作用)
    print(f"\n--- 测试 B: 针对目标版本运行: {target_exe} (预期全部通过) ---")
    res_new = run_cmd([test_runner, "--target", target_exe], cwd=root_dir, check=True)
    print(res_new.stdout.strip())
    if res_new.stderr:
        print(res_new.stderr.strip())
    print(f"[PASSED] 目标版本全部测试通过 (exit={res_new.returncode})")

def main():
    parser = argparse.ArgumentParser(description="编译并运行 SettingsCompositionTests 回归测试套件")
    parser.add_argument("--target", help="待测试目标 WindicatorPlus.exe 路径")
    parser.add_argument("--old-target", help="可选旧版基线 WindicatorPlus.exe 路径")
    args = parser.parse_args()

    root_dir = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    print(f"工作根目录: {root_dir}")
    csc_path = find_csc()
    print(f"使用编译器: {csc_path}")

    # 1. 确定目标 exe
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

    print(f"测试目标可执行文件: {target_exe}")

    # 2. 编译测试 runner
    test_runner = build_test_runner(root_dir, csc_path)

    # 3. 运行测试套件
    run_tests(test_runner, root_dir, target_exe, old_target=args.old_target)

    print("\n[ALL FINISHED] SettingsCompositionTests 编译与测试全部完成！")

if __name__ == "__main__":
    main()
