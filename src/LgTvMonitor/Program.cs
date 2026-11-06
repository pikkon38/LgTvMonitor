namespace LgTvMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cmd = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
        switch (cmd)
        {
            case "--on":
            case "--boot":
                return RunCli(on: true, boot: cmd == "--boot");
            case "--off":
                return RunCli(on: false, boot: false);
            case "--register-boot-task":   // run by the installer (as admin)
                return BootTask.Register();
            case "--unregister-boot-task": // run by the uninstaller
                return BootTask.Unregister();
            case "--help":
            case "/?":
                MessageBox.Show(
                    "LgTvMonitor.exe            open the app (it keeps running in the tray)\n" +
                    "LgTvMonitor.exe --autostart  start as Windows does at sign-in\n" +
                    "LgTvMonitor.exe --setup    open setup\n" +
                    "LgTvMonitor.exe --on       turn the TV on (Wake-on-LAN)\n" +
                    "LgTvMonitor.exe --off      turn the TV off\n" +
                    "LgTvMonitor.exe --boot     used by the startup task\n\n" +
                    $"Settings and log: {Config.Dir}",
                    "LG TV Monitor");
                return 0;
        }

        ApplicationConfiguration.Initialize();
        using var single = new Mutex(true, @"Local\LgTvMonitorTray", out var first);
        if (!first)
        {
            // Already running: bring its window forward instead of starting a second copy.
            if (cmd != "--autostart") PowerEventWindow.AskRunningCopyToShow();
            return 0;
        }
        var launch = cmd switch { "--autostart" => Launch.Autostart, "--setup" => Launch.Setup, _ => Launch.Manual };
        Application.Run(new TrayApp(launch));
        return 0;
    }

    /// <summary>Command-line mode. --boot is run by a SYSTEM scheduled task before anyone signs in.</summary>
    private static int RunCli(bool on, bool boot)
    {
        var cfg = Config.Load();
        if (boot && !cfg.OnOnStartup) { Log.Write("Boot: turning the TV on is disabled in settings"); return 0; }
        if (!cfg.IsConfigured) { Log.Write("CLI: not configured"); return 2; }

        if (on)
        {
            // At boot the network adapter may not be up yet.
            for (int i = 0; i < 60 && !Network.HasUsableIPv4(); i++) Thread.Sleep(1000);
        }
        var tv = new TvController(() => cfg);
        var task = on ? tv.PowerOnAsync(boot ? "PC started" : "command line") : tv.PowerOffAsync("command line", automatic: false);
        task.Wait(TimeSpan.FromSeconds(45));
        return 0;
    }
}
