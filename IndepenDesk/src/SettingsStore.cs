using System.Text.Json;
using System.Text.Json.Nodes;

namespace IndepenDesk;

/// <summary>
/// settings.json okuma/yazma. Yazarken dosyadaki diğer anahtarları korur
/// (dil tercihi ile başlangıç tercihi aynı dosyayı paylaşır).
/// </summary>
internal static class SettingsStore
{
    private static readonly string File_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IndepenDesk", "settings.json");

    public static string? GetString(string key)
    {
        try
        {
            if (File.Exists(File_) &&
                JsonNode.Parse(File.ReadAllText(File_)) is JsonObject obj &&
                obj[key] is JsonValue v)
                return v.GetValue<string>();
        }
        catch { }
        return null;
    }

    public static bool GetBool(string key, bool defaultValue)
    {
        try
        {
            if (File.Exists(File_) &&
                JsonNode.Parse(File.ReadAllText(File_)) is JsonObject obj &&
                obj[key] is JsonValue v)
                return v.GetValue<bool>();
        }
        catch { }
        return defaultValue;
    }

    public static void SetString(string key, string value) => Set(key, JsonValue.Create(value));

    public static void SetBool(string key, bool value) => Set(key, JsonValue.Create(value));

    private static void Set(string key, JsonNode? value)
    {
        try
        {
            JsonObject obj;
            try
            {
                obj = (File.Exists(File_) ? JsonNode.Parse(File.ReadAllText(File_)) as JsonObject : null)
                      ?? new JsonObject();
            }
            catch
            {
                obj = new JsonObject();
            }
            obj[key] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
            File.WriteAllText(File_, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
