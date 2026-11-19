namespace Slender.Settings.Logic;

/// <summary>Where <c>--device &lt;id&gt;</c> opens Settings: a mouse or keyboard page, or the headset page.</summary>
public readonly record struct DeviceTarget(DeviceKind? Kind, bool Headset, bool Buds = false);

/// <summary>
/// <c>Slender.Settings --device &lt;id&gt;</c> opens Settings at that device (ids as in status.json: "mx-master-4",
/// "ergo-k860", "yealink-wh64"). Other apps (a dashboard showing battery levels) use it to link straight to a device.
/// A second launch hands the id to the running Settings through a small file, then signals it to come to the front.
/// </summary>
public static class DeviceRequest
{
    /// <summary>The known id after <c>--device</c> (or <c>--device=id</c>); null when absent or unknown.</summary>
    public static string? Parse(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a.StartsWith("--device=", StringComparison.OrdinalIgnoreCase))
            {
                return Known(a["--device=".Length..]);
            }

            if (string.Equals(a, "--device", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return Known(args[i + 1]);
            }
        }

        return null;
    }

    public static DeviceTarget? Target(string? id) => Known(id) switch
    {
        DeviceIds.Mouse => new DeviceTarget(DeviceKind.Mouse, false),
        DeviceIds.Keyboard => new DeviceTarget(DeviceKind.Keyboard, false),
        DeviceIds.Headset => new DeviceTarget(null, true),
        DeviceIds.Buds => new DeviceTarget(null, false, true),
        _ => null,
    };

    /// <summary>Writes the hand-off for the running Settings. False when it cannot be written (the window still comes forward).</summary>
    public static bool Hand(string path, string id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, id);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads and removes the hand-off; null when there is none or it names no known device.</summary>
    public static string? Take(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return Known(text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Known(string? id) =>
        id?.Trim().ToLowerInvariant() is DeviceIds.Mouse or DeviceIds.Keyboard or DeviceIds.Headset or DeviceIds.Buds ? id.Trim().ToLowerInvariant() : null;
}
