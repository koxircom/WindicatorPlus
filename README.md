# Windicator+ (Windows 11 虚拟桌面指示器增强版)

[![Platform: Windows 11](https://img.shields.io/badge/Platform-Windows%2011-0078D4?logo=windows11)](https://microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Build](https://img.shields.io/badge/.NET%20Framework-Builtin%20csc-brightgreen)](#)

轻量级、免安装的 Windows 11 虚拟桌面任务栏指示器与屏幕置顶悬浮水印增强工具。

本项目基于开源项目 **[Wielding/Windicator](https://github.com/Wielding/Windicator)** 进行演进与重构，保留了系统托盘数字指示与事件驱动监听机制，新增了**置顶鼠标完全穿透悬浮水印**、**修饰键拖拽定位**、**任务栏点击菜单**、**空间不足自动浮动退让**、**菜单 Z 序协调**、**多桌面专属配色**与**个性化桌面命名**等实用功能。

---

## 🌟 核心特性与行为说明

1. **托盘交互与快速设置**
   - **单击托盘图标（左键/右键）**：在任务栏上方精确弹出完整控制与设置菜单。
   - **双击托盘图标**：快速唤起并打开偏好设置中心（Settings）。

2. **屏幕悬浮指示器（Watermark Overlay）**
   - **日常完全鼠标穿透（Desktop Clickthrough）**：基于 Win32 `WS_EX_TRANSPARENT` 分层窗口技术，平时无论鼠标划过、点击或拖动底层应用，100% 穿透至底层，绝不影响日常办公与游戏。
   - **修饰键拖拽定位（Modifier Drag）**：日常鼠标无法误触；仅当**按住配置的修饰键（默认 `Ctrl`，可在设置中切换为 `Alt` 或 `Shift`）**时，鼠标左键方可拖动悬浮窗至屏幕任意位置，松开即刻持久化记忆新坐标并恢复完全穿透。

3. **任务栏停靠与空间自适应**
   - **任务栏单击唤起菜单（Taskbar Click 菜单）**：指示器停靠于任务栏时，直接单击指示器即可快速弹出控制菜单。
   - **空间不足自动浮动退让（Taskbar Space Monitor）**：后台自动化探查任务栏可用空间，当打开的应用图标过多或托盘区域挤压导致空间不足时，指示器自动退让切换为浮动状态，杜绝与系统任务栏控件发生重叠。
   - **菜单 Z 序 200ms 条件检查（Z-Order Reconciliation）**：针对 Windows 11 任务栏层级特性，系统内置 200ms 周期状态协调，维持 `[控制菜单] > [悬浮指示器] > [任务栏着色(Tint)] > [Shell任务栏]` 的严格层级，防止菜单弹出时被任务栏或自身图层遮挡。

4. **个性化外观定制**
   - **每桌面专属颜色（Per-Desktop Tint & Theme）**：支持为每个虚拟桌面单独指定任务栏强调着色与水印色彩，直观区分不同工作环境。
   - **虚拟桌面别名**：可为各个虚拟桌面指定名称（如“主工作区”、“开发调试”、“通讯”），并在水印与托盘悬停提示（Tooltip）中同步呈现。

---

## ⚙️ 真实承诺与技术边界

为保持透明与真实，本项目明确以下设计与技术边界：

- **非零 CPU 承诺（低开销但非 0%）**：软件以注册表通知为基础实现事件驱动的桌面切换感知，空闲状态开销极低；但由于存在定时器层级同步、界面重绘与空间检测逻辑，运行时存在微量 CPU 消耗，承诺低开销，不虚构“绝对 0.00% CPU”或“秒开神话”。
- **Primary-only Tint（仅限主屏幕着色）**：任务栏强调色叠加（Taskbar Tint Overlay）目前仅在主显示器任务栏生效；多显示器副屏仅展示常规指示器与托盘。
- **不保留 Shell 空间（No Shell AppBar Reservation）**：本程序采用顶层分层窗口技术贴合在任务栏上方，不向 Windows 系统申请抢占任务栏物理保留区（AppBar），不会改变系统任务栏原生控件排布。
- **截图工具不保证零瞬时遮挡**：置顶半透明水印作为顶层分层窗口渲染，在系统全屏截图、区域截屏或部分第三方截图工具工作时，可能会被捕获进截图中，不承诺绝对的零瞬时遮挡。
- **配置与自启动机制**：
  - **配置文件路径**：所有偏好设置与桌面别名均持久化存储于 `%APPDATA%\WindicatorPlus\config.ini`，绿色便携，不污染注册表杂项目录。
  - **开机自启动**：属于完全可选功能，仅在用户勾选时写入当前用户启动项 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，取消勾选或退出时可随时清理。

---

## 🚀 获取与运行

### 1. 下载程序
- **预留发布入口**：[GitHub Releases 最新版本](https://github.com/koxircom/WindicatorPlus/releases/latest)  
  *注：本项目尚未正式创建线上 GitHub Release，上述链接为预留入口。*
- **直接获取**：用户可直接下载本公开仓库根目录下的可执行文件 **`WindicatorPlus.exe`** 运行使用。

### 2. 运行环境要求
- **操作系统**：Windows 11（64 位推荐）
- **依赖环境**：Windows 自带的 .NET Framework 4.0 及以上运行库（系统原生内置，免安装任何第三方运行时或运行库）。

---

## 🛠️ 本地编译（零外部依赖）

本项目无需安装 Visual Studio 或 .NET SDK，利用 Windows 11 自带的 C# 编译器（`csc.exe`）即可完成构建：

1. 双击或在任意终端目录下运行项目中的 `build.bat`；
2. 脚本将自动调用系统目录下的 `csc.exe`，基于显式源文件列表完成编译，并在项目根目录生成单文件 `WindicatorPlus.exe`；
3. 全过程无需联网、无任何安装步骤，编译失败将返回非零退出码。

---

## 📄 开源协议与致谢

- 本项目采用 **[MIT License](LICENSE)** 开源协议。
- 衷心致谢上游开源项目 **[Wielding/Windicator](https://github.com/Wielding/Windicator)** 及其原作者 Andrew 提供的优秀架构设计与启发。
