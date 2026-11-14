using BankManager.Core;

namespace BankManager.Gui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = de;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = de;
        System.Globalization.CultureInfo.CurrentCulture = de;
        System.Globalization.CultureInfo.CurrentUICulture = de;
        if (args.Contains(Updater.SelfTestArg, StringComparer.OrdinalIgnoreCase))
        {
            _ = typeof(TresorDatei).Assembly.GetName();
            using (new Label()) { }
            Environment.Exit(Updater.SelfTestExitCode);
        }
        string? screenshots = ArgValue(args, "--screenshots");
        if (screenshots != null)
        {
            ApplicationConfiguration.Initialize();
            Environment.Exit(Bildschirmfotos.Erstellen(screenshots));
        }
        bool afterUpdate = Updater.WaitForPreviousInstance(args);
        string? dataDir = ArgValue(args, "--daten");

        var instanceKey = "BankManager_" + (dataDir == null ? "standard" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..12]);
        using var mutex = new Mutex(true, @"Local\" + instanceKey, out bool firstInstance);
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + instanceKey + "_Zeigen");
        if (!firstInstance)
        {
            showEvent.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        Updater.CleanupLeftovers();
        Application.ThreadException += (_, e) =>
            MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten:\n\n" + e.Exception.Message, "Bank-Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var sitzung = new Sitzung(dataDir);
        Theme.Set(sitzung.Einstellungen.Design);

        using var form = new MainForm(sitzung, Updater.SourceUrl(args)) { StartArgs = args, NachUpdate = afterUpdate };
        var waiter = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                try { form.BeginInvoke(form.Zeigen); } catch { return; }
            }
        }) { IsBackground = true };
        waiter.Start();
        Application.Run(form);
    }

    internal static string? ArgValue(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
