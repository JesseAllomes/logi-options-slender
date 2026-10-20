using System.Diagnostics;
using Microsoft.Win32;

namespace Slender.Settings.Logic;

/// <summary>Where Logi Options+ stands on this PC, read without admin rights.</summary>
public enum OptionsPlusStatus
{
    NotInstalled,

    /// <summary>Service disabled, startup entry off, nothing running: it never loads.</summary>
    Off,

    /// <summary>Not running now, but its service or startup entry would start it again.</summary>
    WillStart,

    Running,
}

/// <summary>
/// Reads the Options+ state (install folder, its processes, the updater service's start type and the Download Assistant startup
/// switch) and holds the elevated "squash" script: the same as scripts/Set-OptionsPlus.ps1 -Off (user 2026-10-20: Options+ must
/// never load except for a firmware update).
/// </summary>
public static class OptionsPlusState
{
    public const string AppPath = @"C:\Program Files\LogiOptionsPlus\logioptionsplus.exe";
    public const string ServiceName = "OptionsPlusUpdaterService";
    public const string StartupName = "Logi Download Assistant";

    public static readonly string[] Processes =
    [
        "logioptionsplus", "logioptionsplus_agent", "logioptionsplus_appbroker", "logioptionsplus_updater",
        "LogiPluginService", "LogiPluginServiceExt", "logi_crashpad_handler", "logi_download_assistant",
    ];

    /// <summary>Turns everything off. Run elevated (Windows PowerShell, hidden).</summary>
    public const string SquashScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        $svc = 'OptionsPlusUpdaterService'
        Stop-Service $svc -Force; Set-Service $svc -StartupType Disabled
        if (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name 'Logi Download Assistant') {
            $k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
            New-Item -Force $k | Out-Null
            Set-ItemProperty $k -Name 'Logi Download Assistant' -Type Binary -Value ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0))
        }
        'logioptionsplus','logioptionsplus_agent','logioptionsplus_appbroker','logioptionsplus_updater','LogiPluginService',
        'LogiPluginServiceExt','logi_crashpad_handler','logi_download_assistant' | ForEach-Object { Get-Process $_ | Stop-Process -Force }
        """;

    public static OptionsPlusStatus Read()
    {
        if (AnyRunning())
        {
            return OptionsPlusStatus.Running;
        }

        if (!File.Exists(AppPath))
        {
            return OptionsPlusStatus.NotInstalled;
        }

        return ServiceDisabled() && StartupOff() ? OptionsPlusStatus.Off : OptionsPlusStatus.WillStart;
    }

    public static string Describe(OptionsPlusStatus status) => status switch
    {
        OptionsPlusStatus.NotInstalled => "Not installed.",
        OptionsPlusStatus.Off => "Off. It only runs when you choose Update firmware.",
        OptionsPlusStatus.WillStart => "Not running, but set to start again with Windows.",
        _ => "Running, and fighting Slender for the mouse and keyboard.",
    };

    private static bool AnyRunning()
    {
        foreach (string name in Processes)
        {
            Process[] found = Process.GetProcessesByName(name);
            bool any = found.Length > 0;
            foreach (Process p in found)
            {
                p.Dispose();
            }

            if (any)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Service start type 4 = Disabled (no service at all counts as disabled).</summary>
    private static bool ServiceDisabled()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
        return key?.GetValue("Start") is not int start || start == 4;
    }

    /// <summary>The startup entry is absent, or switched off (StartupApproved first byte odd = disabled, as Task Manager writes it).</summary>
    private static bool StartupOff()
    {
        using RegistryKey? run = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        if (run?.GetValue(StartupName) is null)
        {
            return true;
        }

        using RegistryKey? approved = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run");
        return approved?.GetValue(StartupName) is byte[] { Length: > 0 } b && (b[0] & 1) == 1;
    }
}
