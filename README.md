# Windicator+ & IndepenDesk (v1.4.0-preview.1 Source Package)

This source archive contains the complete build sources for **Windicator+ 1.4.0-preview.1** and the integrated **IndepenDesk 0.4.0-windicator.1** bridge engine.

## Directory Structure

- `WindicatorPlus/`: The companion client for per-monitor independent desktop badge indication.
  - 16 core C# production source files
  - `app.manifest`: Application execution manifest
  - `build.bat`: One-click zero-dependency compilation script using system .NET Framework `csc.exe`
  - `LICENSE`: MIT License
- `IndepenDesk/`: The per-monitor independent virtual desktop window manager engine.
  - `IndepenDesk.csproj`: .NET 8 Windows Forms project file
  - `src/`: Complete C# source files including `IndicatorStateServer.cs` (read-only state bridge)
  - `packaging/app.ico`: Application icon resource
  - `LICENSE`: MIT License
- `STATE-PROTOCOL.md`: Protocol specification for the local named-pipe state snapshot stream.

## Building from Source

### 1. Building Windicator+ (Companion Client)
Prerequisites: Windows 10/11 with .NET Framework 4.8 (pre-installed by default).
1. Open a command prompt in `WindicatorPlus/`.
2. Run `build.bat`.
3. The executable `WindicatorPlus.exe` will be generated in the same directory.

### 2. Building IndepenDesk (Bridge Engine)
Prerequisites: .NET 8.0 SDK.
1. Open a terminal in `IndepenDesk/`.
2. Run `dotnet build -c Release`.
3. The standalone executable will be generated under `IndepenDesk/bin/Release/net8.0-windows/`.

## Runtime Mode

- Launch Engine: `IndepenDesk.exe --windicator-bridge`
- Launch Companion: `WindicatorPlus.exe --independesk`

## License

Both projects are released under the MIT License.
