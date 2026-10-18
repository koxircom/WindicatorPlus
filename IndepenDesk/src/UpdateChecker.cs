using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace IndepenDesk;

/// <summary>GitHub Releases üzerinden güncelleme denetimi.</summary>
internal static class UpdateChecker
{
    public const string RepoOwner = "harungecit";
    public const string RepoName = "IndepenDesk";
    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";

    public static string CurrentVersion
    {
        get
        {
            var v = typeof(UpdateChecker).Assembly.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Son sürümü denetler ve sonucu kullanıcıya bildirir.</summary>
    public static async Task CheckAndNotifyAsync(IWin32Window owner)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("IndepenDesk-UpdateCheck");
            http.Timeout = TimeSpan.FromSeconds(10);

            string json = await http.GetStringAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string htmlUrl = doc.RootElement.GetProperty("html_url").GetString() ?? RepoUrl;

            string latest = tag.TrimStart('v', 'V');
            if (Version.TryParse(latest, out var latestVer) &&
                Version.TryParse(CurrentVersion, out var currentVer) &&
                latestVer > currentVer)
            {
                var answer = MessageBox.Show(owner,
                    L.F("update.available", latest, CurrentVersion),
                    L.T("update.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(owner, L.F("update.none", CurrentVersion),
                    L.T("update.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch
        {
            MessageBox.Show(owner, L.T("update.error"),
                L.T("update.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
