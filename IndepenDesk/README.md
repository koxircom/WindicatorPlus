# IndepenDesk

**English** | [Türkçe](README.tr.md) | [Deutsch](README.de.md) | [Français](README.fr.md) | [Italiano](README.it.md) | [Русский](README.ru.md) | [中文](README.zh-CN.md) | [日本語](README.ja.md)

**Independent virtual desktops per monitor for Windows** — the Windows counterpart of macOS "Displays have separate Spaces".

## The problem

Windows virtual desktops are global: pressing `Win+Ctrl+←/→` switches **all monitors at once**. On macOS every display has its own Spaces and only the display under the cursor switches. IndepenDesk brings that behavior to Windows.

## Features

- 🖥 **Per-monitor desktops** — switching only affects the monitor under the mouse cursor; other monitors are untouched.
- ➕ **Dynamic, independent desktop counts** — every monitor starts with 1 desktop; swiping right at the end creates a new one (up to 9 per monitor). Empty trailing desktops are removed automatically. One monitor can have 5 desktops while another has 2.
- 🔢 **Global numbering** — desktop numbers continue across monitors (Monitor 1: 1-2-3, Monitor 2: 4-5-6). `Ctrl+Alt+digit` jumps to that desktop wherever it lives.
- 🎞 **macOS-style slide animation**, clipped inside the monitor.
- 🗂 **Overview screen** (`Ctrl+Alt+↑`) — Mission Control-like grid with drag & drop: move windows between desktops and monitors, move whole desktops to another monitor, right-click move menu.
- 🌍 **8 languages** — English, Türkçe, Deutsch, Français, Italiano, Русский, 中文, 日本語 (auto-detected, changeable from the tray menu).
- 🔄 **Update check** from the tray menu via GitHub Releases.
- 🚀 **Starts with Windows** by default — can be turned off anytime from the tray menu.
- 🛟 **Crash-safe** — hidden windows are journaled to disk and restored on the next start; everything is restored on exit and when a monitor is unplugged.

## Installation

Works on Windows 10 (1607+) and Windows 11 on **x64, x86 and ARM64**. All packages are self-contained — no .NET runtime required.

**winget:**

```
winget install harungecit.IndepenDesk
```

**Installer (recommended):** download `IndepenDesk-Setup-<version>-<arch>.exe` from [Releases](https://github.com/harungecit/IndepenDesk/releases) and run it — with optional desktop icon, in 7 setup languages.

**MSI** (corporate / GPO deployment): `IndepenDesk-<version>-<arch>.msi`.

**Portable:** `IndepenDesk-v<version>-win-<arch>.zip` — extract and run, nothing to install.

**MSIX:** signed with a self-signed certificate — first install `IndepenDesk.cer` into *Local Machine → Trusted People*, then double-click the `.msix`.

The app lives in the system tray (two blue screens icon).

## Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Alt+←` / `Ctrl+Alt+→` | Previous / next desktop on the monitor under the mouse (at the end, `→` creates a new one) |
| `Ctrl+Alt+↑` | Toggle the Overview |
| `Ctrl+Alt+1..9` | Jump to the desktop with that global number |
| `Ctrl+Alt+Shift+←/→` | Move the active window to the adjacent desktop and follow it |

An on-screen indicator ("Desktop 4 — Monitor 2 • 2/3") appears on every switch. The tray menu has **How to use…** with an animated gesture guide.

## Touchpad (macOS-like swipes)

By default the 4-finger swipe triggers Windows' **global** desktop switch. Override it:

1. Open **Settings → Bluetooth & devices → Touchpad → Advanced gestures**.
2. For the four-finger swipes pick **Custom shortcut** and record:
   - swipe left → `Ctrl+Alt+←`, swipe right → `Ctrl+Alt+→`, swipe up → `Ctrl+Alt+↑`
3. Recording a custom shortcut automatically replaces the Windows default — only the monitor under the cursor will switch.

## How it works

IndepenDesk does not use (and cannot fix) Windows' global virtual desktop system. Instead it keeps per-monitor window sets and, on a switch, hides/shows only the windows of that monitor (`ShowWindow`). Hidden windows also disappear from the taskbar and Alt-Tab, so it feels like a real desktop switch. New windows are adopted onto the active desktop of the monitor they appear on; windows dragged to another monitor follow it automatically.

## Known limitations

- Windows of elevated (admin) apps cannot be hidden unless IndepenDesk itself runs as admin.
- The native `Win+Ctrl+←/→` still triggers Windows' global switch — simply don't use it.
- `Ctrl+Alt+←/→` may clash with Intel graphics "rotate screen" hotkeys; disable those in the Intel graphics settings if needed (a tray notification tells you when registration fails).
- Windows 11's taskbar context menu cannot be extended by third-party apps; use the Overview's right-click menu instead.

## License

[MIT](LICENSE)
