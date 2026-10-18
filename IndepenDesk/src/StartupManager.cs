using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace IndepenDesk;

/// <summary>
/// Windows ile otomatik başlatma. Varsayılan açıktır; tray menüsünden kapatılabilir.
/// Normal kurulumlarda HKCU\...\Run anahtarı kullanılır. MSIX paketinde bu anahtar
/// sanallaştırıldığı için kayıt defterine dokunulmaz; başlangıç, manifest'teki
/// StartupTask ile gelir ve Windows Ayarları > Uygulamalar > Başlangıç'tan yönetilir.
/// </summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "IndepenDesk";

    public static readonly bool IsPackaged = DetectPackaged();

    public static bool Enabled { get; private set; } = SettingsStore.GetBool("startup", true);

    /// <summary>Uygulama açılışında çağrılır: tercih neyse kayıt defterini ona eşitler
    /// (exe taşınmışsa yol da tazelenir).</summary>
    public static void ApplyOnLaunch()
    {
        if (!IsPackaged)
            Apply();
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        SettingsStore.SetBool("startup", enabled);
        Apply();
    }

    private static void Apply()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (Enabled && Environment.ProcessPath is { } exe)
                key.SetValue(ValueName, $"\"{exe}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }

    /// <summary>MSIX'te geçiş Windows Ayarları'ndan yapılır; Başlangıç sayfasını açar.</summary>
    public static void OpenWindowsStartupSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:startupapps") { UseShellExecute = true });
        }
        catch { }
    }

    private static bool DetectPackaged()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
        }
        catch
        {
            return false; // Windows 7 vb.: API yok => paketsiz
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
