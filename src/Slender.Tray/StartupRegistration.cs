using Microsoft.Win32;

namespace Slender.Tray;

/// <summary>Minimal access to one registry Run key. Tests supply a fake.</summary>
public interface IRunKey
{
    string? GetValue(string name);
    void SetValue(string name, string value);
    void DeleteValue(string name);
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
public sealed class RegistryRunKey : IRunKey
{
    private const string SubKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SubKeyPath, writable: false);
        return key?.GetValue(name) as string;
    }

    public void SetValue(string name, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(SubKeyPath, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SubKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

/// <summary>Launch-at-login entry: value "Slender" = quoted path of this exe.</summary>
public sealed class StartupRegistration
{
    public const string ValueName = "Slender";

    private readonly IRunKey _runKey;

    public StartupRegistration(IRunKey runKey, string exePath)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        _runKey = runKey;
        Command = $"\"{exePath}\"";
    }

    public string Command { get; }

    /// <summary>True only when the Run value points at this exe.</summary>
    public bool IsEnabled => string.Equals(_runKey.GetValue(ValueName), Command, StringComparison.OrdinalIgnoreCase);

    public void Enable() => _runKey.SetValue(ValueName, Command);

    public void Disable() => _runKey.DeleteValue(ValueName);

    public static StartupRegistration ForCurrentProcess() => new(
        new RegistryRunKey(),
        Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable."));
}
