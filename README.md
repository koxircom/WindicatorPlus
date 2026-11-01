# Windicator+ (虚拟桌面任务栏自发光指示器)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%20Windows%2010-informational.svg)](README.md)
[![Release](https://img.shields.io/badge/Release-v1.3.15-success.svg)](https://github.com/koxircom/WindicatorPlus/releases/tag/v1.3.15)

**Windicator+** 是专为 Windows 11 深度优化的原生 C#/.NET 虚拟桌面任务栏吸附式指示器（同时保留 Windows 10 回退兼容机制）。

采用自发光文字渲染引擎（Luminous Glow Engine），平滑停靠于系统右下角托盘左侧，提供桌面别名管理、桌面专属霓虹色彩、平滑桌面循环切换、鼠标穿透与常态低开销静默后台运行。

---

## ✨ 核心特性与设计

- 🌟 **双层漫反射霓虹自发光 (Luminous Glow Engine)**：
  - 采用双层平滑漫反射外发光与自适应高亮字芯；
  - 在 Windows 11 暗黑任务栏上呈现透亮通透的霓虹自发光质感；
  - 智能自适应冷白光晕、白金微光与彩色自发光；
- 🎨 **虚拟桌面专属色彩体系**：
  - 支持为每个虚拟桌面单独分配专属霓虹颜色（深海蓝、赛博紫、极光绿、暖琥珀等）；
  - 切换桌面时水印编号、名称及分隔线实时秒级平滑变色；
- 🚀 **任务栏智能吸附与空间退让 (Taskbar Smart Avoidance & Float)**：
  - 基于 Windows 11 原生 COM `CUIAutomation8` 批量缓存读取与时间预算保护；
  - 优先锁定 Windows 11 XAML 任务栏容器，防止兼容列表误探测；
  - **空间不足上浮避让**：当任务栏图标增多导致可用空间不足以完整容纳指示器时，指示器自动上浮至任务栏上方 8px 处浮动，拒绝难看截断或强行挤压；
  - **自动回栏**：当任务栏空间恢复宽裕后，自动恢复停靠回任务栏原位；
- 🛡️ **鼠标穿透与低感静默 (Zero-Impact Invariant)**：
  - 依托 Win32 `WS_EX_TRANSPARENT` 分层窗口技术，鼠标点击完全穿透到达任务栏；
  - 无控制台窗口弹出，纯静默后台驻留；
  - 协调 `Menu > Watermark > Shell` 层级，优化层级关系并减少异常遮挡与闪烁；
- ⌨️ **便捷桌面管理与快捷操作**：
  - 按住修饰键（默认 Ctrl）可随时将指示器拖拽至屏幕任意位置；
  - 单击托盘图标弹出右键菜单，双击直接唤起现代深色设置中心；
  - 支持虚拟桌面循环切换（首尾桌面无缝滚轮/快捷键跳转）。

---

## 🔍 技术边界与客观声明

1. **客观开销**：软件主要基于系统注册表通知与系统事件监听，常态空闲低开销，但包含必要的界面重绘、层级协调与空间监测逻辑，运行时存在合理微量 CPU 消耗，不作“绝对 0% CPU”等不实承诺。
2. **遮挡与层级边界**：置顶悬浮指示器属于 Win32 分层窗口，在第三方全屏独占游戏、特定顶层窗口或截屏工具下可能存在层级重叠，已内置防遮挡与避让机制但无法保证在任意第三方场景下“永不遮挡”。
3. **平台支持**：架构保留了 Windows 10 回退兼容代码路径，但本次 v1.3.15 版本的全套自动化回归测试与端到端实机验收仅在当前 Windows 11 平台上严格执行。

---

## 🛠️ 构建与编译

本项目采用纯原生 Windows .NET Framework 4.0+ 与系统内置编译器，无需安装任何重型 SDK。

在终端中运行：
```cmd
build.bat
```
脚本将自动调用系统编译器与内置 UIA 导出器，生成独立的单文件可执行程序 `WindicatorPlus.exe`。

---

## 📦 快速下载

前往 [GitHub Releases (v1.3.15)](https://github.com/koxircom/WindicatorPlus/releases/tag/v1.3.15) 下载预编译文件：
- `WindicatorPlus-v1.3.15.exe`：单文件免安装，双击即用；
- `WindicatorPlus-v1.3.15-Portable.zip`：便携式绿色压缩包，配置自动保存在 `%APPDATA%\WindicatorPlus\config.ini`。

---

## 📄 开源许可

本项目遵循 [MIT License](LICENSE) 许可协议。
