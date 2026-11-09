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

    private const string TaskName = "Simple Temp Autostart";
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
        var folder = Folder();
        if (on)
        {
            folder.RegisterTask(TaskName, TaskXml(mini), CreateOrUpdate, null, null, InteractiveToken);
        }
        else if (Read().On)
        {
            folder.DeleteTask(TaskName, 0);
        }
    }

    private static dynamic Folder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    private static string TaskXml(bool mini)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string user = SecurityElement.Escape(identity.Name);
        string exe = SecurityElement.Escape(AdminTask.InstalledExe);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(AdminTask.InstalledExe)!);
        string args = mini ? $"{AdminTask.FromTaskArg} {MiniArg}" : AdminTask.FromTaskArg;

        // No time limit: by default Windows would close the app after 3 days.
        return $"""
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Starts Simple Temp when you sign in.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
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
