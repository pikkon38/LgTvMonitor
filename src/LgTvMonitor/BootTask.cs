using System.Diagnostics;
using System.Security;

namespace LgTvMonitor;

/// <summary>
/// The scheduled task that turns the TV on when the PC starts. It runs as SYSTEM before anyone signs in,
/// so the TV is on for the sign-in screen. The installer creates it (needs admin) by running
/// <c>LgTvMonitor.exe --register-boot-task</c>, and removes it on uninstall.
/// </summary>
static class BootTask
{
    public const string Name = "LG TV Monitor - turn TV on at startup";

    public static int Register()
    {
        var exe = SecurityElement.Escape(Environment.ProcessPath ?? "");
        // Triggers: normal boot, plus Kernel-Boot event 27, which is also logged on "Fast Startup" boots.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Wakes the LG TV (used as a monitor) when the PC starts.</Description></RegistrationInfo>
              <Triggers>
                <BootTrigger><Enabled>true</Enabled></BootTrigger>
                <EventTrigger>
                  <Enabled>true</Enabled>
                  <Subscription>&lt;QueryList&gt;&lt;Query Id="0" Path="System"&gt;&lt;Select Path="System"&gt;*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and EventID=27]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
                </EventTrigger>
              </Triggers>
              <Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT3M</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{exe}"</Command><Arguments>--boot</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), $"lgtvmonitor-task-{Environment.ProcessId}.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            var code = Schtasks($"/Create /F /TN \"{Name}\" /XML \"{file}\"");
            Log.Write(code == 0 ? "Startup task registered" : $"Couldn't register the startup task (schtasks exit {code})");
            return code;
        }
        finally { try { File.Delete(file); } catch { } }
    }

    public static int Unregister()
    {
        var code = Schtasks($"/Delete /F /TN \"{Name}\"");
        Log.Write("Startup task removed");
        return code;
    }

    static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
