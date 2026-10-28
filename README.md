# Windicator+ (虚拟桌面任务栏自发光指示器)

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%20Windows%2010-informational.svg)](README.md)
[![Release](https://img.shields.io/badge/Release-v1.3.12-success.svg)](https://github.com/koxircom/WindicatorPlus/releases)

**Windicator+** 是专为 Windows 11 / 10 深度优化的原生 C#/.NET 虚拟桌面任务栏吸附式指示器。

采用自发光文字渲染引擎（Luminous Glow Engine），无缝停靠于系统右下角托盘箭头旁，提供桌面别名管理、桌面专属霓虹色彩、平滑桌面循环切换，100% 鼠标完全穿透（Zero Clickthrough Loss），后台 0 占用纯静默运行。

---

## ✨ 核心特性

- 🌟 **双层漫反射霓虹自发光 (Luminous Glow Engine)**：
  - 彻底摒弃传统硬描边，采用双层平滑漫反射外发光与自适应高亮字芯；
  - 在 Windows 11 暗黑任务栏上呈现透亮通透的霓虹自发光质感；
  - 智能自适应暗曜石冷白光晕、白金银霜微光与高饱和彩色自发光；
- 🎨 **虚拟桌面专属色彩体系**：
  - 支持为每个虚拟桌面单独分配专属霓虹颜色（深海蓝、赛博紫、极光绿、暖琥珀等）；
  - 切换桌面时水印编号、名称及分隔线实时秒级平滑变色；
- 🚀 **任务栏智能吸附与避让 (Taskbar Smart Avoidance)**：
  - 基于 Windows 11 原生 COM `CUIAutomation8` 批量缓存读取与强时间预算保护；
  - 严格确保永不遮挡任何系统托盘图标、折叠箭头或打开的应用按钮；
  - 针对短名称（如 "Ai"）智能右对齐吸附至托盘左侧，杜绝任何多余空隙；
- 🛡️ **100% 鼠标击穿与静默无感 (Zero-Impact Invariant)**：
  - 依托 Win32 `WS_EX_TRANSPARENT` 分层窗口技术，鼠标点击 100% 完全穿透到达任务栏；
  - 无任何控制台黑框弹窗，完全无感静默驻留；
  - 独创 `Menu > Watermark > Shell` 原子层级协调器，杜绝闪烁或被任务栏遮挡；
- ⌨️ **便捷桌面管理与快捷操作**：
  - 按住修饰键（默认 Ctrl）可随时将指示器拖拽至屏幕任意位置；
  - 单击托盘图标弹出右键菜单，双击直接唤起现代深色设置中心；
  - 支持虚拟桌面循环切换（首尾桌面无缝滚轮/快捷键跳转）。

---

## 🛠️ 构建与编译

本项目采用纯原生 Windows .NET Framework 4.0+ 与系统内置编译器，无需安装任何重型 SDK 或额外运行库。

双击或在终端运行：
```cmd
build.bat
```
脚本将自动调用系统编译器与内置 UIA 导出器，生成独立的单文件可执行程序 `WindicatorPlus.exe`。

---

## 📦 快速下载

前往 [GitHub Releases](https://github.com/koxircom/WindicatorPlus/releases/tag/v1.3.12) 下载预编译的便携式绿色压缩包：
- `WindicatorPlus-v1.3.12-Portable.zip`：解压即用，配置自动保存在 `%APPDATA%\WindicatorPlus\config.ini`。

---

## 📄 开源许可

本项目遵循 [MIT License](LICENSE) 许可协议。
