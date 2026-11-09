using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Temperature;

/// <summary>
/// install.ps1 makes a Windows scheduled task that starts the app with admin rights and no prompt.
/// A normal start of the installed app asks that task to start the real window, then quits.
/// </summary>
internal static class AdminTask
{
    /// <summary>The task passes this. It stops the app from asking the task again, forever.</summary>
    public const string FromTaskArg = "--from-task";

    private const string TaskName = "Simple Temp";

    public static string InstalledExe { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Simple Temp", "SimpleTemp.exe");

    public static bool IsInstalledCopy =>
        string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>True if the task took over. False means carry on as a normal app.</summary>
    public static bool TryStart()
    {
        // Only the installed copy hands over, so a fresh build you are testing runs itself.
        if (MainForm.IsAdmin() || !IsInstalledCopy)
            return false;

        try
        {
            // You just clicked this copy, so it may take focus. This passes that on to the new window.
            AllowSetForegroundWindow(AnyProcess);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), ["/run", "/tn", TaskName])
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var schtasks = Process.Start(start);
            return schtasks is not null && schtasks.WaitForExit(5000) && schtasks.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
