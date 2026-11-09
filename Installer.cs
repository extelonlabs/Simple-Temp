using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Temperature;

/// <summary>
/// Makes the downloaded exe its own installer, in three steps:
/// 1. RunFromDownload, as a normal user: asks you, then starts step 2 through the Windows admin prompt.
/// 2. CopyToProgramFiles, as admin, still in your download folder: does as little as it can there,
///    because any program can drop files in that folder. It copies the exe, then starts step 3.
/// 3. Finish, as admin, from the protected folder: does the rest.
/// </summary>
internal static class Installer
{
    public const string InstallArg = "--install";
    public const string FinishArg = "--install-finish";
    public const string UninstallArg = "--uninstall";

    /// <summary>For testing a build: open the app and don't try to install it.</summary>
    public const string NoInstallArg = "--no-install";

    // PawnIO is downloaded, not bundled: its license doesn't cover passing it on.
    // The hash means only this exact file can ever run. It comes from the Windows Package Manager listing.
    private const string PawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    private const string PawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    private const string PawnIoServiceKey = @"SYSTEM\CurrentControlSet\Services\PawnIO";
    private const int RestartNeeded = 3010;

    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SimpleTemp";
    private const int ErrorCancelled = 1223;
    private const string Title = "Simple Temp";

    private static string InstallDir => Path.GetDirectoryName(AdminTask.InstalledExe)!;
    private static string LogPath => Path.Combine(InstallDir, "install.log");
    private static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Simple Temp.lnk");
    private static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Simple Temp.lnk");

    private static bool PawnIoInstalled
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(PawnIoServiceKey);
            return key is not null;
        }
    }

    /// <summary>Step 1. True means this copy is done, and the program should quit.</summary>
    public static bool RunFromDownload()
    {
        bool installed = File.Exists(AdminTask.InstalledExe);

        // Same version already installed: this is just someone opening the app from their downloads.
        if (installed && SameFile(Environment.ProcessPath!, AdminTask.InstalledExe) && AdminTask.RunTask())
            return true;

        if (!AskToInstall(installed)) return true;

        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!, InstallArg) { UseShellExecute = true, Verb = "runas" };
            using var elevated = Process.Start(start);
            elevated!.WaitForExit();
            if (elevated.ExitCode != 0)
            {
                MessageBox.Show($"The install didn't finish (code {elevated.ExitCode}).\n\nPlease try again.", Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            MessageBox.Show("Install cancelled. Nothing was changed.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        // This copy is the one you clicked, so it can open the app in front of your other windows.
        AdminTask.RunTask();
        return true;
    }

    private static bool AskToInstall(bool update)
    {
        string driver = PawnIoInstalled
            ? ""
            : "\n•  Download and install the free PawnIO driver, which CPU temp needs";
        string message = update
            ? $"Update Simple Temp to version {Application.ProductVersion}?{(driver.Length > 0 ? "\n\nThis will also:" + driver : "")}\n\nWindows will ask you to confirm next."
            : "Install Simple Temp?\n\nThis will:" +
              "\n•  Put the app in Program Files" +
              "\n•  Add desktop and Start menu shortcuts" +
              "\n•  Let the app open as admin without asking, because CPU temp needs that" +
              driver +
              "\n\nWindows will ask you to confirm next. You can remove it any time from Settings > Apps.";

        return MessageBox.Show(message, Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
    }

    /// <summary>Step 2. Runs as admin from an unprotected folder, so it shows no windows and loads as little as it can.</summary>
    public static int CopyToProgramFiles()
    {
        if (!MainForm.IsAdmin()) return 1;
        try
        {
            StopRunningCopies();
            Directory.CreateDirectory(InstallDir);
            if (!AdminTask.IsInstalledCopy)
            {
                // Copying the bytes leaves behind the "downloaded from the internet" mark on the file.
                string temp = AdminTask.InstalledExe + ".new";
                using (var from = File.OpenRead(Environment.ProcessPath!))
                using (var to = File.Create(temp))
                    from.CopyTo(to);
                File.Move(temp, AdminTask.InstalledExe, overwrite: true);
            }
            File.WriteAllText(LogPath, $"Copied app to {InstallDir}{Environment.NewLine}");

            var start = new ProcessStartInfo(AdminTask.InstalledExe, FinishArg) { UseShellExecute = false, WorkingDirectory = InstallDir };
            using var finish = Process.Start(start);
            finish!.WaitForExit();
            return finish.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            TryLog("FAILED: " + ex.Message);
            return 2;
        }
    }

    /// <summary>Step 3. Runs as admin from the protected folder, with a small window so you can see it working.</summary>
    public static int Finish()
    {
        if (!MainForm.IsAdmin() || !AdminTask.IsInstalledCopy) return 1;
        using var window = new InstallWindow(FinishSteps);
        Application.Run(window);
        return window.ExitCode;
    }

    /// <summary>Returns a message to show when done, or null. Throws if the app itself couldn't be set up.</summary>
    private static async Task<string?> FinishSteps(Action<string> status)
    {
        status("Setting up Simple Temp...");
        WriteNotices();
        AutoStart.RegisterTask(AdminTask.TaskName, AdminTask.FromTaskArg, atSignIn: false, "Opens Simple Temp as admin with no prompt.");
        TryLog("Made task: " + AdminTask.TaskName);
        MakeShortcut(DesktopShortcut);
        MakeShortcut(StartMenuShortcut);
        TryLog("Made shortcuts");
        AddToInstalledApps();
        TryLog("Added to Settings > Apps");

        string? note = null;
        if (PawnIoInstalled)
        {
            TryLog("PawnIO already installed");
        }
        else
        {
            status("Installing the PawnIO driver...");
            note = await InstallPawnIo();
        }

        status("Checking the sensors...");
        LogSensorCheck();
        return note;
    }

    private static void WriteNotices()
    {
        // These licenses say the notices must travel with the app.
        foreach (string name in new[] { "LICENSE.txt", "THIRD-PARTY-NOTICES.txt" })
        {
            using var from = typeof(Installer).Assembly.GetManifestResourceStream(name)!;
            using var to = File.Create(Path.Combine(InstallDir, name));
            from.CopyTo(to);
        }
    }

    private static void MakeShortcut(string path)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = AdminTask.InstalledExe;
        link.WorkingDirectory = InstallDir;
        link.IconLocation = AdminTask.InstalledExe + ",0";
        link.Description = "CPU and GPU temperature and usage, and RAM";
        link.Save();
    }

    private static void AddToInstalledApps()
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Simple Temp");
        key.SetValue("DisplayVersion", Application.ProductVersion);
        key.SetValue("Publisher", "extelonlabs");
        key.SetValue("DisplayIcon", AdminTask.InstalledExe);
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{AdminTask.InstalledExe}\" {UninstallArg}");
        key.SetValue("URLInfoAbout", "https://github.com/extelonlabs/Simple-Temp");
        key.SetValue("EstimatedSize", (int)(new FileInfo(AdminTask.InstalledExe).Length / 1024), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>The app works without PawnIO, so failing here doesn't fail the install. It returns a message instead.</summary>
    private static async Task<string?> InstallPawnIo()
    {
        const string fallback = "Simple Temp is installed, but CPU temp will show \"—\" until the free PawnIO driver is installed.\n\nGet it at https://pawnio.eu";

        // Saved in the protected folder, so nothing can swap the file between the hash check and running it.
        string setup = Path.Combine(InstallDir, "PawnIO_setup.exe");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            byte[] bytes = await http.GetByteArrayAsync(PawnIoUrl);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != PawnIoSha256)
            {
                TryLog("PawnIO download failed the hash check. Not running it.");
                return fallback;
            }
            await File.WriteAllBytesAsync(setup, bytes);

            var start = new ProcessStartInfo(setup, ["-install", "-silent"]) { UseShellExecute = false, CreateNoWindow = true };
            using var process = Process.Start(start);
            process!.WaitForExit(TimeSpan.FromMinutes(2));
            if (!process.HasExited)
            {
                TryLog("PawnIO setup timed out");
                return fallback;
            }

            TryLog($"PawnIO setup exit code: {process.ExitCode}");
            if (process.ExitCode == RestartNeeded) return "Simple Temp is installed. Restart your PC so CPU temp can show.";
            return process.ExitCode == 0 ? null : fallback;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or Win32Exception)
        {
            TryLog("PawnIO failed: " + ex.Message);
            return fallback;
        }
        finally
        {
            try { File.Delete(setup); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { TryLog("Couldn't remove PawnIO setup: " + ex.Message); }
        }
    }

    private static void LogSensorCheck()
    {
        try
        {
            using var reader = new SensorReader();
            reader.Read();
            Thread.Sleep(1200); // Usage values need two reads with a gap to mean anything.
            TryLog("Sensor check: " + MiniBar.Format(reader.Read()));
        }
        catch (Exception ex)
        {
            // Only a note for the log. The app reports sensor problems itself when it opens.
            TryLog("Sensor check failed: " + ex.Message);
        }
    }

    public static int Uninstall()
    {
        if (!MainForm.IsAdmin())
        {
            try
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!, UninstallArg) { UseShellExecute = true, Verb = "runas" };
                using var elevated = Process.Start(start);
                elevated!.WaitForExit();
                return elevated.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return 0;
            }
        }

        var answer = MessageBox.Show("Remove Simple Temp from this PC?\n\nThe PawnIO driver stays, because other apps may use it.", Title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return 0;

        StopRunningCopies();
        AutoStart.DeleteTask(AdminTask.TaskName);
        AutoStart.DeleteTask(AutoStart.TaskName);
        File.Delete(DesktopShortcut);
        File.Delete(StartMenuShortcut);
        Registry.LocalMachine.DeleteSubKey(UninstallKey, throwOnMissingSubKey: false);

        MessageBox.Show("Simple Temp is removed.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

        // A running exe can't delete itself, so a hidden helper does it a moment after this one quits.
        // It tries a few times, because something else can hold the folder open for a moment.
        var cleanup = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            $"/c ping -n 3 127.0.0.1 >nul & for /L %i in (1,1,5) do @(rd /s /q \"{InstallDir}\" 2>nul & ping -n 2 127.0.0.1 >nul)")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Environment.SystemDirectory,
        };
        using (Process.Start(cleanup)) { }
        return 0;
    }

    private static void StopRunningCopies()
    {
        AutoStart.StopTask(AdminTask.TaskName);
        AutoStart.StopTask(AutoStart.TaskName);

        foreach (var process in Process.GetProcessesByName("SimpleTemp"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, AdminTask.InstalledExe, StringComparison.OrdinalIgnoreCase)) continue;
                    if (process.WaitForExit(2000)) continue; // Stopping the tasks usually ended it already.
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // It quit while we were looking at it.
                }
            }
        }
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            using var first = File.OpenRead(a);
            using var second = File.OpenRead(b);
            return first.Length == second.Length && SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryLog(string line)
    {
        try { File.AppendAllText(LogPath, line + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must never break the install. */ }
    }

    /// <summary>A small window that runs the setup steps, so the install doesn't look frozen.</summary>
    private sealed class InstallWindow : Form
    {
        private readonly Func<Action<string>, Task<string?>> _steps;
        private readonly Label _status;

        public int ExitCode { get; private set; }

        public InstallWindow(Func<Action<string>, Task<string?>> steps)
        {
            _steps = steps;
            float scale = DeviceDpi / 96f;

            Text = Title;
            BackColor = MainForm.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(340 * scale), (int)(80 * scale));

            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = MainForm.TextMain,
                Font = new Font("Segoe UI", 10f),
            };
            Controls.Add(_status);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // On a background thread, so this window stays alive during the download.
                void Status(string text) => BeginInvoke(() => _status.Text = text);
                string? note = await Task.Run(() => _steps(Status));
                if (note is not null) MessageBox.Show(this, note, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ExitCode = 3;
                TryLog("FAILED: " + ex);
                MessageBox.Show(this, "The install didn't finish.\n\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _status.Font.Dispose();
            base.Dispose(disposing);
        }
    }
}
