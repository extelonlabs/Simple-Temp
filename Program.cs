using System.Globalization;

namespace Temperature;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--probe")) return Probe();
        if (args.Contains(Installer.InstallArg)) return Installer.CopyToProgramFiles();

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Contains(Installer.FinishArg)) return Installer.Finish();
        if (args.Contains(Installer.UninstallArg)) return Installer.Uninstall();

        bool fromTask = args.Contains(AdminTask.FromTaskArg);
        bool skipInstall = args.Contains(Installer.NoInstallArg);
        if (!fromTask && !skipInstall && !AdminTask.IsInstalledCopy && Installer.RunFromDownload()) return 0;
        if (!fromTask && AdminTask.TryStart()) return 0;

        bool startMini = args.Contains(AutoStart.MiniArg);
        Application.Run(new MainForm(bringToFront: fromTask && !startMini, startMini));
        return 0;
    }

    /// <summary>Prints the values once, with no window. Pipe the output to see it, or a file.</summary>
    private static int Probe()
    {
        using var reader = new SensorReader();
        reader.Read();
        Thread.Sleep(1200); // Usage values need two reads with a gap to mean anything.
        var r = reader.Read();

        static string Show(float? v) => v?.ToString("0.0", CultureInfo.InvariantCulture) ?? "null";
        Console.WriteLine($"cpu_name={r.CpuName}");
        Console.WriteLine($"cpu_temp={Show(r.CpuTemp)}");
        Console.WriteLine($"cpu_load={Show(r.CpuLoad)}");
        Console.WriteLine($"gpu_name={r.GpuName}");
        Console.WriteLine($"gpu_temp={Show(r.GpuTemp)}");
        Console.WriteLine($"gpu_load={Show(r.GpuLoad)}");
        Console.WriteLine($"ram_load={Show(r.RamLoad)}");
        Console.WriteLine($"ram_used_gb={Show(r.RamUsedGb)}");
        Console.WriteLine($"ram_total_gb={Show(r.RamTotalGb)}");
        Console.WriteLine($"mini_bar={MiniBar.Format(r)}");
        return 0;
    }
}
