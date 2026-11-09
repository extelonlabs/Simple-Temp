using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace Temperature;

/// <summary>
/// The "Start with Windows" option. It is a second scheduled task that runs when you sign in.
/// The task itself is the saved setting: no task means off, and "--mini" in it means start as the bar.
/// </summary>
internal static class AutoStart
{
    public const string MiniArg = "--mini";

    public const string TaskName = "Simple Temp Autostart";
    private const int CreateOrUpdate = 6;
    private const int InteractiveToken = 3;

    /// <summary>Making this task needs admin, and it must point at the protected installed copy.</summary>
    public static bool CanChange => MainForm.IsAdmin() && AdminTask.IsInstalledCopy;

    public static (bool On, bool Mini) Read()
    {
        try
        {
            string xml = Folder().GetTask(TaskName).Xml;
            return (true, xml.Contains(MiniArg));
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException)
        {
            return (false, false);
        }
    }

    public static void Set(bool on, bool mini)
    {
        if (on)
        {
            string args = mini ? $"{AdminTask.FromTaskArg} {MiniArg}" : AdminTask.FromTaskArg;
            RegisterTask(TaskName, args, atSignIn: true, "Starts Simple Temp when you sign in.");
        }
        else if (Read().On)
        {
            DeleteTask(TaskName);
        }
    }

    public static void RegisterTask(string name, string args, bool atSignIn, string description) =>
        Folder().RegisterTask(name, TaskXml(args, atSignIn, description), CreateOrUpdate, null, null, InteractiveToken);

    /// <summary>Ends the app if this task started it. Fine if the task doesn't exist.</summary>
    public static void StopTask(string name)
    {
        try
        {
            Folder().GetTask(name).Stop(0);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException)
        {
            // No such task, so nothing to stop.
        }
    }

    /// <summary>Fine if the task doesn't exist.</summary>
    public static void DeleteTask(string name)
    {
        try
        {
            Folder().DeleteTask(name, 0);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException)
        {
            // No such task, so nothing to delete.
        }
    }

    private static dynamic Folder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    private static string TaskXml(string args, bool atSignIn, string description)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string user = SecurityElement.Escape(identity.Name);
        string exe = SecurityElement.Escape(AdminTask.InstalledExe);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(AdminTask.InstalledExe)!);
        string triggers = atSignIn
            ? $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>"
            : "<Triggers />";

        // No time limit: by default Windows would close the app after 3 days.
        return $"""
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>{description}</Description></RegistrationInfo>
              {triggers}
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>{args}</Arguments>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
