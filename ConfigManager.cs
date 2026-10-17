using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WindicatorPlus
{
    public class AppConfig
    {
        public bool WatermarkEnabled { get; set; }
        public int WatermarkOpacity { get; set; } // 10 ~ 100
        public string WatermarkStyle { get; set; } // "Card", "Minimal", "Badge", "TextZh", "TextEn"
        public string WatermarkPosition { get; set; } // "BottomRight", "TopRight", "BottomLeft", "TopCenter", "Custom"
        public int CustomX { get; set; }
        public int CustomY { get; set; }
        public string DragModifierKey { get; set; } // "Control", "Alt", "Shift"
        public string TrayStyle { get; set; } // "Light", "Dark", "Blue"
        public bool AutoStart { get; set; }
        public bool DesktopLoopEnabled { get; set; }
        public string GitHubRepoUrl { get; set; }

        // Watermark Designer Properties
        public int WatermarkScale { get; set; } // 75 ~ 160 (Default 100%)
        public string TitleTemplate { get; set; } // e.g. "{name}" or "桌面 {n}"
        public string SubtitleTemplate { get; set; } // e.g. "虚拟桌面 {n} (共 {total} 个)"
        public bool ShowBigNumber { get; set; } // Default true
        public bool ShowBackgroundCard { get; set; } // Default true
        public string WatermarkColorTheme { get; set; } // "WhiteHighlight", "NeonPurple", "CyanGlow", "DarkSubtle"

        // Taskbar Tint Overlay Properties
        public bool TaskbarTintEnabled { get; set; }
        public int TaskbarTintOpacity { get; set; } // 5 ~ 40 (Default 18%)
        public Dictionary<int, string> TaskbarTintColors { get; set; }

        public Dictionary<int, string> DesktopNames { get; set; }

        private static readonly string[] DefaultTintPalette = new string[]
        {
            "#2563EB", // 1: Deep Blue (办公通讯)
            "#7C3AED", // 2: Cyber Violet (开发调试)
            "#059669", // 3: Aurora Green (看板)
            "#D97706", // 4: Amber Gold
            "#DC2626", // 5: Crimson Rose
            "#0891B2"  // 6: Cyan Teal
        };

        public AppConfig()
        {
            WatermarkEnabled = true;
            WatermarkOpacity = 40; // 默认 40% 显目高透
            WatermarkStyle = "Card";
            WatermarkPosition = "BottomRight";
            CustomX = -1;
            CustomY = -1;
            DragModifierKey = "Control";
            TrayStyle = "Light"; // 醒目大号白底黑字
            AutoStart = false;
            DesktopLoopEnabled = true;
            GitHubRepoUrl = "https://github.com/koxircom/WindicatorPlus";

            WatermarkScale = 100;
            TitleTemplate = "{name}";
            SubtitleTemplate = "虚拟桌面 {n} (共 {total} 个)";
            ShowBigNumber = true;
            ShowBackgroundCard = true;
            WatermarkColorTheme = "WhiteHighlight"; // 默认超高显目纯白

            TaskbarTintEnabled = true;
            TaskbarTintOpacity = 18; // 默认 18% 柔和微透
            TaskbarTintColors = new Dictionary<int, string>();

            DesktopNames = new Dictionary<int, string>();
        }

        public string GetTaskbarTintColor(int desktopIndex)
        {
            if (TaskbarTintColors != null && TaskbarTintColors.ContainsKey(desktopIndex))
            {
                return TaskbarTintColors[desktopIndex];
            }
            if (desktopIndex > 0 && desktopIndex <= DefaultTintPalette.Length)
            {
                return DefaultTintPalette[desktopIndex - 1];
            }
            int idx = Math.Max(0, (desktopIndex - 1) % DefaultTintPalette.Length);
            return DefaultTintPalette[idx];
        }

        public void SetTaskbarTintColor(int desktopIndex, string hexColor)
        {
            if (TaskbarTintColors == null) TaskbarTintColors = new Dictionary<int, string>();
            TaskbarTintColors[desktopIndex] = hexColor;
        }

        public string GetDesktopName(int desktopIndex)
        {
            if (DesktopNames != null && DesktopNames.ContainsKey(desktopIndex))
            {
                return DesktopNames[desktopIndex];
            }
            return null;
        }

        public void SetDesktopName(int desktopIndex, string name)
        {
            if (DesktopNames == null) DesktopNames = new Dictionary<int, string>();
            DesktopNames[desktopIndex] = name;
        }
    }

    public static class ConfigManager
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindicatorPlus");

        private static readonly string ConfigFilePath = Path.Combine(ConfigDir, "config.ini");
        private const string RUN_REG_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "WindicatorPlus";
        private const string OLD_APP_NAME = "Windicator";

        public static AppConfig Load()
        {
            AppConfig config = new AppConfig();

            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string[] lines = File.ReadAllLines(ConfigFilePath);
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                            continue;

                        int eqIdx = line.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            string key = line.Substring(0, eqIdx).Trim();
                            string val = line.Substring(eqIdx + 1).Trim();

                            if (key.Equals("WatermarkEnabled", StringComparison.OrdinalIgnoreCase))
                            {
                                bool b;
                                if (bool.TryParse(val, out b)) config.WatermarkEnabled = b;
                            }
                            else if (key.Equals("WatermarkOpacity", StringComparison.OrdinalIgnoreCase))
                            {
                                int o;
                                if (int.TryParse(val, out o)) config.WatermarkOpacity = Math.Max(5, Math.Min(100, o));
                            }
                            else if (key.Equals("WatermarkStyle", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val)) config.WatermarkStyle = val;
                            }
                            else if (key.Equals("WatermarkPosition", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val)) config.WatermarkPosition = val;
                            }
                            else if (key.Equals("CustomX", StringComparison.OrdinalIgnoreCase))
                            {
                                int x;
                                if (int.TryParse(val, out x)) config.CustomX = x;
                            }
                            else if (key.Equals("CustomY", StringComparison.OrdinalIgnoreCase))
                            {
                                int y;
                                if (int.TryParse(val, out y)) config.CustomY = y;
                            }
                            else if (key.Equals("DragModifierKey", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val)) config.DragModifierKey = val;
                            }
                            else if (key.Equals("TrayStyle", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val)) config.TrayStyle = val;
                            }
                            else if (key.Equals("DesktopLoopEnabled", StringComparison.OrdinalIgnoreCase))
                            {
                                bool b;
                                if (bool.TryParse(val, out b)) config.DesktopLoopEnabled = b;
                            }
                            else if (key.Equals("GitHubRepoUrl", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val))
                                {
                                    if (val.IndexOf("github.com/aaron/WindicatorPlus", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        config.GitHubRepoUrl = "https://github.com/koxircom/WindicatorPlus";
                                    }
                                    else
                                    {
                                        config.GitHubRepoUrl = val;
                                    }
                                }
                            }
                            else if (key.Equals("WatermarkScale", StringComparison.OrdinalIgnoreCase))
                            {
                                int sc;
                                if (int.TryParse(val, out sc)) config.WatermarkScale = Math.Max(70, Math.Min(180, sc));
                            }
                            else if (key.Equals("TitleTemplate", StringComparison.OrdinalIgnoreCase))
                            {
                                config.TitleTemplate = val;
                            }
                            else if (key.Equals("SubtitleTemplate", StringComparison.OrdinalIgnoreCase))
                            {
                                config.SubtitleTemplate = val;
                            }
                            else if (key.Equals("ShowBigNumber", StringComparison.OrdinalIgnoreCase))
                            {
                                bool b;
                                if (bool.TryParse(val, out b)) config.ShowBigNumber = b;
                            }
                            else if (key.Equals("ShowBackgroundCard", StringComparison.OrdinalIgnoreCase))
                            {
                                bool b;
                                if (bool.TryParse(val, out b)) config.ShowBackgroundCard = b;
                            }
                            else if (key.Equals("WatermarkColorTheme", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!string.IsNullOrEmpty(val)) config.WatermarkColorTheme = val;
                            }
                            else if (key.Equals("TaskbarTintEnabled", StringComparison.OrdinalIgnoreCase))
                            {
                                bool b;
                                if (bool.TryParse(val, out b)) config.TaskbarTintEnabled = b;
                            }
                            else if (key.Equals("TaskbarTintOpacity", StringComparison.OrdinalIgnoreCase))
                            {
                                int o;
                                if (int.TryParse(val, out o)) config.TaskbarTintOpacity = Math.Max(5, Math.Min(40, o));
                            }
                            else if (key.StartsWith("TaskbarTintColor_", StringComparison.OrdinalIgnoreCase))
                            {
                                string idxStr = key.Substring("TaskbarTintColor_".Length);
                                int dIdx;
                                if (int.TryParse(idxStr, out dIdx))
                                {
                                    config.SetTaskbarTintColor(dIdx, val);
                                }
                            }
                            else if (key.StartsWith("DesktopName_", StringComparison.OrdinalIgnoreCase))
                            {
                                string idxStr = key.Substring("DesktopName_".Length);
                                int dIdx;
                                if (int.TryParse(idxStr, out dIdx))
                                {
                                    config.SetDesktopName(dIdx, val);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error loading config: " + ex.Message);
            }

            config.AutoStart = IsAutoStartEnabled();

            return config;
        }

        public static void Save(AppConfig config)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                {
                    Directory.CreateDirectory(ConfigDir);
                }

                using (StreamWriter sw = new StreamWriter(ConfigFilePath, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("# Windicator+ Configuration");
                    sw.WriteLine(string.Format("WatermarkEnabled={0}", config.WatermarkEnabled));
                    sw.WriteLine(string.Format("WatermarkOpacity={0}", config.WatermarkOpacity));
                    sw.WriteLine(string.Format("WatermarkStyle={0}", config.WatermarkStyle));
                    sw.WriteLine(string.Format("WatermarkPosition={0}", config.WatermarkPosition));
                    sw.WriteLine(string.Format("CustomX={0}", config.CustomX));
                    sw.WriteLine(string.Format("CustomY={0}", config.CustomY));
                    sw.WriteLine(string.Format("DragModifierKey={0}", config.DragModifierKey));
                    sw.WriteLine(string.Format("TrayStyle={0}", config.TrayStyle));
                    sw.WriteLine(string.Format("DesktopLoopEnabled={0}", config.DesktopLoopEnabled));
                    sw.WriteLine(string.Format("GitHubRepoUrl={0}", config.GitHubRepoUrl));

                    sw.WriteLine(string.Format("WatermarkScale={0}", config.WatermarkScale));
                    sw.WriteLine(string.Format("TitleTemplate={0}", config.TitleTemplate));
                    sw.WriteLine(string.Format("SubtitleTemplate={0}", config.SubtitleTemplate));
                    sw.WriteLine(string.Format("ShowBigNumber={0}", config.ShowBigNumber));
                    sw.WriteLine(string.Format("ShowBackgroundCard={0}", config.ShowBackgroundCard));
                    sw.WriteLine(string.Format("WatermarkColorTheme={0}", config.WatermarkColorTheme));

                    sw.WriteLine(string.Format("TaskbarTintEnabled={0}", config.TaskbarTintEnabled));
                    sw.WriteLine(string.Format("TaskbarTintOpacity={0}", config.TaskbarTintOpacity));
                    if (config.TaskbarTintColors != null)
                    {
                        foreach (var kvp in config.TaskbarTintColors)
                        {
                            sw.WriteLine(string.Format("TaskbarTintColor_{0}={1}", kvp.Key, kvp.Value));
                        }
                    }

                    if (config.DesktopNames != null)
                    {
                        foreach (var kvp in config.DesktopNames)
                        {
                            sw.WriteLine(string.Format("DesktopName_{0}={1}", kvp.Key, kvp.Value));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error saving config: " + ex.Message);
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(APP_NAME);
                        if (val != null) return true;

                        object oldVal = key.GetValue(OLD_APP_NAME);
                        if (oldVal != null)
                        {
                            string oldStr = oldVal.ToString();
                            if (oldStr.IndexOf("WindicatorPlus", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_REG_KEY, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            string standardPath = @"D:\安装软件\WindicatorPlus.exe";
                            if (File.Exists(standardPath))
                            {
                                exePath = standardPath;
                            }

                            key.SetValue(APP_NAME, "\"" + exePath + "\"");

                            if (key.GetValue(OLD_APP_NAME) != null)
                            {
                                key.DeleteValue(OLD_APP_NAME, false);
                            }
                        }
                        else
                        {
                            if (key.GetValue(APP_NAME) != null)
                            {
                                key.DeleteValue(APP_NAME, false);
                            }
                            if (key.GetValue(OLD_APP_NAME) != null)
                            {
                                key.DeleteValue(OLD_APP_NAME, false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error setting auto start: " + ex.Message);
            }
        }
    }
}
