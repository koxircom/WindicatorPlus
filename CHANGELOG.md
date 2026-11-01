# Changelog - Windicator+

All notable changes to this project will be documented in this file.
This project adheres to [Semantic Versioning](https://semver.org/) and is licensed under the [MIT License](LICENSE).

---

## [v1.3.15] - 2026-10-31

### 🚀 优化与新增 (Features & Improvements)
- **任务栏空间不足智能上浮 (8px Float)**：
  - 动态监测 Windows 11 任务栏可用空间；当应用图标增多导致任务栏拥挤、无法完整容纳指示器时，指示器自动上浮至任务栏上方 8px（`Bottom <= Taskbar.Top - 8`）浮动呈现，绝不进行截断或硬塞。
- **自动回栏恢复 (Taskbar Re-Docking)**：
  - 当关闭应用、任务栏空间恢复充裕后，指示器平滑自动恢复回任务栏原位停靠。
- **Windows 11 XAML 容器优先探测**：
  - 调整自动化探测策略，优先锁定 Windows 11 原生 XAML 任务栏容器（`TaskbarFrameRepeater` / `TaskbarFrame`），避免误命中 Windows 10 兼容列表（`MSTaskListWClass`）；
  - 保留 Windows 10 回退兼容机制（注：本次版本真实验收基于 Windows 11 平台）。
- **任务栏应用按钮饱和安全封顶**：
  - 修复应用按钮右边界顶到或跨过托盘左侧时误抛 `InvalidOperationException` 的缺陷，改为边界封顶 `trayLeft`，保证极限拥挤状态下指示器稳定进入浮动避让状态。

### 📦 发布与下载计划 (Release Assets)
- `WindicatorPlus-v1.3.15.exe`：单文件独立免安装可执行程序，双击即用；
- `WindicatorPlus-v1.3.15-Portable.zip`：便携式绿色压缩包，配置自动存储于 `%APPDATA%\WindicatorPlus\config.ini`。

---

## [v1.3.14] - 2026-10-31

### 🐛 修复与加固 (Fixes & Hardening)
- **设置窗口样式与合成加固**：
  - 移除设置窗口中的 `WS_EX_COMPOSITED`（`0x02000000`）扩展样式，彻底消除在 Windows 11 某些主题与缩放比例下可能引发的控件树重绘异常或视觉伪影；
  - 针对设置界面 6 个设置页进行独占显隐断言与卡片拓扑几何防重叠加固；
  - 修复窗口多次反复开启时的句柄泄露隐患，确保内存平滑释放与配置无副作用写入。

### 🧪 自动化测试套件
- 新增 `SettingsCompositionTests`：覆盖 WS_EX_COMPOSITED 样式过滤、设置页独占可见性、重开无泄漏断言；
- 新增 `TaskbarOverflowTests` 与 `TaskbarOverflowBaselineTests`：验证拥挤浮动避让、饱和封顶、自动回栏与真实程序集红绿比对。

---

## 📄 License
This project is licensed under the [MIT License](LICENSE).
