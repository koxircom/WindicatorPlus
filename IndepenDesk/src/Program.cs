namespace IndepenDesk;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool isBridgeMode = args.Any(a => string.Equals(a, "--windicator-bridge", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, "IndepenDesk_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("IndepenDesk zaten çalışıyor.", "IndepenDesk",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(isBridgeMode));
    }
}
