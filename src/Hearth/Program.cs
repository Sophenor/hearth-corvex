namespace Hearth;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--self-test") || args.Contains("--live-test"))
        { Environment.ExitCode = Verification.Run(args).GetAwaiter().GetResult(); return; }
        using var mutex = new Mutex(true, "Local\\HearthCorvex-" + Environment.UserName, out var first);
        if (!first) { MessageBox.Show("Hearth is already open. Look for it on your taskbar.", "Hearth"); return; }
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        var uiReport = args.Contains("--ui-test") ? args.SkipWhile(a => a != "--report").Skip(1).FirstOrDefault() : null;
        Application.Run(new MainWindow(uiReport));
    }    
}
