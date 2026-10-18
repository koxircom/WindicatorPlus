using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Xml;

namespace WindicatorPlus
{
    public class DesktopDisplaySetting
    {
        public string Device { get; set; }
        public int LocalIndex { get; set; }
        public string CustomName { get; set; }
        public string AccentColor { get; set; }

        public DesktopDisplaySetting()
        {
            Device = string.Empty;
            LocalIndex = 0;
            CustomName = string.Empty;
            AccentColor = string.Empty;
        }
    }

    /// <summary>
    /// 管理按屏幕与本地桌面序号的独立命名与颜色设置。
    /// 配置文件保存于 %APPDATA%/WindicatorPlus/independent-desktops.xml，
    /// 绝不读写系统注册表或现有的 config.ini。
    /// </summary>
    public class IndependentDesktopSettings
    {
        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly Dictionary<string, DesktopDisplaySetting> _items = new Dictionary<string, DesktopDisplaySetting>(StringComparer.OrdinalIgnoreCase);

        public bool BadgeEnabled { get; set; }

        private static readonly string[] DefaultPalette = new string[]
        {
            "#2563EB", // 1: Blue
            "#7C3AED", // 2: Cyber Violet
            "#059669", // 3: Aurora Green
            "#D97706", // 4: Amber Gold
            "#DC2626", // 5: Crimson Rose
            "#0891B2"  // 6: Cyan Teal
        };

        public IndependentDesktopSettings()
            : this(GetDefaultSettingsFilePath())
        {
        }

        public IndependentDesktopSettings(string customFilePath)
        {
            _filePath = customFilePath;
            BadgeEnabled = true;
        }

        public static string GetDefaultSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(Path.Combine(appData, "WindicatorPlus"), "independent-desktops.xml");
        }

        public string FilePath
        {
            get { return _filePath; }
        }

        private static string MakeKey(string device, int localIndex)
        {
            string dev = (device ?? string.Empty).Trim().ToUpperInvariant();
            return string.Format("{0}#{1}", dev, localIndex);
        }

        public string GetDesktopName(string device, int localIndex)
        {
            lock (_lock)
            {
                string key = MakeKey(device, localIndex);
                DesktopDisplaySetting s;
                if (_items.TryGetValue(key, out s) && !string.IsNullOrEmpty(s.CustomName))
                {
                    return s.CustomName;
                }
                return null;
            }
        }

        public void SetDesktopName(string device, int localIndex, string name)
        {
            lock (_lock)
            {
                string key = MakeKey(device, localIndex);
                DesktopDisplaySetting s;
                if (!_items.TryGetValue(key, out s))
                {
                    s = new DesktopDisplaySetting { Device = device, LocalIndex = localIndex };
                    _items[key] = s;
                }
                s.CustomName = name != null ? name.Trim() : string.Empty;
            }
        }

        public string GetAccentColor(string device, int localIndex)
        {
            lock (_lock)
            {
                string key = MakeKey(device, localIndex);
                DesktopDisplaySetting s;
                if (_items.TryGetValue(key, out s) && !string.IsNullOrEmpty(s.AccentColor))
                {
                    return s.AccentColor;
                }

                int paletteIdx = Math.Max(0, localIndex % DefaultPalette.Length);
                return DefaultPalette[paletteIdx];
            }
        }

        public void SetAccentColor(string device, int localIndex, string hexColor)
        {
            lock (_lock)
            {
                string key = MakeKey(device, localIndex);
                DesktopDisplaySetting s;
                if (!_items.TryGetValue(key, out s))
                {
                    s = new DesktopDisplaySetting { Device = device, LocalIndex = localIndex };
                    _items[key] = s;
                }
                s.AccentColor = hexColor != null ? hexColor.Trim() : string.Empty;
            }
        }

        public static IndependentDesktopSettings Load(string filePath = null)
        {
            string path = filePath ?? GetDefaultSettingsFilePath();
            IndependentDesktopSettings settings = new IndependentDesktopSettings(path);
            settings.Reload();
            return settings;
        }

        public void Reload()
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                {
                    _items.Clear();
                    BadgeEnabled = true;
                    return;
                }

                Dictionary<string, DesktopDisplaySetting> tempItems = new Dictionary<string, DesktopDisplaySetting>(StringComparer.OrdinalIgnoreCase);
                bool tempBadgeEnabled = true;

                try
                {
                    XmlReaderSettings readerSettings = new XmlReaderSettings();
                    readerSettings.DtdProcessing = DtdProcessing.Prohibit;
                    readerSettings.XmlResolver = null;
                    readerSettings.MaxCharactersInDocument = 65536;

                    using (FileStream fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (XmlReader reader = XmlReader.Create(fs, readerSettings))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element)
                            {
                                if (reader.Name.Equals("settings", StringComparison.OrdinalIgnoreCase))
                                {
                                    string be = reader.GetAttribute("badgeEnabled");
                                    if (!string.IsNullOrEmpty(be))
                                    {
                                        bool b;
                                        if (bool.TryParse(be, out b))
                                        {
                                            tempBadgeEnabled = b;
                                        }
                                    }
                                }
                                else if (reader.Name.Equals("desktop", StringComparison.OrdinalIgnoreCase))
                                {
                                    string dev = reader.GetAttribute("device");
                                    string idxStr = reader.GetAttribute("localIndex");
                                    string name = reader.GetAttribute("name");
                                    string color = reader.GetAttribute("color");

                                    int localIdx;
                                    if (!string.IsNullOrEmpty(dev) && int.TryParse(idxStr, out localIdx) && localIdx >= 0)
                                    {
                                        string key = MakeKey(dev, localIdx);
                                        tempItems[key] = new DesktopDisplaySetting
                                        {
                                            Device = dev,
                                            LocalIndex = localIdx,
                                            CustomName = name ?? string.Empty,
                                            AccentColor = color ?? string.Empty
                                        };
                                    }
                                }
                            }
                        }
                    }

                    // 完整成功解析后，才原子替换内部状态，绝不半包覆盖
                    _items.Clear();
                    foreach (KeyValuePair<string, DesktopDisplaySetting> kvp in tempItems)
                    {
                        _items[kvp.Key] = kvp.Value;
                    }
                    BadgeEnabled = tempBadgeEnabled;
                }
                catch
                {
                    // 解析异常时保留已有完好数据，绝不半包清空
                }
            }
        }

        public void Save()
        {
            lock (_lock)
            {
                string dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempFile = _filePath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    XmlWriterSettings writerSettings = new XmlWriterSettings();
                    writerSettings.Indent = true;
                    writerSettings.Encoding = new UTF8Encoding(false);
                    writerSettings.OmitXmlDeclaration = false;

                    using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (XmlWriter writer = XmlWriter.Create(fs, writerSettings))
                    {
                        writer.WriteStartDocument();
                        writer.WriteStartElement("settings");
                        writer.WriteAttributeString("badgeEnabled", BadgeEnabled.ToString());

                        foreach (KeyValuePair<string, DesktopDisplaySetting> kvp in _items)
                        {
                            DesktopDisplaySetting item = kvp.Value;
                            writer.WriteStartElement("desktop");
                            writer.WriteAttributeString("device", item.Device ?? string.Empty);
                            writer.WriteAttributeString("localIndex", item.LocalIndex.ToString());
                            if (!string.IsNullOrEmpty(item.CustomName))
                            {
                                writer.WriteAttributeString("name", item.CustomName);
                            }
                            if (!string.IsNullOrEmpty(item.AccentColor))
                            {
                                writer.WriteAttributeString("color", item.AccentColor);
                            }
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteEndDocument();
                    }

                    // 原子替换：失败时保留原文件并直接上抛异常，移除破坏原子性的 delete fallback
                    if (File.Exists(_filePath))
                    {
                        File.Replace(tempFile, _filePath, null);
                    }
                    else
                    {
                        File.Move(tempFile, _filePath);
                    }
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        try { File.Delete(tempFile); } catch { }
                    }
                }
            }
        }
    }
}
